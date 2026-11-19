using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record MatchNotificationPayload(string HomeTeam, string AwayTeam, DateTime? StartsAt,
    string? Venue, string? PlayingArea, int? ChangedMatches = null);

public interface INotificationOutbox
{
    Task MatchScheduled(Guid matchId, bool rescheduled, CancellationToken ct);
    Task DisputeOpened(Guid disputeId, Guid matchId, CancellationToken ct);
    Task DisputeResolved(Guid disputeId, Guid matchId, CancellationToken ct);
    Task ScheduleRepaired(Guid tournamentId, IReadOnlyCollection<Guid> changedMatchIds, CancellationToken ct);
}

public sealed class NotificationOutbox(TournamentDbContext db, IOptions<WhatsAppOptions> options) : INotificationOutbox
{
    public async Task MatchScheduled(Guid matchId, bool rescheduled, CancellationToken ct)
    {
        var match = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        var payload = await MatchPayload(match, ct);
        var type = rescheduled ? NotificationType.MatchRescheduled : NotificationType.MatchScheduled;
        var prefix = rescheduled ? "match-rescheduled" : "match-scheduled";
        var recipients = await db.TeamManagers.Where(x => x.TeamId == match.HomeTeamId || x.TeamId == match.AwayTeamId)
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        await Add(type, payload, $"{prefix}:{match.Id}:{match.StartsAt:O}", recipients,
            match.TournamentId, null, match.Id, ct);
    }

    public async Task DisputeOpened(Guid disputeId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        var recipients = await db.TournamentOrganizers.Where(x => x.TournamentId == match.TournamentId)
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        await Add(NotificationType.ScoreDisputeOpened, await MatchPayload(match, ct),
            $"dispute-opened:{disputeId}", recipients, match.TournamentId, null, match.Id, ct);
    }

    public async Task DisputeResolved(Guid disputeId, Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        var recipients = await db.TeamManagers.Where(x => x.TeamId == match.HomeTeamId || x.TeamId == match.AwayTeamId)
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        await Add(NotificationType.ScoreDisputeResolved, await MatchPayload(match, ct),
            $"dispute-resolved:{disputeId}", recipients, match.TournamentId, null, match.Id, ct);
    }

    public async Task ScheduleRepaired(Guid tournamentId, IReadOnlyCollection<Guid> changedMatchIds, CancellationToken ct)
    {
        if (changedMatchIds.Count == 0) return;
        var matches = await db.Matches.Where(x => changedMatchIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct);
        var teamIds = matches.SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId }).Distinct().ToArray();
        var recipients = await db.TeamManagers.Where(x => teamIds.Contains(x.TeamId))
            .Select(x => x.UserId).Distinct().ToArrayAsync(ct);
        var first = matches[0];
        var payload = (await MatchPayload(first, ct)) with { ChangedMatches = matches.Length };
        var stable = string.Join('|', matches.Select(x => $"{x.Id:N}:{x.StartsAt:O}"));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stable))).ToLowerInvariant();
        await Add(NotificationType.ScheduleRepaired, payload, $"repair-applied:{tournamentId}:{hash}",
            recipients, tournamentId, null, null, ct);
    }

    private async Task<MatchNotificationPayload> MatchPayload(Match match, CancellationToken ct)
    {
        var home = await db.Teams.Where(x => x.Id == match.HomeTeamId).Select(x => x.Name).SingleAsync(ct);
        var away = await db.Teams.Where(x => x.Id == match.AwayTeamId).Select(x => x.Name).SingleAsync(ct);
        string? area = null;
        string? venue = null;
        if (match.PlayingAreaId.HasValue)
        {
            var location = await (from playingArea in db.PlayingAreas
                join place in db.Venues on playingArea.VenueId equals place.Id
                where playingArea.Id == match.PlayingAreaId
                select new { Area = playingArea.Name, Venue = place.Name }).SingleOrDefaultAsync(ct);
            area = location?.Area;
            venue = location?.Venue;
        }
        return new(home, away, match.StartsAt, venue, area);
    }

    private async Task Add(NotificationType type, MatchNotificationPayload payload, string key,
        Guid[] recipients, Guid? tournamentId, Guid? teamId, Guid? matchId, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(x => x.DeduplicationKey == key, ct) ||
            db.Notifications.Local.Any(x => x.DeduplicationKey == key)) return;
        var notification = new Notification(type, JsonSerializer.Serialize(payload), key, tournamentId, teamId, matchId);
        db.Notifications.Add(notification);
        if (recipients.Length == 0) return;
        var preferences = await db.UserNotificationPreferences.Where(x => recipients.Contains(x.UserId))
            .ToDictionaryAsync(x => x.UserId, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var userId in recipients.Distinct())
        {
            var enabled = options.Value.Enabled && preferences.TryGetValue(userId, out var preference)
                && preference.WhatsAppEnabled && preference.WhatsAppPhoneNumber is not null;
            db.NotificationDeliveries.Add(new NotificationDelivery(notification.Id, userId, enabled, now));
        }
    }
}

public sealed record RenderedNotification(string Title, string Summary, string WhatsAppMessage);

public interface INotificationRenderer
{
    RenderedNotification Render(Notification notification);
}

public sealed class NotificationRenderer : INotificationRenderer
{
    public RenderedNotification Render(Notification notification)
    {
        var payload = JsonSerializer.Deserialize<MatchNotificationPayload>(notification.PayloadJson)
            ?? throw new InvalidOperationException("Notification payload is invalid.");
        var title = notification.Type switch
        {
            NotificationType.MatchScheduled => "Partida agendada",
            NotificationType.MatchRescheduled => "Partida reagendada",
            NotificationType.ScheduleRepaired => "Agenda reparada",
            NotificationType.ScoreDisputeOpened => "Contestação de placar aberta",
            NotificationType.ScoreDisputeResolved => "Contestação de placar resolvida",
            _ => "Notificação"
        };
        var match = $"{payload.HomeTeam} × {payload.AwayTeam}";
        var when = payload.StartsAt.HasValue ? payload.StartsAt.Value.ToString("dd/MM/yyyy 'às' HH:mm 'UTC'") : "horário não informado";
        var location = payload.Venue is null ? "local não informado" :
            payload.PlayingArea is null ? payload.Venue : $"{payload.Venue} / {payload.PlayingArea}";
        var summary = notification.Type == NotificationType.ScheduleRepaired
            ? $"{payload.ChangedMatches} partida(s) alterada(s)."
            : $"{match} — {when}";
        var message = notification.Type switch
        {
            NotificationType.MatchScheduled or NotificationType.MatchRescheduled =>
                $"Brakvyo\n\n{title}\n{match}\n\nData: {when}\nLocal: {location}",
            NotificationType.ScheduleRepaired =>
                $"Brakvyo\n\n{title}\n{payload.ChangedMatches} partida(s) da sua equipe tiveram o horário ou local alterado.",
            _ => $"Brakvyo\n\n{title}\n{match}"
        };
        return new(title, summary, message);
    }
}
