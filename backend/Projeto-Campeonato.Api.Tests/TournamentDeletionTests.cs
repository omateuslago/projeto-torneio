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

public sealed class TournamentDeletionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task OnlyRecordedOwnerMayDelete()
    {
        var (owner, user) = await factory.RegisterUser();
        var (organizer, other) = await factory.RegisterUser();
        using var admin = factory.CreateClient();
        using var anonymous = factory.CreateAnonymousClient();
        var cup = await Create(owner);
        var path = $"/api/tournaments/{cup.Id}";
        await factory.GrantTournament(other.Id!.Value, cup.Id);
        Assert.True(cup.CanDelete);
        Assert.True((await owner.GetFromJsonAsync<TournamentResponse>(path, Json))!.CanDelete);
        Assert.False((await organizer.GetFromJsonAsync<TournamentResponse>(path, Json))!.CanDelete);
        Assert.Equal(HttpStatusCode.Forbidden, (await organizer.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync(path)).StatusCode);
        factory.AddCsrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/tournaments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync(path)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var legacy = new Tournament("Copa sem proprietário registrado", "Futsal", 2, TournamentFormat.RoundRobin);
        db.AddRange(legacy, new TournamentOrganizer(user.Id!.Value, legacy.Id));
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.DeleteAsync($"/api/tournaments/{legacy.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("RoundRobin", "Open")]
    [InlineData("RoundRobin", "Closed")]
    [InlineData("RoundRobin", "Disputed")]
    [InlineData("RoundRobin", "Finished")]
    [InlineData("SingleElimination", "InProgress")]
    public async Task RemovesOnlyTheSelectedTournamentGraph(string format, string state)
    {
        var (owner, user) = await factory.RegisterUser();
        var cup = await Create(owner, format);
        var other = await Create(owner);
        var path = $"/api/tournaments/{cup.Id}";
        var otherPath = $"/api/tournaments/{other.Id}";
        var userId = user.Id!.Value;
        var teamIds = new List<Guid>();
        await SetStatus(owner, path, "Open");
        await SetStatus(owner, otherPath, "Open");
        for (var i = 0; i < (format == "SingleElimination" ? 4 : 2); i++)
        {
            var team = await Read<TeamResponse>(await owner.PostAsJsonAsync("/api/teams", new { name = $"Equipe {i}" }));
            teamIds.Add(team.Id);
            (await owner.PostAsJsonAsync($"{path}/registrations", new { teamId = team.Id })).EnsureSuccessStatusCode();
            if (i < 2)
                (await owner.PostAsJsonAsync($"{otherPath}/registrations", new { teamId = team.Id })).EnsureSuccessStatusCode();
        }
        await SetStatus(owner, otherPath, "Closed");
        var otherMatch = Assert.Single(await Read<MatchResponse[]>(await owner.PostAsync($"{otherPath}/matches/generate", null)));
        if (state != "Open") await SetStatus(owner, path, "Closed");
        MatchResponse[] matches = state is "Open" or "Closed" ? [] :
            await Read<MatchResponse[]>(await owner.PostAsync($"{path}/matches/generate", null));
        var matchIds = matches.Select(x => x.Id).ToArray();
        if (state == "Disputed")
        {
            var match = Assert.Single(matches);
            (await owner.PutAsJsonAsync($"/api/matches/{match.Id}/score-submissions/{match.HomeTeamId}",
                new { homeScore = 2, awayScore = 1 })).EnsureSuccessStatusCode();
            (await owner.PutAsJsonAsync($"/api/matches/{match.Id}/score-submissions/{match.AwayTeamId}",
                new { homeScore = 3, awayScore = 1 })).EnsureSuccessStatusCode();
        }
        if (state == "Finished")
        {
            (await owner.PutAsJsonAsync($"/api/matches/{Assert.Single(matches).Id}/result",
                new { homeScore = 2, awayScore = 1 })).EnsureSuccessStatusCode();
            Assert.Equal(TournamentStatus.Finished, (await owner.GetFromJsonAsync<TournamentResponse>(path, Json))!.Status);
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var venue = new Venue(cup.Id, "Ginásio", "Rua 1");
        var area = new PlayingArea(cup.Id, venue.Id, "Quadra");
        var referee = new Referee(cup.Id, "Árbitro");
        var start = DateTime.UtcNow.AddDays(1);
        var end = start.AddHours(1);
        var tournamentNotice = new Notification(NotificationType.MatchScheduled, "{}", Guid.NewGuid().ToString(), tournamentId: cup.Id);
        var sharedNotice = new Notification(NotificationType.MatchScheduled, "{}", Guid.NewGuid().ToString(), tournamentId: other.Id, matchId: otherMatch.Id);
        db.AddRange(venue, area, referee, tournamentNotice, sharedNotice,
            new NotificationDelivery(tournamentNotice.Id, userId, true, DateTimeOffset.UtcNow),
            new Availability(cup.Id, "teams", teamIds[0], start, end),
            new Availability(cup.Id, "playing-areas", area.Id, start, end),
            new Availability(cup.Id, "referees", referee.Id, start, end),
            AccessInvitation.ForTournament(cup.Id, "invite@example.test", "INVITE@EXAMPLE.TEST", userId, DateTimeOffset.UtcNow),
            AccessInvitation.ForTeam(teamIds[0], "invite@example.test", "INVITE@EXAMPLE.TEST", userId, DateTimeOffset.UtcNow));
        if (matches.Length > 0)
        {
            var match = await db.Matches.SingleAsync(x => x.Id == matchIds[0]);
            if (state != "Finished") match.Schedule(start, end, area.Id, referee.Id);
            var matchNotice = new Notification(NotificationType.MatchScheduled, "{}", Guid.NewGuid().ToString(), matchId: match.Id);
            db.AddRange(matchNotice, new NotificationDelivery(matchNotice.Id, userId, true, DateTimeOffset.UtcNow));
        }
        await db.SaveChangesAsync();
        var notificationIds = await db.Notifications.Where(x => x.TournamentId == cup.Id ||
            (x.MatchId.HasValue && matchIds.Contains(x.MatchId.Value))).Select(x => x.Id).ToArrayAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(path)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.False(await db.Tournaments.AnyAsync(x => x.Id == cup.Id));
        Assert.False(await db.Registrations.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.Matches.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.BracketNodes.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.ScoreSubmissions.AnyAsync(x => matchIds.Contains(x.MatchId)));
        Assert.False(await db.Disputes.AnyAsync(x => matchIds.Contains(x.MatchId)));
        Assert.False(await db.AuditLogs.AnyAsync(x => matchIds.Contains(x.MatchId)));
        Assert.False(await db.Availabilities.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.PlayingAreas.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.Venues.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.Referees.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.AccessInvitations.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.TournamentOrganizers.AnyAsync(x => x.TournamentId == cup.Id));
        Assert.False(await db.Notifications.AnyAsync(x => notificationIds.Contains(x.Id)));
        Assert.False(await db.NotificationDeliveries.AnyAsync(x => notificationIds.Contains(x.NotificationId)));
        Assert.True(await db.Users.AnyAsync(x => x.Id == userId));
        Assert.Equal(teamIds.Count, await db.Teams.CountAsync(x => teamIds.Contains(x.Id)));
        Assert.Equal(teamIds.Count, await db.TeamManagers.CountAsync(x => x.UserId == userId && teamIds.Contains(x.TeamId)));
        Assert.True(await db.AccessInvitations.AnyAsync(x => x.TeamId == teamIds[0]));
        Assert.True(await db.Tournaments.AnyAsync(x => x.Id == other.Id));
        Assert.True(await db.Matches.AnyAsync(x => x.Id == otherMatch.Id));
        Assert.Equal(2, await db.Registrations.CountAsync(x => x.TournamentId == other.Id));
        Assert.True(await db.Notifications.AnyAsync(x => x.Id == sharedNotice.Id));
    }

    [Fact]
    public async Task ARestrictFailureRollsBackEarlierDeletes()
    {
        var (owner, _) = await factory.RegisterUser();
        var cup = await Create(owner, "SingleElimination");
        var other = await Create(owner, "SingleElimination");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var target = new BracketNode(cup.Id, 1, 1);
        var foreignNode = new BracketNode(other.Id, 1, 1);
        // Simulate a pre-existing inconsistent reference that the schema allows but RESTRICT protects.
        foreignNode.ConfigureNext(target.Id, BracketSlot.Top);
        var notification = new Notification(NotificationType.MatchScheduled, "{}", Guid.NewGuid().ToString(),
            tournamentId: cup.Id);
        db.AddRange(target, foreignNode, notification);
        await db.SaveChangesAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/tournaments/{cup.Id}")).StatusCode);
        Assert.True(await db.Tournaments.AnyAsync(x => x.Id == cup.Id));
        Assert.True(await db.Notifications.AnyAsync(x => x.Id == notification.Id));
        Assert.True(await db.BracketNodes.AnyAsync(x => x.Id == foreignNode.Id && x.NextNodeId == target.Id));
    }

    private static async Task<TournamentResponse> Create(HttpClient client, string format = "RoundRobin") =>
        await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments",
            new { name = "Campeonato de teste", sport = "Futsal", maxTeams = 4, format }));

    private static async Task SetStatus(HttpClient client, string path, string status) =>
        (await client.PutAsJsonAsync($"{path}/status", new { status })).EnsureSuccessStatusCode();

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<T>(Json))!;
}
