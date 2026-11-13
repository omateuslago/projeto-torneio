using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Projeto_Campeonato.Api.Features;

namespace Projeto_Campeonato.Api.Tests;

public sealed partial class AutopilotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly AutopilotOptions Options = new("2026-11-14T18:00:00Z", "2026-11-15T02:00:00Z", 15, false);
    private static readonly ScheduleFairness Fairness = new(91, 94, 100, 82, ["Equipe 1 possui apenas 30 minutos de descanso."]);
    private static async Task<T> Read<T>(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<T>(Json))!; }
    private sealed class Stub : ISchedulerClient
    {
        public Func<RepairInput, SolverResult>? RepairResponse;
        public Task<SolverResult> Repair(RepairInput input, CancellationToken ct) => Task.FromResult(RepairResponse!(input));
        public SolverInput? Last;
        public Func<SolverInput, SolverResult> Response = input => new("optimal", input.Matches.Select((m, i) => new SolverAssignment(m.Id, input.HorizonStart.AddHours(i).ToString("O"), input.PlayingAreas[0].Id, null)).ToArray(), [], Fairness, new(80, 90, 70, null, 3, 1, ["Análise determinística."]));
        public Task<SolverResult> Generate(SolverInput input, CancellationToken ct) { Last = input; return Task.FromResult(Response(input)); }
        public Task<bool> Healthy(CancellationToken ct) => Task.FromResult(true);
    }
    private async Task<(Guid Id, Guid Area, Guid Team)> Setup(HttpClient client, int count = 2, Guid? sharedTeam = null)
    {
        var t = await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments", new { name = "Autopilot", sport = "Futsal", maxTeams = 4, format = "RoundRobin" }));
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Open" })).EnsureSuccessStatusCode();
        Guid teamId = default;
        for (var i = 0; i < count; i++)
        {
            teamId = i == 0 && sharedTeam.HasValue ? sharedTeam.Value : (await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name = $"Equipe {i}" }))).Id;
            (await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/registrations", new { teamId })).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/teams/{teamId}/availability", new { startsAt = Options.HorizonStart, endsAt = Options.HorizonEnd })).EnsureSuccessStatusCode();
        }
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Closed" })).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/tournaments/{t.Id}/matches/generate", null)).EnsureSuccessStatusCode();
        var v = await Read<VenueDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/venues", new { name = "Ginásio", address = "Rua 1" }));
        var a = await Read<AreaDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/venues/{v.Id}/playing-areas", new { name = "Quadra" }));
        (await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/playing-areas/{a.Id}/availability", new { startsAt = Options.HorizonStart, endsAt = Options.HorizonEnd })).EnsureSuccessStatusCode();
        return (t.Id, a.Id, teamId);
    }
    private HttpClient Client(Stub stub) => factory.Authenticate(factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<ISchedulerClient>(); s.AddSingleton<ISchedulerClient>(stub); })).CreateClient());
    private Task<HttpResponseMessage> Generate(HttpClient c, Guid id) => c.PostAsJsonAsync($"/api/tournaments/{id}/schedule/generate", Options);
    private Task<HttpResponseMessage> Apply(HttpClient c, Guid id, string token) => c.PostAsJsonAsync($"/api/tournaments/{id}/schedule/apply", new { proposalToken = token });
    private async Task<ScheduleDto[]> Agenda(HttpClient c, Guid id) => await Read<ScheduleDto[]>(await c.GetAsync($"/api/tournaments/{id}/schedule"));

    [Fact]
    public async Task ProposalDoesNotPersistAndApplyIsAtomicAndOneTime()
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c, 3);
        var proposal = await Read<ScheduleProposal>(await Generate(c, t.Id));
        Assert.Equal(3, proposal.Metrics.ScheduledCount); Assert.NotNull(proposal.ProposalToken);
        Assert.Equal(60, stub.Last!.MatchDurationMinutes); Assert.Equal(3, stub.Last.Teams.Length);
        Assert.Equal(60, stub.Last.PreferredRestMinutes);
        Assert.Equal(80, proposal.Resilience!.Score);
        Assert.Equal(90, proposal.Resilience.DelayScore);
        Assert.Equal(70, proposal.Resilience.AreaScore);
        Assert.Null(proposal.Resilience.RefereeScore);
        Assert.Equal("Análise determinística.", Assert.Single(proposal.Resilience.Observations));
        Assert.Equal(Fairness.Score, proposal.Fairness!.Score);
        Assert.Equal(Fairness.RestScore, proposal.Fairness.RestScore);
        Assert.Equal(Fairness.BackToBackScore, proposal.Fairness.BackToBackScore);
        Assert.Equal(Fairness.TimeBalanceScore, proposal.Fairness.TimeBalanceScore);
        Assert.Equal(Fairness.Observations, proposal.Fairness.Observations);
        Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
        (await Apply(c, t.Id, proposal.ProposalToken!)).EnsureSuccessStatusCode();
        Assert.All(await Agenda(c, t.Id), m => Assert.NotNull(m.StartsAt));
        Assert.Equal(HttpStatusCode.Conflict, (await Apply(c, t.Id, proposal.ProposalToken!)).StatusCode);
    }
    [Theory]
    [InlineData("infeasible")]
    [InlineData("timeout")]
    [InlineData("error")]
    public async Task NonSolutionHasNoTokenOrChanges(string status)
    {
        var stub = new Stub { Response = _ => new(status, [], ["Diagnóstico do solver."]) }; using var c = Client(stub); var t = await Setup(c);
        var p = await Read<ScheduleProposal>(await Generate(c, t.Id)); Assert.Equal(status, p.Status); Assert.Null(p.ProposalToken); Assert.NotEmpty(p.Warnings); Assert.Null((await Agenda(c, t.Id))[0].StartsAt);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("collision")]
    [InlineData("outside")]
    [InlineData("duplicate")]
    public async Task InvalidSolverSolutionNeverPersists(string mode)
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c, 3);
        var good = stub.Response; stub.Response = input =>
        {
            var rows = good(input).ScheduledMatches;
            if (mode == "missing") rows = rows.Take(1).ToArray();
            if (mode == "foreign") rows[0] = rows[0] with { PlayingAreaId = Guid.NewGuid() };
            if (mode == "collision") rows[1] = rows[1] with { StartsAt = rows[0].StartsAt };
            if (mode == "outside") rows[0] = rows[0] with { StartsAt = "2027-01-01T00:00:00Z" };
            if (mode == "duplicate") rows[1] = rows[0];
            return new("feasible", rows, [], Fairness, new(80, 90, 70, null, 3, 1, ["Análise determinística."]));
        };
        Assert.Equal(HttpStatusCode.BadGateway, (await Generate(c, t.Id)).StatusCode);
        Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
    }
    [Fact]
    public async Task AvailabilityChangeRejectsOldSnapshotAndKeepsAllMatchesUnchanged()
    {
        using var c = Client(new Stub()); var t = await Setup(c, 3); var proposal = await Read<ScheduleProposal>(await Generate(c, t.Id));
        (await c.PostAsJsonAsync($"/api/tournaments/{t.Id}/teams/{t.Team}/availability", new { startsAt = "2026-11-16T18:00:00Z", endsAt = "2026-11-16T22:00:00Z" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Apply(c, t.Id, proposal.ProposalToken!)).StatusCode); Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
    }
    [Fact]
    public async Task ConcurrentApplicationsOnlyOneWins()
    {
        using var c = Client(new Stub()); var t = await Setup(c, 3); var p = await Read<ScheduleProposal>(await Generate(c, t.Id));
        var results = await Task.WhenAll(Apply(c, t.Id, p.ProposalToken!), Apply(c, t.Id, p.ProposalToken!));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task ForgedTokenRejected()
    {
        using var c = Client(new Stub()); var t = await Setup(c); Assert.Equal(HttpStatusCode.BadRequest, (await Apply(c, t.Id, "invented")).StatusCode);
        var p = await Read<ScheduleProposal>(await Generate(c, t.Id)); Assert.Equal(HttpStatusCode.BadRequest, (await Apply(c, Guid.NewGuid(), p.ProposalToken!)).StatusCode);
    }
    [Fact]
    public async Task ExistingMatchPreservedAndPassedAsFixedOccupation()
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c, 3); var first = (await Agenda(c, t.Id))[0];
        (await c.PutAsJsonAsync($"/api/matches/{first.MatchId}/schedule", new { startsAt = "2026-11-15T01:00:00Z", playingAreaId = t.Area })).EnsureSuccessStatusCode();
        var p = await Read<ScheduleProposal>(await Generate(c, t.Id)); Assert.Equal(1, p.Metrics.PreservedCount); Assert.Single(stub.Last!.ExistingSchedule);
        Assert.True(stub.Last.ExistingSchedule[0].CountsForFairness);
        (await Apply(c, t.Id, p.ProposalToken!)).EnsureSuccessStatusCode(); Assert.Equal(DateTime.Parse("2026-11-15T01:00:00Z").ToUniversalTime(), (await Agenda(c, t.Id)).Single(x => x.MatchId == first.MatchId).StartsAt);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("score")]
    [InlineData("delay")]
    [InlineData("area")]
    [InlineData("referee")]
    [InlineData("counts")]
    [InlineData("observations")]
    public async Task InvalidResilienceRejected(string mode)
    {
        var stub = new Stub(); var good = stub.Response;
        stub.Response = input => { var value = good(input); return value with { Resilience = mode switch {
            "missing" => null,
            "score" => value.Resilience! with { Score = 101 },
            "delay" => value.Resilience! with { DelayScore = -1 },
            "area" => value.Resilience! with { AreaScore = 101 },
            "referee" => value.Resilience! with { RefereeScore = 50 },
            "counts" => value.Resilience! with { VulnerableScenarios = 100 },
            _ => value.Resilience! with { Observations = null! }
        } }; };
        using var c = Client(stub); var t = await Setup(c);
        Assert.Equal(HttpStatusCode.BadGateway, (await Generate(c, t.Id)).StatusCode);
        Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(241)]
    public async Task InvalidPreferredRestRejected(int minutes)
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c);
        var response = await c.PostAsJsonAsync($"/api/tournaments/{t.Id}/schedule/generate", Options with { PreferredRestMinutes = minutes });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(stub.Last);
    }
    [Fact]
    public async Task CustomRestSurvivesPreviewAndProtectedApplication()
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c);
        var p = await Read<ScheduleProposal>(await c.PostAsJsonAsync($"/api/tournaments/{t.Id}/schedule/generate", Options with { PreferredRestMinutes = 120 }));
        Assert.Equal(120, stub.Last!.PreferredRestMinutes);
        Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
        (await Apply(c, t.Id, p.ProposalToken!)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task OtherTournamentOccupationsAreExcludedFromFairnessButStillSent()
    {
        var stub = new Stub(); using var c = Client(stub); var t = await Setup(c);
        var other = await Setup(c, sharedTeam: t.Team); var match = (await Agenda(c, other.Id))[0];
        (await c.PutAsJsonAsync($"/api/matches/{match.MatchId}/schedule", new { startsAt = "2026-11-15T01:00:00Z", playingAreaId = other.Area })).EnsureSuccessStatusCode();
        var p = await Read<ScheduleProposal>(await Generate(c, t.Id));
        Assert.Equal(0, p.Metrics.PreservedCount);
        Assert.Equal(match.MatchId, Assert.Single(stub.Last!.ExistingSchedule).MatchId);
        Assert.False(stub.Last.ExistingSchedule[0].CountsForFairness);
        (await Apply(c, t.Id, p.ProposalToken!)).EnsureSuccessStatusCode();
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("score")]
    [InlineData("rest")]
    [InlineData("backToBack")]
    [InlineData("balance")]
    [InlineData("observations")]
    [InlineData("nullObservation")]
    public async Task InvalidFairnessNeverProducesApplicableProposal(string mode)
    {
        var stub = new Stub(); var good = stub.Response;
        stub.Response = input => good(input) with { Fairness = mode switch
        {
            "missing" => null,
            "score" => Fairness with { Score = 101 },
            "rest" => Fairness with { RestScore = -1 },
            "backToBack" => Fairness with { BackToBackScore = 101 },
            "balance" => Fairness with { TimeBalanceScore = -1 },
            "observations" => Fairness with { Observations = null! },
            _ => Fairness with { Observations = [null!] }
        } };
        using var c = Client(stub); var t = await Setup(c);
        Assert.Equal(HttpStatusCode.BadGateway, (await Generate(c, t.Id)).StatusCode);
        Assert.All(await Agenda(c, t.Id), m => Assert.Null(m.StartsAt));
    }
    [Theory]
    [InlineData(503)]
    [InlineData(504)]
    public async Task SchedulerFailureDoesNotBreakManualScheduling(int status)
    {
        using var c = Client(new Stub { Response = _ => throw new SchedulerUnavailableException(status, "Scheduler offline.") }); var t = await Setup(c);
        Assert.Equal((HttpStatusCode)status, (await Generate(c, t.Id)).StatusCode);
        var m = (await Agenda(c, t.Id))[0]; (await c.PutAsJsonAsync($"/api/matches/{m.MatchId}/schedule", new { startsAt = Options.HorizonStart, playingAreaId = t.Area })).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/matches/{m.MatchId}/result", new { homeScore = 2, awayScore = 1 })).EnsureSuccessStatusCode();
    }
}

public sealed class SchedulerClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
    [Fact]
    public async Task MissingFairnessComponentsAreRejectedInsteadOfDefaultingToZero()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("""{"status":"optimal","scheduledMatches":[],"warnings":[],"fairness":{"score":100,"observations":[]}}""") })))
        { BaseAddress = new Uri("http://scheduler/") };
        var error = await Assert.ThrowsAsync<SchedulerUnavailableException>(() => new SchedulerClient(http).Generate(new(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, 15, false, [], [], [], [], []), CancellationToken.None));
        Assert.Equal(502, error.Status);
    }
    [Theory]
    [InlineData("offline", 503)]
    [InlineData("timeout", 504)]
    [InlineData("invalid", 502)]
    [InlineData("rejected", 502)]
    public async Task MapsTransportFailures(string mode, int expected)
    {
        using var http = new HttpClient(new Handler(async (_, ct) =>
        {
            if (mode == "offline") throw new HttpRequestException();
            if (mode == "timeout") await Task.Delay(1000, ct);
            return new HttpResponseMessage(mode == "rejected" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK) { Content = new StringContent("not json") };
        }))
        { BaseAddress = new Uri("http://scheduler/"), Timeout = TimeSpan.FromMilliseconds(20) };
        var error = await Assert.ThrowsAsync<SchedulerUnavailableException>(() => new SchedulerClient(http).Generate(new(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 60, 15, false, [], [], [], [], []), CancellationToken.None));
        Assert.Equal(expected, error.Status);
    }
}
