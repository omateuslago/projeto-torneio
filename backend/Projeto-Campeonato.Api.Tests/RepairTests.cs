using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Projeto_Campeonato.Api.Features;
using System.Text.Json;

namespace Projeto_Campeonato.Api.Tests;
public sealed partial class AutopilotTests
{
    private static SolverResult RepairAnswer(RepairInput input)
    {
        var rows = input.Schedule.ExistingSchedule.Where(m => m.CountsForResilience && m.MatchId != input.Incident.MatchId).OrderBy(m => m.StartsAt).Select((m,i) => new SolverAssignment(m.MatchId, (i == 0 ? m.StartsAt.AddHours(3) : m.StartsAt).ToString("O"), m.PlayingAreaId, m.RefereeId)).ToArray();
        return new("optimal", rows, [], Fairness, new(50, 100, 0, null, rows.Length * 3, rows.Length, ["Análise."]));
    }
    private async Task<(Guid Id, Guid Area, ScheduleDto[] Rows)> RepairSetup(HttpClient c)
    {
        var t = await Setup(c, 3); var rows = await Agenda(c,t.Id);
        for(var i=0; i<rows.Length; i++)
            (await c.PutAsJsonAsync($"/api/matches/{rows[i].MatchId}/schedule", new { startsAt=DateTime.Parse(Options.HorizonStart).ToUniversalTime().AddHours(i).ToString("O"), playingAreaId=t.Area })).EnsureSuccessStatusCode();
        return (t.Id,t.Area,await Agenda(c,t.Id));
    }
    private static RepairOptions IncidentOptions(Guid area) => new(Options, new("playingAreaUnavailable",area,Options.HorizonStart,"2026-11-14T19:00:00Z"));
    private Task<HttpResponseMessage> RepairGenerate(HttpClient c, Guid id, RepairOptions o) => c.PostAsJsonAsync($"/api/tournaments/{id}/schedule/repair/generate",o);
    private Task<HttpResponseMessage> RepairApply(HttpClient c, Guid id, string token) => c.PostAsJsonAsync($"/api/tournaments/{id}/schedule/repair/apply",new {proposalToken=token});
    private async Task<AuditResponse[]> AuditRows(HttpClient c,Guid match) => await Read<AuditResponse[]>(await c.GetAsync($"/api/matches/{match}/audit-log"));
    [Fact]
    public async Task RepairPreviewApplyOnlyChangedRowsAuditedAndReplayRejected()
    {
        using var c=Client(new Stub { RepairResponse=RepairAnswer }); var t=await RepairSetup(c);
        var before=await Agenda(c,t.Id);
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,IncidentOptions(t.Area)));
        Assert.Single(p.Changes); Assert.Equal(1,p.Metrics.ImpactedCount); Assert.Equal(180,p.Metrics.TotalDisplacementMinutes);
        Assert.Equal(2,p.Metrics.PreservedCount); Assert.Equal(before,await Agenda(c,t.Id));
        foreach(var m in before) Assert.DoesNotContain(await AuditRows(c,m.MatchId), a=>a.EventType=="MatchRescheduled");
        (await RepairApply(c,t.Id,p.ProposalToken!)).EnsureSuccessStatusCode();
        foreach(var m in before) Assert.Equal(p.Changes.Any(x=>x.After.MatchId==m.MatchId)?1:0,(await AuditRows(c,m.MatchId)).Count(a=>a.EventType=="MatchRescheduled"));
        Assert.Equal(HttpStatusCode.Conflict,(await RepairApply(c,t.Id,p.ProposalToken!)).StatusCode);
    }
    [Fact]
    public async Task RepairNoOpHasNoToken()
    {
        using var c=Client(new Stub { RepairResponse=_=>new("optimal",[],["Nenhuma alteração necessária."]) }); var t=await RepairSetup(c);
        var options=IncidentOptions(t.Area) with { Incident=new("playingAreaUnavailable",t.Area,"2026-11-15T00:00:00Z",Options.HorizonEnd) };
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,options)); Assert.Empty(p.Changes);Assert.Null(p.ProposalToken);Assert.Equal(0,p.Metrics.ChangedCount);
    }
    [Theory]
    [InlineData("invalidResource")]
    [InlineData("invalidDelay")]
    [InlineData("missingInterval")]
    public async Task RepairRejectsInvalidIncident(string kind)
    {
        using var c=Client(new Stub { RepairResponse=RepairAnswer });var t=await RepairSetup(c);
        var incident=kind switch { "invalidResource"=>new RepairIncident("teamUnavailable",Guid.NewGuid(),Options.HorizonStart,Options.HorizonEnd), "invalidDelay"=>new("matchDelayed",MatchId:t.Rows[0].MatchId,DelayMinutes:241), _=>new("refereeUnavailable",t.Area) };
        Assert.Equal(HttpStatusCode.BadRequest,(await RepairGenerate(c,t.Id,new(Options,incident))).StatusCode);
    }
    [Theory]
    [InlineData("incident")]
    [InlineData("collision")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public async Task RepairInvalidSchedulerResponseNeverWrites(string mode)
    {
        var stub=new Stub { RepairResponse=input=> {
            var good=RepairAnswer(input);var rows=good.ScheduledMatches;
            if(mode=="incident") rows[0]=rows[0] with { StartsAt=Options.HorizonStart };
            if(mode=="collision") rows[0]=rows[0] with { StartsAt=rows[1].StartsAt };
            if(mode=="foreign") rows[0]=rows[0] with { PlayingAreaId=Guid.NewGuid() };
            if(mode=="missing") rows=rows.Take(1).ToArray();
            return good with { ScheduledMatches=rows };
        }};
        using var c=Client(stub);var t=await RepairSetup(c);var before=await Agenda(c,t.Id);
        Assert.Equal(HttpStatusCode.BadGateway,(await RepairGenerate(c,t.Id,IncidentOptions(t.Area))).StatusCode);
        Assert.Equal(before,await Agenda(c,t.Id));
    }
    [Fact]
    public async Task RepairTokensAreScopedAndSnapshotProtected()
    {
        using var c=Client(new Stub { RepairResponse=RepairAnswer });var t=await RepairSetup(c);
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,IncidentOptions(t.Area)));
        Assert.Equal(HttpStatusCode.BadRequest,(await RepairApply(c,t.Id,p.ProposalToken!+"tampered")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await RepairApply(c,Guid.NewGuid(),p.ProposalToken!)).StatusCode);
        (await c.PostAsJsonAsync($"/api/tournaments/{t.Id}/teams/{t.Rows[0].HomeTeamId}/availability",new { startsAt="2026-11-16T00:00:00Z",endsAt="2026-11-16T01:00:00Z" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict,(await RepairApply(c,t.Id,p.ProposalToken!)).StatusCode);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyRevalidatesProtectedIncidentAndRollsBackInvalidProposal(bool writerFailure)
    {
        using var c=Client(new Stub { RepairResponse=RepairAnswer });var t=await RepairSetup(c);
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,IncidentOptions(t.Area)));
        // Test-only access to server key ring simulates an invalid internal proposal.
        var protector=factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("ScheduleRepair.v1");
        var envelope=JsonSerializer.Deserialize<ProtectedRepair>(protector.Unprotect(p.ProposalToken!))!;
        var before=await Agenda(c,t.Id);
        var invalid=envelope.Assignments.Select(a=>a.MatchId==p.Changes[0].After.MatchId?a with {StartsAt=Options.HorizonStart}:a).ToArray();
        if (writerFailure)
        {
            invalid=envelope.Assignments.ToArray();
            invalid[1]=invalid[1] with { StartsAt="2026-11-14T22:00:00Z", PlayingAreaId=Guid.NewGuid() };
        }
        var token=protector.Protect(JsonSerializer.Serialize(envelope with { Assignments=invalid }));
        Assert.Equal(HttpStatusCode.Conflict,(await RepairApply(c,t.Id,token)).StatusCode);
        Assert.Equal(before,await Agenda(c,t.Id));
        foreach(var row in before) Assert.DoesNotContain(await AuditRows(c,row.MatchId),a=>a.EventType=="MatchRescheduled");
    }
    [Fact]
    public async Task RepairCanSwapReservationsAtomicallyUsingScheduleWriter()
    {
        var stub=new Stub { RepairResponse=input=> {
            var rows=input.Schedule.ExistingSchedule.OrderBy(m=>m.StartsAt).ToArray();
            return new("optimal",rows.Select((m,i)=>new SolverAssignment(m.MatchId,(i==0?rows[1].StartsAt:i==1?rows[0].StartsAt:m.StartsAt).ToString("O"),m.PlayingAreaId,m.RefereeId)).ToArray(),[],Fairness,new(50,100,0,null,9,3,["Análise."]));
        }};
        using var c=Client(stub);var t=await RepairSetup(c);
        var ordered=t.Rows.OrderBy(m=>m.StartsAt).ToArray();
        var secondTeams=new[]{ordered[1].HomeTeamId,ordered[1].AwayTeamId};
        var team=new[]{ordered[0].HomeTeamId,ordered[0].AwayTeamId}.Single(id=>!secondTeams.Contains(id));
        var options=new RepairOptions(Options,new("teamUnavailable",team,Options.HorizonStart,"2026-11-14T19:00:00Z"));
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,options));
        Assert.Equal(2,p.Metrics.ChangedCount);
        (await RepairApply(c,t.Id,p.ProposalToken!)).EnsureSuccessStatusCode();
        var after=await Agenda(c,t.Id);
        Assert.Equal(ordered[1].StartsAt,after.Single(m=>m.MatchId==ordered[0].MatchId).StartsAt);
        Assert.Equal(ordered[0].StartsAt,after.Single(m=>m.MatchId==ordered[1].MatchId).StartsAt);
    }
    [Fact]
    public async Task MatchDelayedKeepsOriginalReservationAndRepairsFollowingGame()
    {
        using var c=Client(new Stub {RepairResponse=RepairAnswer});var t=await RepairSetup(c);
        var first=t.Rows.OrderBy(m=>m.StartsAt).First();
        var options=new RepairOptions(Options,new("matchDelayed",MatchId:first.MatchId,DelayMinutes:30));
        var p=await Read<RepairProposal>(await RepairGenerate(c,t.Id,options));
        Assert.Single(p.Changes);Assert.DoesNotContain(p.Changes,x=>x.After.MatchId==first.MatchId);
        (await RepairApply(c,t.Id,p.ProposalToken!)).EnsureSuccessStatusCode();
        var unchanged=(await Agenda(c,t.Id)).Single(m=>m.MatchId==first.MatchId);
        Assert.Equal(first,unchanged);
        Assert.DoesNotContain(await AuditRows(c,first.MatchId),a=>a.EventType=="MatchRescheduled");
    }

}
