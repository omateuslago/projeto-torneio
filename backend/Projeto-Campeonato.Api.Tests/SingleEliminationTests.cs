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

public sealed class SingleEliminationDomainTests
{
    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 3, 2)]
    [InlineData(5, 7, 2)]
    [InlineData(8, 7, 4)]
    [InlineData(16, 15, 8)]
    public void GeneratesCompletePersistableStructure(int teamCount, int nodeCount, int initialMatchCount)
    {
        var tournamentId = Guid.NewGuid();
        var teams = Enumerable.Range(0, teamCount).Select(_ => Guid.NewGuid()).ToArray();

        var bracket = SingleEliminationBracket.Generate(tournamentId, teams);

        Assert.Equal(nodeCount, bracket.Nodes.Count);
        Assert.Equal(initialMatchCount, bracket.Matches.Count);
        Assert.Equal(nodeCount, bracket.Nodes.Select(x => (x.RoundNumber, x.Position)).Distinct().Count());
        Assert.All(bracket.Matches, match =>
        {
            Assert.Contains(match.HomeTeamId, teams);
            Assert.Contains(match.AwayTeamId, teams);
            Assert.NotEqual(match.HomeTeamId, match.AwayTeamId);
        });
        Assert.DoesNotContain(bracket.Nodes, x => x.TopTeamId == Guid.Empty || x.BottomTeamId == Guid.Empty);
        Assert.Single(bracket.Nodes, x => x.NextNodeId is null);
    }

    [Fact]
    public void SeedingIsDeterministicAndGivesByesToHighestSeeds()
    {
        var teams = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        var first = SingleEliminationBracket.Generate(Guid.NewGuid(), teams);
        var second = SingleEliminationBracket.Generate(Guid.NewGuid(), teams);

        var firstRound = first.Nodes.Where(x => x.RoundNumber == 1).OrderBy(x => x.Position).ToArray();
        var repeated = second.Nodes.Where(x => x.RoundNumber == 1).OrderBy(x => x.Position).ToArray();
        Assert.Equal(firstRound.Select(x => (x.TopTeamId, x.BottomTeamId)), repeated.Select(x => (x.TopTeamId, x.BottomTeamId)));
        Assert.Equal(3, firstRound.Count(x => x.IsBye));
        Assert.Contains(firstRound, x => x.IsBye && (x.TopTeamId == teams[0] || x.BottomTeamId == teams[0]));
        Assert.Contains(firstRound, x => x.IsBye && (x.TopTeamId == teams[1] || x.BottomTeamId == teams[1]));
        Assert.Contains(firstRound, x => x.IsBye && (x.TopTeamId == teams[2] || x.BottomTeamId == teams[2]));
    }

    [Fact]
    public void RejectsTooFewOrDuplicateTeams()
    {
        var team = Guid.NewGuid();
        Assert.Throws<DomainConflictException>(() => SingleEliminationBracket.Generate(Guid.NewGuid(), [team]));
        Assert.Throws<ArgumentException>(() => SingleEliminationBracket.Generate(Guid.NewGuid(), [team, team]));
    }
}

public sealed class SingleEliminationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<Guid> Tournament(int count = 4)
    {
        var tournament = await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments",
            new { name = "Eliminatórias", sport = "Futsal", maxTeams = 32, format = "SingleElimination" }));
        await Read<TournamentResponse>(await client.PutAsJsonAsync($"/api/tournaments/{tournament.Id}/status", new { status = "Open" }));
        for (var i = 1; i <= count; i++)
        {
            var team = await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name = $"Seed {i:00}" }));
            (await client.PostAsJsonAsync($"/api/tournaments/{tournament.Id}/registrations", new { teamId = team.Id })).EnsureSuccessStatusCode();
        }
        await Read<TournamentResponse>(await client.PutAsJsonAsync($"/api/tournaments/{tournament.Id}/status", new { status = "Closed" }));
        return tournament.Id;
    }

    private Task<HttpResponseMessage> Generate(Guid id) => client.PostAsync($"/api/tournaments/{id}/matches/generate", null);
    private Task<HttpResponseMessage> Result(Guid id, int home, int away) =>
        client.PutAsJsonAsync($"/api/matches/{id}/result", new { homeScore = home, awayScore = away });
    private Task<HttpResponseMessage> Submit(MatchResponse match, Guid teamId, int home, int away) =>
        client.PutAsJsonAsync($"/api/matches/{match.Id}/score-submissions/{teamId}", new { homeScore = home, awayScore = away });

    [Fact]
    public async Task GeneratesIdempotentBracketAndProgressesWinnerToChampion()
    {
        var id = await Tournament();
        var semifinals = await Read<MatchResponse[]>(await Generate(id));
        Assert.Equal(2, semifinals.Length);
        var initial = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Equal(2, initial.Rounds.Count);
        Assert.Equal("Semifinais", initial.Rounds[0].Name);
        Assert.Equal("Final", initial.Rounds[1].Name);
        Assert.Null(initial.Rounds[1].Nodes[0].Match);

        var repeated = await Read<MatchResponse[]>(await Generate(id));
        Assert.Equal(semifinals.Select(x => x.Id), repeated.Select(x => x.Id));

        await Read<MatchResponse>(await Result(semifinals[0].Id, 2, 0));
        var afterFirst = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.NotNull(afterFirst.Rounds[1].Nodes[0].TopTeam ?? afterFirst.Rounds[1].Nodes[0].BottomTeam);
        Assert.Null(afterFirst.Rounds[1].Nodes[0].Match);

        await Read<MatchResponse>(await Result(semifinals[1].Id, 0, 1));
        var readyFinal = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        var final = Assert.IsType<BracketMatchResponse>(readyFinal.Rounds[1].Nodes[0].Match);
        Assert.Equal(MatchStatus.Pending, final.Status);
        Assert.Equal(TournamentStatus.InProgress,
            (await Read<TournamentResponse>(await client.GetAsync($"/api/tournaments/{id}"))).Status);

        var tie = await Result(final.Id, 1, 1);
        Assert.Equal(HttpStatusCode.Conflict, tie.StatusCode);
        var finished = await Read<MatchResponse>(await Result(final.Id, 3, 2));
        var complete = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Equal(TournamentStatus.Finished, complete.Status);
        Assert.NotNull(complete.Champion);
        Assert.Equal(finished.HomeTeamId, complete.Champion!.Id);
        var audit = await Read<AuditResponse[]>(await client.GetAsync($"/api/matches/{final.Id}/audit-log"));
        Assert.Contains(audit, x => x.EventType == "TournamentChampionDefined" && x.TeamId == complete.Champion.Id);
    }

    [Fact]
    public async Task ByeAdvancesWithoutCreatingFakeMatch()
    {
        var id = await Tournament(3);
        var matches = await Read<MatchResponse[]>(await Generate(id));
        Assert.Single(matches);
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Equal(3, bracket.Rounds.Sum(x => x.Nodes.Count));
        Assert.Single(bracket.Rounds[0].Nodes, x => x.IsBye);
        Assert.DoesNotContain(bracket.Rounds[0].Nodes.Where(x => x.IsBye), x => x.Match is not null);
        Assert.NotNull(bracket.Rounds[1].Nodes[0].TopTeam ?? bracket.Rounds[1].Nodes[0].BottomTeam);
    }

    [Fact]
    public async Task WinnerCorrectionIsRejectedAfterDownstreamActivity()
    {
        var id = await Tournament();
        var semifinals = await Read<MatchResponse[]>(await Generate(id));
        await Read<MatchResponse>(await Result(semifinals[0].Id, 1, 0));
        await Read<MatchResponse>(await Result(semifinals[1].Id, 1, 0));
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        var finalId = bracket.Rounds[1].Nodes[0].Match!.Id;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
            var final = await db.Matches.SingleAsync(x => x.Id == finalId);
            db.ScoreSubmissions.Add(new ScoreSubmission(final, final.HomeTeamId, 1, 0));
            await db.SaveChangesAsync();
        }

        var correction = await Result(semifinals[0].Id, 0, 1);
        Assert.Equal(HttpStatusCode.Conflict, correction.StatusCode);
    }

    [Fact]
    public async Task WinnerCorrectionUpdatesUntouchedDownstreamSlot()
    {
        var id = await Tournament();
        var semifinal = (await Read<MatchResponse[]>(await Generate(id)))[0];
        await Read<MatchResponse>(await Result(semifinal.Id, 2, 0));
        await Read<MatchResponse>(await Result(semifinal.Id, 4, 1));
        var corrected = await Read<MatchResponse>(await Result(semifinal.Id, 0, 1));
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        var finalNode = bracket.Rounds.Single(x => x.Name == "Final").Nodes[0];

        Assert.Equal(semifinal.AwayTeamId, corrected.AwayTeamId);
        Assert.Contains(semifinal.AwayTeamId, new[] { finalNode.TopTeam?.Id, finalNode.BottomTeam?.Id });
        Assert.DoesNotContain(semifinal.HomeTeamId, new[] { finalNode.TopTeam?.Id, finalNode.BottomTeam?.Id });
    }

    [Fact]
    public async Task BilateralAgreementAdvancesWinner()
    {
        var id = await Tournament();
        var semifinal = (await Read<MatchResponse[]>(await Generate(id)))[0];
        (await Submit(semifinal, semifinal.HomeTeamId, 2, 0)).EnsureSuccessStatusCode();
        (await Submit(semifinal, semifinal.AwayTeamId, 2, 0)).EnsureSuccessStatusCode();

        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        var finalNode = bracket.Rounds.Single(x => x.Name == "Final").Nodes[0];
        Assert.Contains(semifinal.HomeTeamId, new[] { finalNode.TopTeam?.Id, finalNode.BottomTeam?.Id });
        Assert.DoesNotContain(semifinal.AwayTeamId, new[] { finalNode.TopTeam?.Id, finalNode.BottomTeam?.Id });
    }

    [Fact]
    public async Task DisputeResolutionAdvancesWinner()
    {
        var id = await Tournament();
        var semifinal = (await Read<MatchResponse[]>(await Generate(id)))[0];
        (await Submit(semifinal, semifinal.HomeTeamId, 1, 0)).EnsureSuccessStatusCode();
        (await Submit(semifinal, semifinal.AwayTeamId, 0, 1)).EnsureSuccessStatusCode();
        var current = (await Read<MatchResponse[]>(await client.GetAsync($"/api/tournaments/{id}/matches")))
            .Single(x => x.Id == semifinal.Id);

        (await client.PutAsJsonAsync($"/api/disputes/{current.Dispute!.Id}/resolve",
            new { homeScore = 0, awayScore = 2, resolutionNote = "Súmula revisada." })).EnsureSuccessStatusCode();
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        var finalNode = bracket.Rounds.Single(x => x.Name == "Final").Nodes[0];
        Assert.Contains(semifinal.AwayTeamId, new[] { finalNode.TopTeam?.Id, finalNode.BottomTeam?.Id });
    }

    [Fact]
    public async Task UserWithoutTournamentPermissionCannotGenerateBracket()
    {
        var id = await Tournament();
        var outsider = await factory.RegisterUser();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.Client.PostAsync($"/api/tournaments/{id}/matches/generate", null)).StatusCode);
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Empty(bracket.Rounds);
    }

    [Fact]
    public async Task FutureSlotHasNoMatchEndpointUntilBothParticipantsAreKnown()
    {
        var id = await Tournament();
        await Read<MatchResponse[]>(await Generate(id));
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Null(bracket.Rounds.Single(x => x.Name == "Final").Nodes[0].Match);
        Assert.Equal(HttpStatusCode.NotFound, (await Result(Guid.NewGuid(), 1, 0)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentGenerationCreatesOneEliminationStructure()
    {
        var id = await Tournament(8);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Generate(id)));
        foreach (var response in responses) Assert.Equal(4, (await Read<MatchResponse[]>(response)).Length);
        var bracket = await Read<BracketResponse>(await client.GetAsync($"/api/tournaments/{id}/bracket"));
        Assert.Equal(7, bracket.Rounds.Sum(x => x.Nodes.Count));
    }
}
