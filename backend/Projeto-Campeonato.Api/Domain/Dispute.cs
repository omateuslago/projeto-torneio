namespace Projeto_Campeonato.Api.Domain;

public enum DisputeStatus { Open, Resolved }
public sealed class Dispute
{
    private Dispute() { }
    public Dispute(Guid matchId) { MatchId = matchId; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public DisputeStatus Status { get; private set; } = DisputeStatus.Open;
    public DateTime OpenedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; private set; }
    public string? ResolutionNote { get; private set; }
    public void Resolve(string note)
    {
        if (Status != DisputeStatus.Open) throw new DomainConflictException("Dispute is already resolved.");
        ResolutionNote = DomainRules.Name(note, 2000, nameof(note));
        Status = DisputeStatus.Resolved; ResolvedAt = DateTime.UtcNow;
    }
}
