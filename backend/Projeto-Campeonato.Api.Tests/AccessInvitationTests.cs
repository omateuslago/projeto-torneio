using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projeto_Campeonato.Api.Features;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

public sealed class AccessInvitationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task OrganizerCreatesInvitationOnlyForOwnTournamentAndAdminBypasses()
    {
        var (owner, _) = await factory.RegisterUser(displayName: "Owner");
        var owned = await CreateTournament(owner, "Invitation Cup");
        var (other, _) = await factory.RegisterUser(displayName: "Other");
        var foreign = await CreateTournament(other, "Foreign Cup");
        var email = UniqueEmail();

        Assert.Equal(HttpStatusCode.Created,
            (await owner.PostAsJsonAsync($"/api/tournaments/{owned}/organizer-invitations", new { email })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.PostAsJsonAsync($"/api/tournaments/{foreign}/organizer-invitations", new { email = UniqueEmail() })).StatusCode);

        using var admin = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/tournaments/{foreign}/organizer-invitations", new { email = UniqueEmail() })).StatusCode);
    }

    [Fact]
    public async Task TeamManagerCreatesInvitationOnlyForOwnTeam()
    {
        var (manager, _) = await factory.RegisterUser();
        var owned = await CreateTeam(manager, "Owned team");
        var (other, _) = await factory.RegisterUser();
        var foreign = await CreateTeam(other, "Foreign team");

        Assert.Equal(HttpStatusCode.Created,
            (await manager.PostAsJsonAsync($"/api/teams/{owned}/manager-invitations", new { email = UniqueEmail() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync($"/api/teams/{foreign}/manager-invitations", new { email = UniqueEmail() })).StatusCode);
    }

    [Fact]
    public async Task CorrectEmailAcceptsOrganizerInvitationAndMeReflectsMembership()
    {
        var email = UniqueEmail();
        var (owner, _) = await factory.RegisterUser();
        var tournamentId = await CreateTournament(owner, "Accepted Cup");
        var invitation = await Invite(owner, $"/api/tournaments/{tournamentId}/organizer-invitations", email);
        var (invitee, _) = await factory.RegisterUser(email, "Invitee");

        var preview = await invitee.GetFromJsonAsync<InvitationPreviewResponse>(
            $"/api/access-invitations/preview?token={Uri.EscapeDataString(invitation.Token)}");
        Assert.Equal("TournamentOrganizer", preview!.Type);
        Assert.Equal("Accepted Cup", preview.TargetName);

        Assert.Equal(HttpStatusCode.OK,
            (await invitee.PostAsJsonAsync("/api/access-invitations/accept", new { invitation.Token })).StatusCode);
        var current = await invitee.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Contains(tournamentId, current!.OrganizerTournamentIds);
        Assert.Equal(HttpStatusCode.Conflict,
            (await invitee.PostAsJsonAsync("/api/access-invitations/accept", new { invitation.Token })).StatusCode);
    }

    [Fact]
    public async Task CorrectEmailAcceptsTeamInvitationButDifferentEmailIsForbidden()
    {
        var invitedEmail = UniqueEmail();
        var (owner, _) = await factory.RegisterUser();
        var teamId = await CreateTeam(owner, "Accepted team");
        var invitation = await Invite(owner, $"/api/teams/{teamId}/manager-invitations", invitedEmail);
        var (wrong, _) = await factory.RegisterUser();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await wrong.PostAsJsonAsync("/api/access-invitations/accept", new { invitation.Token })).StatusCode);

        var (invitee, _) = await factory.RegisterUser(invitedEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await invitee.PostAsJsonAsync("/api/access-invitations/accept", new { invitation.Token })).StatusCode);
        Assert.Contains(teamId, (await invitee.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!.ManagedTeamIds);
    }

    [Fact]
    public async Task TamperedExpiredAndRevokedInvitationsAreRejected()
    {
        var (owner, _) = await factory.RegisterUser();
        var tournamentId = await CreateTournament(owner, "Invalid invitations");
        var email = UniqueEmail();
        var tampered = await Invite(owner, $"/api/tournaments/{tournamentId}/organizer-invitations", email);
        var (invitee, _) = await factory.RegisterUser(email);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await invitee.PostAsJsonAsync("/api/access-invitations/accept", new { token = tampered.Token + "x" })).StatusCode);

        var expiredEmail = UniqueEmail();
        var expired = await Invite(owner, $"/api/tournaments/{tournamentId}/organizer-invitations", expiredEmail);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
            await db.AccessInvitations.Where(x => x.Id == expired.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var (expiredUser, _) = await factory.RegisterUser(expiredEmail);
        Assert.Equal(HttpStatusCode.Conflict,
            (await expiredUser.PostAsJsonAsync("/api/access-invitations/accept", new { expired.Token })).StatusCode);

        var revokedEmail = UniqueEmail();
        var revoked = await Invite(owner, $"/api/tournaments/{tournamentId}/organizer-invitations", revokedEmail);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/tournaments/{tournamentId}/organizer-invitations/{revoked.Id}")).StatusCode);
        var (revokedUser, _) = await factory.RegisterUser(revokedEmail);
        Assert.Equal(HttpStatusCode.Conflict,
            (await revokedUser.PostAsJsonAsync("/api/access-invitations/accept", new { revoked.Token })).StatusCode);
    }

    [Fact]
    public async Task DuplicateExistingMemberAndLastMemberRulesReturnConflict()
    {
        var (owner, user) = await factory.RegisterUser();
        var tournamentId = await CreateTournament(owner, "Conflict Cup");
        var pendingEmail = UniqueEmail();
        await Invite(owner, $"/api/tournaments/{tournamentId}/organizer-invitations", pendingEmail);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync($"/api/tournaments/{tournamentId}/organizer-invitations", new { email = pendingEmail.ToUpperInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync($"/api/tournaments/{tournamentId}/organizer-invitations", new { email = user.Email })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/tournaments/{tournamentId}/organizers/{user.Id}")).StatusCode);

        using var admin = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.DeleteAsync($"/api/tournaments/{tournamentId}/organizers/{user.Id}")).StatusCode);

        var teamId = await CreateTeam(owner, "Last manager team");
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.DeleteAsync($"/api/teams/{teamId}/managers/{user.Id}")).StatusCode);
    }

    [Fact]
    public async Task AntiforgeryRemainsRequiredForInvitationWrites()
    {
        var (owner, _) = await factory.RegisterUser();
        var tournamentId = await CreateTournament(owner, "Csrf Cup");
        owner.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/tournaments/{tournamentId}/organizer-invitations", new { email = UniqueEmail() })).StatusCode);
    }

    private static async Task<CreatedInvitationResponse> Invite(HttpClient client, string path, string email)
    {
        var response = await client.PostAsJsonAsync(path, new { email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedInvitationResponse>())!;
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

    private static string UniqueEmail() => $"invited-{Guid.NewGuid():N}@example.com";
}
