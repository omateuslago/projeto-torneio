using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record NotificationPreferenceRequest([StringLength(40)] string? WhatsAppPhoneNumber,
    bool WhatsAppEnabled);
public sealed record NotificationPreferenceResponse(string? WhatsAppPhoneNumber, bool WhatsAppEnabled);
public sealed record MyNotificationResponse(Guid Id, NotificationType Type, string Title, string Summary,
    DateTimeOffset CreatedAt, NotificationDeliveryStatus WhatsAppStatus);

[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MyNotificationsController(TournamentDbContext db, IAccessControl access,
    INotificationRenderer renderer) : ControllerBase
{
    [HttpGet("notification-preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> Preferences(CancellationToken ct)
    {
        var userId = access.UserId(User);
        var preference = await db.UserNotificationPreferences.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return Ok(new NotificationPreferenceResponse(preference?.WhatsAppPhoneNumber,
            preference?.WhatsAppEnabled ?? false));
    }

    [HttpPut("notification-preferences")]
    public async Task<ActionResult<NotificationPreferenceResponse>> Preferences(
        NotificationPreferenceRequest request, CancellationToken ct)
    {
        var phone = NormalizePhone(request.WhatsAppPhoneNumber);
        if (phone is not null && !Regex.IsMatch(phone, @"^\+[1-9]\d{7,14}$"))
            return ValidationProblem("Use um telefone internacional válido, como +5511999999999.");
        if (request.WhatsAppEnabled && phone is null)
            return ValidationProblem("Informe um telefone antes de habilitar o WhatsApp.");
        var userId = access.UserId(User);
        var preference = await db.UserNotificationPreferences.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (preference is null)
        {
            preference = new UserNotificationPreference(userId, DateTimeOffset.UtcNow);
            db.UserNotificationPreferences.Add(preference);
        }
        preference.Update(phone, request.WhatsAppEnabled, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Ok(new NotificationPreferenceResponse(preference.WhatsAppPhoneNumber, preference.WhatsAppEnabled));
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<MyNotificationResponse[]>> Notifications(CancellationToken ct)
    {
        var userId = access.UserId(User);
        var rows = await (from delivery in db.NotificationDeliveries.AsNoTracking()
            join notification in db.Notifications.AsNoTracking() on delivery.NotificationId equals notification.Id
            where delivery.UserId == userId
            orderby notification.CreatedAt descending
            select new { Notification = notification, delivery.Status }).Take(100).ToArrayAsync(ct);
        return Ok(rows.Select(row =>
        {
            var rendered = renderer.Render(row.Notification);
            return new MyNotificationResponse(row.Notification.Id, row.Notification.Type, rendered.Title,
                rendered.Summary, row.Notification.CreatedAt, row.Status);
        }).ToArray());
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Regex.Replace(value.Trim(), @"[\s()\-]", "");
    }
}
