namespace Projeto_Campeonato.Api.Domain;

public enum NotificationType
{
    MatchScheduled,
    MatchRescheduled,
    ScheduleRepaired,
    ScoreDisputeOpened,
    ScoreDisputeResolved
}

public enum NotificationChannel { WhatsApp }
public enum NotificationDeliveryStatus { Pending, Processing, Sent, Failed, Skipped }

public sealed class UserNotificationPreference
{
    private UserNotificationPreference() { }
    public UserNotificationPreference(Guid userId, DateTimeOffset now)
    {
        UserId = userId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid UserId { get; private set; }
    public string? WhatsAppPhoneNumber { get; private set; }
    public bool WhatsAppEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string? phoneNumber, bool enabled, DateTimeOffset now)
    {
        WhatsAppPhoneNumber = phoneNumber;
        WhatsAppEnabled = enabled;
        UpdatedAt = now;
    }
}

public sealed class Notification
{
    private Notification() { }
    public Notification(NotificationType type, string payloadJson, string deduplicationKey,
        Guid? tournamentId = null, Guid? teamId = null, Guid? matchId = null)
    {
        Id = Guid.NewGuid();
        Type = type;
        TournamentId = tournamentId;
        TeamId = teamId;
        MatchId = matchId;
        CreatedAt = DateTimeOffset.UtcNow;
        PayloadJson = payloadJson;
        DeduplicationKey = deduplicationKey;
    }

    public Guid Id { get; private set; }
    public NotificationType Type { get; private set; }
    public Guid? TournamentId { get; private set; }
    public Guid? TeamId { get; private set; }
    public Guid? MatchId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string PayloadJson { get; private set; } = "";
    public string DeduplicationKey { get; private set; } = "";
}

public sealed class NotificationDelivery
{
    private NotificationDelivery() { }
    public NotificationDelivery(Guid notificationId, Guid userId, bool pending, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        NotificationId = notificationId;
        UserId = userId;
        Channel = NotificationChannel.WhatsApp;
        Status = pending ? NotificationDeliveryStatus.Pending : NotificationDeliveryStatus.Skipped;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? LastError { get; private set; }

    public void Start(DateTimeOffset now)
    {
        Status = NotificationDeliveryStatus.Processing;
        AttemptCount++;
        LastAttemptAt = now;
    }

    public void Sent(string? providerMessageId, DateTimeOffset now)
    {
        Status = NotificationDeliveryStatus.Sent;
        SentAt = now;
        ProviderMessageId = providerMessageId;
        LastError = null;
    }

    public void Skip() => Status = NotificationDeliveryStatus.Skipped;

    public void Fail(string error, bool permanent, DateTimeOffset now, TimeSpan retryAfter)
    {
        LastError = error.Length <= 500 ? error : error[..500];
        if (permanent || AttemptCount >= 3)
        {
            Status = NotificationDeliveryStatus.Failed;
            FailedAt = now;
            return;
        }
        Status = NotificationDeliveryStatus.Pending;
        NextAttemptAt = now.Add(retryAfter);
    }
}
