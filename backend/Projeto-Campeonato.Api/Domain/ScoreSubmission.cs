namespace Projeto_Campeonato.Api.Domain;

public sealed class ScoreSubmission
{
    private ScoreSubmission() { }
    public ScoreSubmission(Match match, Guid teamId, int homeScore, int awayScore)
    {
        if (teamId != match.HomeTeamId && teamId != match.AwayTeamId)
            throw new DomainConflictException("Only a participating team can submit a score.");
        MatchId = match.Id;
        SubmittedByTeamId = teamId;
        Update(homeScore, awayScore);
        UpdatedAt = null;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MatchId { get; private set; }
    public Guid SubmittedByTeamId { get; private set; }
    public int HomeScore { get; private set; }
    public int AwayScore { get; private set; }
    public DateTime SubmittedAt { get; private set; } = UtcNow();
    public DateTime? UpdatedAt { get; private set; }
    public void Update(int homeScore, int awayScore)
    {
        if (homeScore < 0 || awayScore < 0) throw new ArgumentException("Scores cannot be negative.");
        HomeScore = homeScore; AwayScore = awayScore; UpdatedAt = UtcNow();
    }
    // PostgreSQL timestamps store microseconds; keep HTTP and persisted values identical.
    private static DateTime UtcNow()
    {
        var now = DateTime.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}
