using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Features;

namespace Projeto_Campeonato.Api.Tests;

public sealed class BilateralResultTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private async Task<MatchResponse> CreateMatch() => Assert.Single(await CreateMatches());
    private async Task<MatchResponse[]> CreateMatches(int count = 2)
    {
        var t = await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments", new { name = "Bilateral", sport = "Futsal", maxTeams = 8, format = "RoundRobin" }));
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Open" })).EnsureSuccessStatusCode();
        foreach (var name in Enumerable.Range(1, count).Select(i => $"Equipe {i}"))
        {
            var team = await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name }));
            (await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/registrations", new { teamId = team.Id })).EnsureSuccessStatusCode();
        }
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Closed" })).EnsureSuccessStatusCode();
        return await Read<MatchResponse[]>(await client.PostAsync($"/api/tournaments/{t.Id}/matches/generate", null));
    }
    private Task<HttpResponseMessage> Submit(MatchResponse m, Guid team, int home = 3, int away = 1) =>
        client.PutAsJsonAsync($"/api/matches/{m.Id}/score-submissions/{team}", new { homeScore = home, awayScore = away });
    private Task<HttpResponseMessage> Resolve(Guid id, string note = "Súmula conferida.") =>
        client.PutAsJsonAsync($"/api/disputes/{id}/resolve", new { homeScore = 3, awayScore = 1, resolutionNote = note });
    private async Task<MatchResponse> Current(MatchResponse m) => Assert.Single(await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{m.TournamentId}/matches")));
    private async Task<StandingRow[]> Table(MatchResponse m) => await Read<StandingRow[]>(await client.GetAsync($"/api/tournaments/{m.TournamentId}/standings"));
    private async Task<TournamentStatus> Status(MatchResponse m) => (await Read<TournamentResponse>(await client.GetAsync($"/api/tournaments/{m.TournamentId}"))).Status;
    private async Task<AuditResponse[]> Audit(MatchResponse m) => await Read<AuditResponse[]>(await client.GetAsync($"/api/matches/{m.Id}/audit-log"));
    private async Task<MatchResponse> Disputed()
    {
        var m = await CreateMatch();
        (await Submit(m, m.HomeTeamId)).EnsureSuccessStatusCode();
        (await Submit(m, m.AwayTeamId, 2, 1)).EnsureSuccessStatusCode();
        return await Current(m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherTeamCanSubmitWithoutSettingOfficialScore(bool home)
    {
        var m = await CreateMatch();
        var team = home ? m.HomeTeamId : m.AwayTeamId;
        var submission = await Read<SubmissionResponse>(await Submit(m, team));
        var current = await Current(m);
        Assert.Equal(team, submission.SubmittedByTeamId);
        Assert.Equal(MatchStatus.AwaitingConfirmation, current.Status);
        Assert.Null(current.HomeScore); Assert.Null(current.FinishedAt);
        Assert.Single(current.Submissions);
        Assert.All(await Table(m), row => Assert.Equal(0, row.Played));
        Assert.Equal(TournamentStatus.InProgress, await Status(m));
    }
    [Fact]
    public async Task ThirdTeamIsRejectedWithoutSideEffects()
    {
        var m = await CreateMatch();
        var team = await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name = "Outsider" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(m, team.Id)).StatusCode);
        Assert.Empty((await Current(m)).Submissions); Assert.Empty(await Audit(m));
    }
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public async Task NegativeScoresAreRejected(int home, int away)
    {
        var m = await CreateMatch();
        Assert.Equal(HttpStatusCode.BadRequest, (await Submit(m, m.HomeTeamId, home, away)).StatusCode);
        Assert.Empty((await Current(m)).Submissions);
    }
    [Fact]
    public async Task UpsertKeepsIdentityAndAuditsCorrection()
    {
        var m = await CreateMatch();
        var first = await Read<SubmissionResponse>(await Submit(m, m.HomeTeamId));
        var second = await Read<SubmissionResponse>(await Submit(m, m.HomeTeamId, 2, 2));
        Assert.Equal(first.Id, second.Id); Assert.Equal(first.SubmittedAt, second.SubmittedAt);
        Assert.NotNull(second.UpdatedAt); Assert.Single((await Current(m)).Submissions);
        var audit = await Audit(m);
        Assert.Equal(new[] { "ScoreSubmitted", "ScoreUpdated" }, audit.Select(x => x.EventType));
        Assert.All(audit, x => Assert.Equal(m.HomeTeamId, x.TeamId));
        Assert.Contains("previous", audit[1].Data);
    }
    [Theory]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    public async Task AgreementConfirmsAndFinishesTournament(int home, int away)
    {
        var m = await CreateMatch();
        (await Submit(m, m.HomeTeamId, home, away)).EnsureSuccessStatusCode();
        (await Submit(m, m.AwayTeamId, home, away)).EnsureSuccessStatusCode();
        var current = await Current(m);
        Assert.Equal(MatchStatus.Finished, current.Status); Assert.Equal("Bilateral", current.ResultSource);
        Assert.Equal(home, current.HomeScore); Assert.NotNull(current.FinishedAt); Assert.Null(current.Dispute);
        Assert.Equal(TournamentStatus.Finished, await Status(m));
        Assert.All(await Table(m), row => Assert.Equal(1, row.Played));
        Assert.Equal(home == away ? 2 : 3, (await Table(m)).Sum(x => x.Points));
        Assert.Single(await Audit(m), x => x.EventType == "AutomaticallyConfirmed");
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(m, m.HomeTeamId, 0, 0)).StatusCode);
    }
    [Fact]
    public async Task DivergenceRemainsDisputedEvenAfterTeamsCorrectToAgreement()
    {
        var m = await Disputed();
        Assert.Equal(MatchStatus.Disputed, m.Status); Assert.Equal(DisputeStatus.Open, m.Dispute!.Status);
        Assert.Null(m.HomeScore); Assert.Null(m.AwayScore); Assert.Null(m.FinishedAt);
        Assert.All(await Table(m), row => Assert.Equal(0, row.Played));
        Assert.Equal(TournamentStatus.InProgress, await Status(m));
        (await Submit(m, m.AwayTeamId)).EnsureSuccessStatusCode();
        var current = await Current(m);
        Assert.Equal(m.Dispute.Id, current.Dispute!.Id); Assert.Equal(MatchStatus.Disputed, current.Status);
        Assert.Single(await Audit(m), x => x.EventType == "DisputeOpened");
    }
    [Fact]
    public async Task ResolutionAppliesOfficialResultOnceAndFinishesTournament()
    {
        var m = await Disputed();
        var resolved = await Read<DisputeResponse>(await Resolve(m.Dispute!.Id));
        Assert.Equal(DisputeStatus.Resolved, resolved.Status); Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal("Súmula conferida.", resolved.ResolutionNote);
        var current = await Current(m);
        Assert.Equal("DisputeResolution", current.ResultSource); Assert.Equal(3, current.HomeScore);
        Assert.Equal(TournamentStatus.Finished, await Status(m));
        Assert.Equal(3, (await Table(m)).Sum(x => x.Points));
        Assert.Equal(HttpStatusCode.Conflict, (await Resolve(resolved.Id)).StatusCode);
        Assert.Single(await Audit(m), x => x.EventType == "DisputeResolved");
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidResolutionNoteLeavesDisputeOpen(string note)
    {
        var m = await Disputed();
        Assert.Equal(HttpStatusCode.BadRequest, (await Resolve(m.Dispute!.Id, note)).StatusCode);
        Assert.Equal(DisputeStatus.Open, (await Current(m)).Dispute!.Status);
        Assert.Null((await Current(m)).HomeScore);
    }
    [Fact]
    public async Task UnicodeResolutionNoteFitsAuditWithoutTruncation()
    {
        var m = await Disputed();
        var note = new string('á', 2000);
        Assert.Equal(note, (await Read<DisputeResponse>(await Resolve(m.Dispute!.Id, note))).ResolutionNote);
    }
    [Fact]
    public async Task AdministrativeOverrideClosesDisputeAndPreservesSubmissions()
    {
        var m = await Disputed();
        (await client.PutAsJsonAsync($"/api/matches/{m.Id}/result", new { homeScore = 0, awayScore = 0 })).EnsureSuccessStatusCode();
        var current = await Current(m);
        Assert.Equal("Manual", current.ResultSource); Assert.Equal(DisputeStatus.Resolved, current.Dispute!.Status);
        Assert.Equal(2, current.Submissions.Count); Assert.Equal(0, current.HomeScore);
        Assert.Equal(2, (await Table(m)).Sum(x => x.Points));
        Assert.Contains(await Audit(m), x => x.EventType == "AdministrativeResult");
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(m, m.AwayTeamId)).StatusCode);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentSubmissionsHaveOneDecision(bool agree)
    {
        var m = await CreateMatch();
        var responses = await Task.WhenAll(Submit(m, m.HomeTeamId), Submit(m, m.AwayTeamId, agree ? 3 : 2, 1));
        foreach (var response in responses) response.EnsureSuccessStatusCode();
        var current = await Current(m);
        Assert.Equal(2, current.Submissions.Count);
        Assert.Equal(agree ? MatchStatus.Finished : MatchStatus.Disputed, current.Status);
        Assert.Single(await Audit(m), x => x.EventType == (agree ? "AutomaticallyConfirmed" : "DisputeOpened"));
    }
    [Fact]
    public async Task MissingResourcesAndMissingScoresReturnAppropriateErrors()
    {
        var m = await CreateMatch();
        Assert.Equal(HttpStatusCode.NotFound, (await Resolve(Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/matches/{Guid.NewGuid()}/score-submissions/{m.HomeTeamId}", new { homeScore = 0, awayScore = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/matches/{m.Id}/score-submissions/{m.HomeTeamId}", new { homeScore = 0 })).StatusCode);
    }
    [Fact]
    public async Task SwaggerIncludesTypedSubmissionAndResolutionContracts()
    {
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/matches/{matchId}/score-submissions/{teamId}", out _));
        Assert.True(paths.TryGetProperty("/api/disputes/{disputeId}/resolve", out _));
        Assert.True(doc.GetProperty("components").GetProperty("schemas").TryGetProperty("SubmissionResponse", out _));
    }
    [Fact]
    public async Task ConcurrentRetriesFromSameTeamKeepOneActiveSubmission()
    {
        var m = await CreateMatch();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Submit(m, m.HomeTeamId)));
        foreach (var response in responses) response.EnsureSuccessStatusCode();
        Assert.Single((await Current(m)).Submissions);
        Assert.Equal(MatchStatus.AwaitingConfirmation, (await Current(m)).Status);
    }
    [Fact]
    public async Task ConcurrentResolutionIsAppliedOnlyOnce()
    {
        var m = await Disputed();
        var responses = await Task.WhenAll(Resolve(m.Dispute!.Id), Resolve(m.Dispute.Id));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(await Audit(m), x => x.EventType == "DisputeResolved");
    }
    [Fact]
    public async Task TournamentWaitsForEveryOfficialResultAndLastDispute()
    {
        var matches = await CreateMatches(3);
        var first = matches[0]; var second = matches[1]; var last = matches[2];
        (await Submit(first, first.HomeTeamId)).EnsureSuccessStatusCode();
        (await Submit(first, first.AwayTeamId)).EnsureSuccessStatusCode();
        Assert.Equal(TournamentStatus.InProgress, await Status(first));
        (await Submit(second, second.HomeTeamId)).EnsureSuccessStatusCode();
        Assert.Equal(TournamentStatus.InProgress, await Status(first));
        (await Submit(last, last.HomeTeamId)).EnsureSuccessStatusCode();
        (await Submit(last, last.AwayTeamId, 0, 0)).EnsureSuccessStatusCode();
        (await Submit(second, second.AwayTeamId)).EnsureSuccessStatusCode();
        Assert.Equal(TournamentStatus.InProgress, await Status(first));
        var current = await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{first.TournamentId}/matches"));
        var dispute = current.Single(x => x.Id == last.Id).Dispute!;
        (await Resolve(dispute.Id)).EnsureSuccessStatusCode();
        Assert.Equal(TournamentStatus.Finished, await Status(first));
    }
    [Fact]
    public async Task InvalidResolutionScoreDoesNotResolveDispute()
    {
        var m = await Disputed();
        var response = await client.PutAsJsonAsync($"/api/disputes/{m.Dispute!.Id}/resolve", new { homeScore = -1, awayScore = 0, resolutionNote = "Verificado" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DisputeStatus.Open, (await Current(m)).Dispute!.Status);
    }
}
