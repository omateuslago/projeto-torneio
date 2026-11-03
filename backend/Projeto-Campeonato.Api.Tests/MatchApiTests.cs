using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Features;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

public sealed class MatchApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private async Task<Guid> Tournament(int count = 2, bool close = true, string format = "RoundRobin")
    {
        var tournament = await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments", new { name = "Cup", sport = "Futsal", maxTeams = 8, format }));
        await Read<TournamentResponse>(await client.PutAsJsonAsync($"/api/tournaments/{tournament.Id}/status", new { status = "Open" }));
        for (var i = 0; i < count; i++)
        {
            var team = await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name = $"Team {i}" }));
            var response = await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/registrations", new { teamId = team.Id });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        if (close) await Read<TournamentResponse>(await client.PutAsJsonAsync($"/api/tournaments/{tournament.Id}/status", new { status = "Closed" }));
        return tournament.Id;
    }
    private Task<HttpResponseMessage> Generate(Guid id) => client.PostAsync($"/api/tournaments/{id}/matches/generate", null);
    private Task<HttpResponseMessage> Result(Guid id, int home, int away) => client.PutAsJsonAsync($"/api/matches/{id}/result", new { homeScore = home, awayScore = away });
    private async Task<TournamentStatus> Status(Guid id) => (await Read<TournamentResponse>(await client.GetAsync($"/api/tournaments/{id}"))).Status;

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 6)]
    [InlineData(6, 15)]
    public async Task GenerationIsCompleteAndIdempotent(int teams, int expected)
    {
        var id = await Tournament(teams);
        var matches = await Read<MatchResponse[]>(await Generate(id));
        Assert.Equal(expected, matches.Length);
        Assert.All(matches, x => { Assert.NotEqual(x.HomeTeamId, x.AwayTeamId); Assert.Null(x.HomeScore); Assert.Equal(MatchStatus.Pending, x.Status); });
        Assert.Equal(expected, matches.Select(x => (x.HomeTeamId, x.AwayTeamId)).Distinct().Count());
        var repeated = await Read<MatchResponse[]>(await Generate(id));
        Assert.Equal(matches.Select(x => x.Id), repeated.Select(x => x.Id));
        Assert.Equal(TournamentStatus.InProgress, await Status(id));
    }

    [Fact]
    public async Task ConcurrentGenerationCreatesOnlyOneSet()
    {
        var id = await Tournament(4);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Generate(id)));
        foreach (var response in responses) Assert.Equal(6, (await Read<MatchResponse[]>(response)).Length);
        Assert.Equal(6, (await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{id}/matches"))).Length);
    }

    [Theory]
    [InlineData(2, false, "RoundRobin")]
    [InlineData(1, true, "RoundRobin")]
    [InlineData(2, true, "SingleElimination")]
    public async Task InvalidGenerationReturnsConflictWithoutChangingTournament(int count, bool close, string format)
    {
        var id = await Tournament(count, close, format);
        Assert.Equal(HttpStatusCode.Conflict, (await Generate(id)).StatusCode);
        Assert.Empty(await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{id}/matches")));
        Assert.Equal(close ? TournamentStatus.Closed : TournamentStatus.Open, await Status(id));
    }

    [Fact]
    public async Task MissingResourcesReturnNotFound()
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Generate(id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Result(id, 1, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tournaments/{id}/matches")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tournaments/{id}/standings")).StatusCode);
    }

    [Fact]
    public async Task ResultsFinishTournamentAndCorrectionRecalculatesStandings()
    {
        var id = await Tournament();
        var match = Assert.Single(await Read<MatchResponse[]>(await Generate(id)));
        var finished = await Read<MatchResponse>(await Result(match.Id, 3, 1));
        Assert.NotNull(finished.FinishedAt);
        Assert.Equal(MatchStatus.Finished, finished.Status);
        Assert.Equal(TournamentStatus.Finished, await Status(id));
        var rows = await Read<StandingRow[]>(await client.GetAsync($"/api/tournaments/{id}/standings"));
        Assert.Equal(match.HomeTeamId, rows[0].TeamId);
        Assert.Equal(3, rows[0].Points);
        Assert.Equal(1, rows[1].Losses);
        var corrected = await Read<MatchResponse>(await Result(match.Id, 2, 2));
        Assert.True(corrected.FinishedAt >= finished.FinishedAt);
        rows = await Read<StandingRow[]>(await client.GetAsync($"/api/tournaments/{id}/standings"));
        Assert.All(rows, x => { Assert.Equal(1, x.Points); Assert.Equal(1, x.Played); Assert.Equal(1, x.Draws); Assert.Equal(2, x.GoalsFor); Assert.Equal(2, x.GoalsAgainst); Assert.Equal(0, x.GoalDifference); });
        var repeated = Assert.Single(await Read<MatchResponse[]>(await Generate(id)));
        Assert.Equal(2, repeated.HomeScore);
        Assert.Equal(TournamentStatus.Finished, await Status(id));
    }

    [Fact]
    public async Task ParallelResultsFinishOnlyAfterAllMatches()
    {
        var id = await Tournament(4);
        var matches = await Read<MatchResponse[]>(await Generate(id));
        await Read<MatchResponse>(await Result(matches[0].Id, 1, 0));
        Assert.Equal(TournamentStatus.InProgress, await Status(id));
        var responses = await Task.WhenAll(matches.Skip(1).Select(x => Result(x.Id, 0, 0)));
        foreach (var response in responses) response.EnsureSuccessStatusCode();
        Assert.Equal(TournamentStatus.Finished, await Status(id));
        var rows = await Read<StandingRow[]>(await client.GetAsync($"/api/tournaments/{id}/standings"));
        Assert.All(rows, x => Assert.Equal(3, x.Played));
    }

    [Fact]
    public async Task InvalidScoreDoesNotChangeMatchOrStatus()
    {
        var id = await Tournament();
        var match = Assert.Single(await Read<MatchResponse[]>(await Generate(id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Result(match.Id, -1, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/matches/{match.Id}/result", new { homeScore = 1 })).StatusCode);
        Assert.Null(Assert.Single(await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{id}/matches"))).FinishedAt);
        Assert.Equal(TournamentStatus.InProgress, await Status(id));
    }

    [Fact]
    public async Task StandingsIncludeAllTeamsBeforeResults()
    {
        var id = await Tournament(4);
        var rows = await Read<StandingRow[]>(await client.GetAsync($"/api/tournaments/{id}/standings"));
        Assert.Equal(4, rows.Length);
        Assert.All(rows, x => { Assert.Equal(0, x.Points); Assert.Equal(0, x.Played); });
        Assert.Equal(rows.Select(x => x.TeamName).Order(), rows.Select(x => x.TeamName));
    }

    [Fact]
    public async Task LifecycleCannotBeAdvancedThroughRegistrationEndpoint()
    {
        var id = await Tournament();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/tournaments/{id}/status", new { status = "Finished" })).StatusCode);
    }

    [Fact]
    public async Task DatabaseRejectsTeamNotRegisteredInTournament()
    {
        var id = await Tournament();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var outsider = new Team("Outsider");
        db.Teams.Add(outsider);
        await db.SaveChangesAsync();
        var participant = await db.Registrations.Where(x => x.TournamentId == id).Select(x => x.TeamId).FirstAsync();
        db.Matches.Add(RoundRobin.Generate(id, [participant, outsider.Id])[0]);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SwaggerDocumentsNewEndpoints()
    {
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/tournaments/{tournamentId}/matches/generate", out _));
        Assert.True(paths.TryGetProperty("/api/matches/{matchId}/result", out _));
        Assert.True(paths.TryGetProperty("/api/tournaments/{tournamentId}/standings", out _));
    }
}
