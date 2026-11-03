namespace Projeto_Campeonato.Api.Domain;

public sealed class AuditLog
{
    private AuditLog() { }
    public AuditLog(Guid matchId, string eventType, string data, Guid? teamId = null)
    { MatchId = matchId; EventType = eventType; Data = data; TeamId = teamId; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public Guid? TeamId { get; private set; }
    public string EventType { get; private set; } = "";
    public string Data { get; private set; } = "";
    public DateTime OccurredAt { get; private set; } = DateTime.UtcNow;
}
