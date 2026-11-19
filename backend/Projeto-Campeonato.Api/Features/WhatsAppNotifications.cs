using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? InstanceName { get; set; }
    public string? WebhookSecret { get; set; }
}

public sealed record WhatsAppSendResult(string? ProviderMessageId, bool Skipped = false);

public sealed class WhatsAppSendException(string message, bool permanent, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Permanent { get; } = permanent;
}

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct);
}

public sealed class DisabledWhatsAppSender : IWhatsAppSender
{
    public Task<WhatsAppSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct) =>
        Task.FromResult(new WhatsAppSendResult(null, true));
}

public sealed class EvolutionWhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppSender
{
    public async Task<WhatsAppSendResult> SendAsync(string phoneNumber, string message, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey)
            || string.IsNullOrWhiteSpace(settings.InstanceName))
            throw new WhatsAppSendException("WhatsApp provider configuration is incomplete.", true);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.BaseUrl.TrimEnd('/')}/message/sendText/{Uri.EscapeDataString(settings.InstanceName)}");
        request.Headers.TryAddWithoutValidation("apikey", settings.ApiKey);
        request.Content = JsonContent.Create(new { number = phoneNumber.TrimStart('+'), text = message });
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WhatsAppSendException("WhatsApp provider is temporarily unavailable.", false, ex);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var permanent = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity;
                throw new WhatsAppSendException($"WhatsApp provider returned HTTP {(int)response.StatusCode}.", permanent);
            }
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
                var id = document.RootElement.TryGetProperty("key", out var key)
                    && key.TryGetProperty("id", out var value) ? value.GetString() : null;
                return new WhatsAppSendResult(id);
            }
            catch (JsonException)
            {
                return new WhatsAppSendResult(null);
            }
        }
    }
}

public sealed class NotificationDeliveryProcessor(TournamentDbContext db, IWhatsAppSender sender,
    INotificationRenderer renderer, TimeProvider timeProvider)
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    public async Task<bool> ProcessOne(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var delivery = await db.NotificationDeliveries.FromSqlRaw("""
            SELECT * FROM "NotificationDeliveries"
            WHERE ("Status" = 'Pending' AND "NextAttemptAt" <= {0})
               OR ("Status" = 'Processing' AND "LastAttemptAt" < {1})
            ORDER BY "NextAttemptAt", "CreatedAt"
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """, now, now.AddMinutes(-5)).SingleOrDefaultAsync(ct);
        if (delivery is null)
        {
            await transaction.CommitAsync(ct);
            return false;
        }
        delivery.Start(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var preference = await db.UserNotificationPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == delivery.UserId, ct);
        if (preference is null || !preference.WhatsAppEnabled || preference.WhatsAppPhoneNumber is null)
        {
            delivery.Skip();
            await db.SaveChangesAsync(ct);
            return true;
        }
        var notification = await db.Notifications.AsNoTracking()
            .SingleAsync(x => x.Id == delivery.NotificationId, ct);
        try
        {
            var result = await sender.SendAsync(preference.WhatsAppPhoneNumber,
                renderer.Render(notification).WhatsAppMessage, ct);
            if (result.Skipped) delivery.Skip();
            else delivery.Sent(result.ProviderMessageId, timeProvider.GetUtcNow());
        }
        catch (WhatsAppSendException ex)
        {
            var index = Math.Clamp(delivery.AttemptCount - 1, 0, Backoff.Length - 1);
            delivery.Fail(ex.Message, ex.Permanent, timeProvider.GetUtcNow(), Backoff[index]);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class NotificationDeliveryWorker(IServiceScopeFactory scopes,
    ILogger<NotificationDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<NotificationDeliveryProcessor>()
                    .ProcessOne(stoppingToken);
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification delivery worker failed; retrying later.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}

[ApiController]
[Route("api/webhooks/evolution")]
public sealed class EvolutionWebhookController(IOptions<WhatsAppOptions> options) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult Receive([FromHeader(Name = "X-Brakvyo-Webhook-Secret")] string? secret)
    {
        var expected = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(expected)) return NotFound();
        if (secret is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(expected))) return Unauthorized();
        return NoContent();
    }
}
