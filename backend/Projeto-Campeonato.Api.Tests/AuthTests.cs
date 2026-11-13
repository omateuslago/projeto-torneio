using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Features;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

public sealed class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task RegisterMeLogoutAndLoginUseCookieSession()
    {
        var email = $"mateus-{Guid.NewGuid():N}@example.com";
        var (client, registered) = await factory.RegisterUser(email, "Mateus");

        Assert.True(registered.Authenticated);
        Assert.Equal(email, registered.Email);
        Assert.Equal("Mateus", registered.DisplayName);
        Assert.False(registered.IsPlatformAdmin);
        Assert.Empty(registered.OrganizerTournamentIds);
        Assert.Empty(registered.ManagedTeamIds);

        var current = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal(registered.Id, current!.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.False((await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Authenticated);

        factory.AddCsrf(client);
        var invalid = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password", rememberMe = false });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Contains("Email ou senha inválidos", await invalid.Content.ReadAsStringAsync());

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Tests-user-2026", rememberMe = true });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<CurrentUserResponse>())!.Authenticated);
    }

    [Fact]
    public async Task ProtectedWriteDistinguishesAnonymousAndForbidden()
    {
        var anonymous = factory.CreateAnonymousClient();
        factory.AddCsrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/teams", new { name = "Anonymous" })).StatusCode);

        var (owner, _) = await factory.RegisterUser(displayName: "Organizer");
        var tournament = await CreateTournament(owner, "Owner Cup");
        var (viewer, _) = await factory.RegisterUser(displayName: "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PutAsJsonAsync($"/api/tournaments/{tournament}/status", new { status = "Open" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PutAsJsonAsync($"/api/tournaments/{tournament}/status", new { status = "Open" })).StatusCode);
    }

    [Fact]
    public async Task CreatorsReceiveContextualOwnershipAndPlatformAdminBypassesIt()
    {
        var (client, user) = await factory.RegisterUser(displayName: "Owner");
        var tournamentId = await CreateTournament(client, "Context Cup");
        var teamResponse = await client.PostAsJsonAsync("/api/teams", new { name = "Falcons" });
        Assert.Equal(HttpStatusCode.Created, teamResponse.StatusCode);
        var teamId = (await teamResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var current = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Contains(tournamentId, current!.OrganizerTournamentIds);
        Assert.Contains(teamId, current.ManagedTeamIds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        Assert.True(await db.TournamentOrganizers.AnyAsync(x => x.UserId == user.Id && x.TournamentId == tournamentId));
        Assert.True(await db.TeamManagers.AnyAsync(x => x.UserId == user.Id && x.TeamId == teamId));

        using var admin = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/tournaments/{tournamentId}/status", new { status = "Open" })).StatusCode);
    }

    [Fact]
    public async Task AntiforgeryRejectsMissingTokenAndAcceptsValidToken()
    {
        var (client, _) = await factory.RegisterUser();
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/teams", new { name = "Rejected" })).StatusCode);

        factory.AddCsrf(client);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/teams", new { name = "Accepted" })).StatusCode);
    }

    [Fact]
    public async Task TeamManagersSubmitOnlyForTheirOwnParticipatingTeamAndOrganizerResolves()
    {
        var (organizer, _) = await factory.RegisterUser(displayName: "Organizer");
        var tournamentId = await CreateTournament(organizer, "Bilateral Cup");
        await organizer.PutAsJsonAsync($"/api/tournaments/{tournamentId}/status", new { status = "Open" });

        var (homeManager, _) = await factory.RegisterUser(displayName: "Home manager");
        var homeId = await CreateTeam(homeManager, "Home");
        var (awayManager, _) = await factory.RegisterUser(displayName: "Away manager");
        var awayId = await CreateTeam(awayManager, "Away");
        Assert.Equal(HttpStatusCode.Created,
            (await organizer.PostAsJsonAsync($"/api/tournaments/{tournamentId}/registrations", new { teamId = homeId })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await organizer.PostAsJsonAsync($"/api/tournaments/{tournamentId}/registrations", new { teamId = awayId })).StatusCode);
        var availability = new { startsAt = "2026-11-15T09:00:00Z", endsAt = "2026-11-15T18:00:00Z" };
        Assert.Equal(HttpStatusCode.Created,
            (await homeManager.PostAsJsonAsync($"/api/tournaments/{tournamentId}/teams/{homeId}/availability", availability)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await homeManager.PostAsJsonAsync($"/api/tournaments/{tournamentId}/teams/{awayId}/availability", availability)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await homeManager.PostAsJsonAsync($"/api/tournaments/{tournamentId}/referees", new { name = "Referee" })).StatusCode);
        await organizer.PutAsJsonAsync($"/api/tournaments/{tournamentId}/status", new { status = "Closed" });
        Assert.Equal(HttpStatusCode.OK,
            (await organizer.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches/generate", new { })).StatusCode);

        var matches = await organizer.GetFromJsonAsync<JsonElement[]>($"/api/tournaments/{tournamentId}/matches");
        var matchId = matches![0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await organizer.PutAsJsonAsync($"/api/matches/{matchId}/score-submissions/{homeId}", new { homeScore = 2, awayScore = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await homeManager.PutAsJsonAsync($"/api/matches/{matchId}/score-submissions/{awayId}", new { homeScore = 2, awayScore = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await homeManager.PutAsJsonAsync($"/api/matches/{matchId}/score-submissions/{homeId}", new { homeScore = 2, awayScore = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await awayManager.PutAsJsonAsync($"/api/matches/{matchId}/score-submissions/{awayId}", new { homeScore = 1, awayScore = 2 })).StatusCode);

        matches = await organizer.GetFromJsonAsync<JsonElement[]>($"/api/tournaments/{tournamentId}/matches");
        var disputeId = matches![0].GetProperty("dispute").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await organizer.PutAsJsonAsync($"/api/disputes/{disputeId}/resolve", new
                { homeScore = 2, awayScore = 1, resolutionNote = "Conferido pelo organizador" })).StatusCode);
    }

    [Fact]
    public async Task AutopilotAndRepairRequireTournamentOrganizer()
    {
        var (owner, _) = await factory.RegisterUser();
        var tournamentId = await CreateTournament(owner, "Protected schedule");
        var (viewer, _) = await factory.RegisterUser();
        var horizon = new
        {
            horizonStart = "2026-11-15T09:00:00Z",
            horizonEnd = "2026-11-15T18:00:00Z",
            preserveExisting = true,
            requireReferee = false
        };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/tournaments/{tournamentId}/schedule/generate", horizon)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync($"/api/tournaments/{tournamentId}/schedule/repair/generate", new
            {
                horizon,
                incident = new { kind = "teamUnavailable", resourceId = Guid.NewGuid(), startsAt = "2026-11-15T10:00:00Z", endsAt = "2026-11-15T11:00:00Z" }
            })).StatusCode);
    }

    [Fact]
    public async Task ConfiguredAdminBootstrapIsIdempotent()
    {
        var email = $"bootstrap-{Guid.NewGuid():N}@brakvyo.local";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdminEmail"] = email,
            ["Auth:BootstrapAdminPassword"] = "Bootstrap-test-2026",
            ["Auth:BootstrapAdminDisplayName"] = "Bootstrap Admin"
        }).Build();

        await AuthBootstrap.Seed(factory.Services, config);
        await AuthBootstrap.Seed(factory.Services, config);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await users.IsInRoleAsync(user!, "PlatformAdmin"));
        Assert.Equal(1, await db.Users.CountAsync(x => x.NormalizedEmail == email.ToUpperInvariant()));
    }

    private static async Task<Guid> CreateTournament(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
            { name, sport = "Futsal", maxTeams = 4, format = "RoundRobin" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTeam(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/teams", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
