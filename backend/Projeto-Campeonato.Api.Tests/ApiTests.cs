using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    private async Task<JsonElement> CreateTournament(int capacity = 2)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new { name = "Cup", sport = "Futsal", maxTeams = capacity, format = "RoundRobin" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<Guid> CreateTeam()
    {
        var response = await client.PostAsJsonAsync("/api/teams", new { name = " Team " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Team", body.GetProperty("name").GetString());
        return body.GetProperty("id").GetGuid();
    }
    private Task<HttpResponseMessage> Register(Guid tournament, Guid team) =>
        client.PostAsJsonAsync($"/api/tournaments/{tournament}/registrations", new { teamId = team });
    private async Task<Guid> OpenTournament(int capacity = 2)
    {
        var tournament = await CreateTournament(capacity);
        var id = tournament.GetProperty("id").GetGuid();
        var response = await client.PutAsJsonAsync($"/api/tournaments/{id}/status", new { status = "Open" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    [Fact]
    public async Task TournamentPersistsAcrossRequests()
    {
        var body = await CreateTournament();
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        using var otherClient = factory.CreateClient();
        var persisted = await otherClient.GetFromJsonAsync<JsonElement>($"/api/tournaments/{id}");
        Assert.Equal("Cup", persisted.GetProperty("name").GetString());
        var list = await otherClient.GetFromJsonAsync<JsonElement[]>("/api/tournaments");
        Assert.Contains(list!, x => x.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("", 2, "RoundRobin")]
    [InlineData("Cup", 1, "RoundRobin")]
    [InlineData("Cup", 2, "Swiss")]
    public async Task InvalidTournamentReturnsValidationProblem(string name, int maxTeams, string format)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new { name, sport = "Futsal", maxTeams, format });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TeamCanBeReadAndListed()
    {
        var id = await CreateTeam();
        var team = await client.GetFromJsonAsync<JsonElement>($"/api/teams/{id}");
        Assert.Equal("Team", team.GetProperty("name").GetString());
        var teams = await client.GetFromJsonAsync<JsonElement[]>("/api/teams");
        Assert.Contains(teams!, x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task BlankTeamIsRejected() => Assert.Equal(HttpStatusCode.BadRequest,
        (await client.PostAsJsonAsync("/api/teams", new { name = "   " })).StatusCode);

    [Fact]
    public async Task RegistrationListsParticipantAndRejectsDuplicate()
    {
        var id = await OpenTournament();
        var team = await CreateTeam();
        Assert.Equal(HttpStatusCode.Created, (await Register(id, team)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Register(id, team)).StatusCode);
        var participants = await client.GetFromJsonAsync<JsonElement[]>($"/api/tournaments/{id}/registrations");
        Assert.Single(participants!);
        Assert.Equal(team, participants![0].GetProperty("teamId").GetGuid());
        Assert.Equal("Team", participants[0].GetProperty("teamName").GetString());
    }

    [Fact]
    public async Task DraftAndClosedTournamentsRejectRegistrations()
    {
        var tournament = await CreateTournament();
        var id = tournament.GetProperty("id").GetGuid();
        var team = await CreateTeam();
        Assert.Equal(HttpStatusCode.Conflict, (await Register(id, team)).StatusCode);
        await client.PutAsJsonAsync($"/api/tournaments/{id}/status", new { status = "Open" });
        await client.PutAsJsonAsync($"/api/tournaments/{id}/status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.Conflict, (await Register(id, team)).StatusCode);
    }

    [Fact]
    public async Task UnknownResourcesReturnNotFound()
    {
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tournaments/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/teams/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tournaments/{missing}/registrations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Register(missing, await CreateTeam())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Register(await OpenTournament(), missing)).StatusCode);
    }

    [Fact]
    public async Task EmptyTeamIdIsBadRequest() => Assert.Equal(HttpStatusCode.BadRequest,
        (await Register(await OpenTournament(), Guid.Empty)).StatusCode);

    [Fact]
    public async Task InvalidStatusTransitionIsConflict()
    {
        var tournament = await CreateTournament();
        var response = await client.PutAsJsonAsync($"/api/tournaments/{tournament.GetProperty("id").GetGuid()}/status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRegistrationsCannotExceedCapacity()
    {
        var id = await OpenTournament();
        var teams = new List<Guid>();
        for (var i = 0; i < 8; i++) teams.Add(await CreateTeam());
        var responses = await Task.WhenAll(teams.Select(team => Register(id, team)));
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        // Retry serialization conflicts to fill any remaining vacancy.
        foreach (var team in teams) await Register(id, team);
        var registrations = await client.GetFromJsonAsync<JsonElement[]>($"/api/tournaments/{id}/registrations");
        Assert.Equal(2, registrations!.Length);
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesOnlyOneRegistration()
    {
        var id = await OpenTournament(8);
        var team = await CreateTeam();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Register(id, team)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task LegacyEndpointContractIsPreserved()
    {
        var response = await client.PostAsJsonAsync("/campeonatos", new { nome = "Legacy", jogo = "Futsal", equipes = 8, status = "Aberto", modalidade = "Físico" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var championships = await client.GetFromJsonAsync<JsonElement[]>("/campeonatos");
        Assert.Contains(championships!, x => x.GetProperty("nome").GetString() == "Legacy");
    }

    [Fact]
    public async Task SwaggerDescribesNewAndLegacyEndpoints()
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/tournaments", out _));
        Assert.True(paths.TryGetProperty("/api/teams", out _));
        Assert.True(paths.TryGetProperty("/campeonatos", out _));
    }

    [Fact]
    public void MigrationMatchesCurrentModel()
    {
        using var scope = factory.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.HasPendingModelChanges());
    }
}
