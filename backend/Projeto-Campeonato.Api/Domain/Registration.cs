namespace Projeto_Campeonato.Api.Domain;

public sealed class Registration
{
    private Registration() { }
    public Registration(Guid tournamentId, Guid teamId)
    {
        if (tournamentId == Guid.Empty || teamId == Guid.Empty)
            throw new ArgumentException("Tournament and team are required.");
        TournamentId = tournamentId;
        TeamId = teamId;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public Guid TeamId { get; private set; }
    public DateTime RegisteredAt { get; private set; } = DateTime.UtcNow;
}
