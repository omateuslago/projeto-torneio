using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Features;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

public sealed class NotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UserUpdatesOnlyOwnPreferenceAndInvalidPhoneIsRejected()
    {
        var (first, firstUser) = await factory.RegisterUser();
        var saved = await first.PutAsJsonAsync("/api/me/notification-preferences", new
            { whatsAppPhoneNumber = "+55 (11) 99999-9999", whatsAppEnabled = true });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var preference = await saved.Content.ReadFromJsonAsync<NotificationPreferenceResponse>();
        Assert.Equal("+5511999999999", preference!.WhatsAppPhoneNumber);
        Assert.True(preference.WhatsAppEnabled);

        var (other, _) = await factory.RegisterUser();
        var otherPreference = await other.GetFromJsonAsync<NotificationPreferenceResponse>(
            "/api/me/notification-preferences");
        Assert.Null(otherPreference!.WhatsAppPhoneNumber);
        Assert.False(otherPreference.WhatsAppEnabled);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await other.PutAsJsonAsync("/api/me/notification-preferences", new
                { whatsAppPhoneNumber = "119999", whatsAppEnabled = true })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        Assert.Equal("+5511999999999", (await db.UserNotificationPreferences
            .SingleAsync(x => x.UserId == firstUser.Id)).WhatsAppPhoneNumber);
    }

    [Fact]
    public async Task MatchEventsDeduplicateManagersAndDisabledDeliveryIsSkipped()
    {
        var (_, manager) = await factory.RegisterUser();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var managerId = manager.Id!.Value;
        var seeded = await SeedMatch(db, managerId, managerId, managerId);
        var preference = new UserNotificationPreference(managerId, DateTimeOffset.UtcNow);
        preference.Update("+5511999999999", false, DateTimeOffset.UtcNow);
        db.UserNotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var enabled = new NotificationOutbox(db, Options.Create(new WhatsAppOptions { Enabled = true }));
        await enabled.MatchScheduled(seeded.Match.Id, false, default);
        await enabled.MatchScheduled(seeded.Match.Id, false, default);
        await db.SaveChangesAsync();
        var scheduled = await db.Notifications.SingleAsync(x => x.Type == NotificationType.MatchScheduled
            && x.MatchId == seeded.Match.Id);
        Assert.Single(await db.NotificationDeliveries.Where(x => x.NotificationId == scheduled.Id).ToArrayAsync());

        var disabledSeed = await SeedMatch(db, managerId, managerId, managerId);
        var disabled = new NotificationOutbox(db, Options.Create(new WhatsAppOptions { Enabled = false }));
        await disabled.MatchScheduled(disabledSeed.Match.Id, false, default);
        await db.SaveChangesAsync();
        Assert.Equal(NotificationDeliveryStatus.Skipped,
            (await db.NotificationDeliveries.SingleAsync(x => x.NotificationId == db.Notifications
                .Where(n => n.MatchId == disabledSeed.Match.Id).Select(n => n.Id).Single())).Status);
    }

    [Fact]
    public async Task DisputesAndRepairTargetOnlyRelevantUsers()
    {
        var (_, organizer) = await factory.RegisterUser();
        var (_, homeManager) = await factory.RegisterUser();
        var (_, awayManager) = await factory.RegisterUser();
        var (_, unrelatedManager) = await factory.RegisterUser();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var organizerId = organizer.Id!.Value;
        var homeManagerId = homeManager.Id!.Value;
        var awayManagerId = awayManager.Id!.Value;
        var unrelatedManagerId = unrelatedManager.Id!.Value;
        var seeded = await SeedMatch(db, homeManagerId, awayManagerId, organizerId);
        var unrelatedTeam = new Team("Unrelated");
        db.Teams.Add(unrelatedTeam);
        db.TeamManagers.Add(new TeamManager(unrelatedManagerId, unrelatedTeam.Id));
        await db.SaveChangesAsync();
        var outbox = new NotificationOutbox(db, Options.Create(new WhatsAppOptions { Enabled = false }));

        var disputeId = Guid.NewGuid();
        await outbox.DisputeOpened(disputeId, seeded.Match.Id, default);
        await outbox.DisputeResolved(disputeId, seeded.Match.Id, default);
        await outbox.ScheduleRepaired(seeded.Tournament.Id, [seeded.Match.Id], default);
        await db.SaveChangesAsync();

        var opened = await DeliveryUsers(db, NotificationType.ScoreDisputeOpened, seeded.Match.Id);
        var resolved = await DeliveryUsers(db, NotificationType.ScoreDisputeResolved, seeded.Match.Id);
        var repaired = await DeliveryUsers(db, NotificationType.ScheduleRepaired, null, seeded.Tournament.Id);
        Assert.Equal([organizerId], opened);
        Assert.Equal(new[] { awayManagerId, homeManagerId }.Order(), resolved);
        Assert.Equal(new[] { awayManagerId, homeManagerId }.Order(), repaired);
        Assert.DoesNotContain(unrelatedManagerId, repaired);
    }

    [Fact]
    public async Task ProcessorPersistsProviderIdRetriesTransientAndFailsPermanent()
    {
        var (_, user) = await factory.RegisterUser();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        var userId = user.Id!.Value;
        var preference = new UserNotificationPreference(userId, DateTimeOffset.UtcNow);
        preference.Update("+5511999999999", true, DateTimeOffset.UtcNow);
        db.UserNotificationPreferences.Add(preference);
        var first = AddPending(db, userId, "worker-sent");
        await db.SaveChangesAsync();
        var sender = new FakeSender { Result = new("provider-123") };
        var processor = new NotificationDeliveryProcessor(db, sender, new NotificationRenderer(), TimeProvider.System);
        Assert.True(await processor.ProcessOne(default));
        Assert.Equal(NotificationDeliveryStatus.Sent, first.Status);
        Assert.Equal("provider-123", first.ProviderMessageId);
        Assert.Equal(1, sender.Calls);

        var transient = AddPending(db, userId, "worker-transient");
        await db.SaveChangesAsync();
        sender.Error = new WhatsAppSendException("Temporary provider failure.", false);
        Assert.True(await processor.ProcessOne(default));
        Assert.Equal(NotificationDeliveryStatus.Pending, transient.Status);
        Assert.Equal(1, transient.AttemptCount);
        Assert.True(transient.NextAttemptAt > DateTimeOffset.UtcNow);

        var permanent = AddPending(db, userId, "worker-permanent");
        await db.SaveChangesAsync();
        sender.Error = new WhatsAppSendException("Provider rejected request.", true);
        Assert.True(await processor.ProcessOne(default));
        Assert.Equal(NotificationDeliveryStatus.Failed, permanent.Status);
        Assert.DoesNotContain("apikey", permanent.LastError ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeliveryStopsAfterThreeAttempts()
    {
        var delivery = new NotificationDelivery(Guid.NewGuid(), Guid.NewGuid(), true, DateTimeOffset.UtcNow);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            delivery.Start(DateTimeOffset.UtcNow);
            delivery.Fail("Temporary", false, DateTimeOffset.UtcNow, TimeSpan.Zero);
        }
        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public async Task EvolutionSenderUsesSendTextContractAndPersistsOpaqueId()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK,
            "{\"key\":{\"id\":\"opaque-provider-id\"}}");
        var sender = new EvolutionWhatsAppSender(new HttpClient(handler), Options.Create(new WhatsAppOptions
        {
            Enabled = true,
            BaseUrl = "https://evolution.example",
            ApiKey = "test-api-key",
            InstanceName = "brakvyo-test"
        }));
        var result = await sender.SendAsync("+5511999999999", "Mensagem", default);
        Assert.Equal("opaque-provider-id", result.ProviderMessageId);
        Assert.Equal("https://evolution.example/message/sendText/brakvyo-test", handler.Uri);
        Assert.Equal("test-api-key", handler.ApiKey);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("5511999999999", body.RootElement.GetProperty("number").GetString());
        Assert.Equal("Mensagem", body.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task EvolutionSenderClassifiesPermanentAndTransientFailures(HttpStatusCode status, bool permanent)
    {
        var sender = new EvolutionWhatsAppSender(new HttpClient(new StubHttpHandler(status, "{}")),
            Options.Create(new WhatsAppOptions
            {
                Enabled = true, BaseUrl = "https://evolution.example", ApiKey = "key", InstanceName = "instance"
            }));
        var error = await Assert.ThrowsAsync<WhatsAppSendException>(() =>
            sender.SendAsync("+5511999999999", "Mensagem", default));
        Assert.Equal(permanent, error.Permanent);
    }

    [Fact]
    public void EvolutionWebhookRequiresConfiguredSecret()
    {
        var disabled = new EvolutionWebhookController(Options.Create(new WhatsAppOptions()));
        Assert.IsType<NotFoundResult>(disabled.Receive(null));
        var secured = new EvolutionWebhookController(Options.Create(new WhatsAppOptions { WebhookSecret = "secret" }));
        Assert.IsType<UnauthorizedResult>(secured.Receive("wrong"));
        Assert.IsType<NoContentResult>(secured.Receive("secret"));
    }

    [Fact]
    public async Task UserListsOnlyOwnNotifications()
    {
        var (firstClient, first) = await factory.RegisterUser();
        var (_, other) = await factory.RegisterUser();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
            AddPending(db, first.Id!.Value, "inbox-first");
            AddPending(db, other.Id!.Value, "inbox-other");
            await db.SaveChangesAsync();
        }
        var notifications = await firstClient.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/me/notifications");
        Assert.Single(notifications!);
        Assert.Equal("Partida agendada", notifications![0].GetProperty("title").GetString());
    }

    private static NotificationDelivery AddPending(TournamentDbContext db, Guid userId, string key)
    {
        var notification = new Notification(NotificationType.MatchScheduled,
            System.Text.Json.JsonSerializer.Serialize(new MatchNotificationPayload("Home", "Away", DateTime.UtcNow, null, null)), key);
        var delivery = new NotificationDelivery(notification.Id, userId, true, DateTimeOffset.UtcNow);
        db.Notifications.Add(notification);
        db.NotificationDeliveries.Add(delivery);
        return delivery;
    }

    private static async Task<Guid[]> DeliveryUsers(TournamentDbContext db, NotificationType type,
        Guid? matchId = null, Guid? tournamentId = null) => await (
        from delivery in db.NotificationDeliveries
        join notification in db.Notifications on delivery.NotificationId equals notification.Id
        where notification.Type == type && (matchId == null || notification.MatchId == matchId)
            && (tournamentId == null || notification.TournamentId == tournamentId)
        select delivery.UserId).Order().ToArrayAsync();

    private static async Task<(Tournament Tournament, Match Match)> SeedMatch(TournamentDbContext db,
        Guid homeManager, Guid awayManager, Guid organizer)
    {
        var tournament = new Tournament($"Notification Cup {Guid.NewGuid():N}", "Futsal", 4, TournamentFormat.RoundRobin);
        var home = new Team($"Home {Guid.NewGuid():N}");
        var away = new Team($"Away {Guid.NewGuid():N}");
        var venue = new Venue(tournament.Id, "Venue", "Address");
        var area = new PlayingArea(tournament.Id, venue.Id, "Court");
        var match = new Match(tournament.Id, new[] { home.Id, away.Id }.Min(), new[] { home.Id, away.Id }.Max());
        match.Schedule(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), area.Id, null);
        db.AddRange(tournament, home, away, venue, area,
            new Registration(tournament.Id, home.Id), new Registration(tournament.Id, away.Id), match,
            new TournamentOrganizer(organizer, tournament.Id),
            new TeamManager(homeManager, home.Id), new TeamManager(awayManager, away.Id));
        await db.SaveChangesAsync();
        return (tournament, match);
    }

    private sealed class FakeSender : IWhatsAppSender
    {
        public int Calls { get; private set; }
        public WhatsAppSendResult Result { get; set; } = new(null);
        public WhatsAppSendException? Error { get; set; }
        public Task<WhatsAppSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct)
        {
            Calls++;
            if (Error is not null) throw Error;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubHttpHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("apikey", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
