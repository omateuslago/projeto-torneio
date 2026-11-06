namespace Projeto_Campeonato.Api.Domain;

public enum MatchStatus { Pending, Finished, AwaitingConfirmation, Disputed }

public sealed class Match
{
    private Match() { }
    public Match(Guid tournamentId, Guid homeTeamId, Guid awayTeamId)
    {
        if (tournamentId == Guid.Empty || homeTeamId == Guid.Empty || awayTeamId == Guid.Empty)
            throw new ArgumentException("Tournament and teams are required.");
        // Canonical orientation makes the unordered pair unique, including in PostgreSQL.
        if (homeTeamId.CompareTo(awayTeamId) >= 0)
            throw new ArgumentException("Teams must be distinct and in canonical order.");
        TournamentId = tournamentId;
        HomeTeamId = homeTeamId;
        AwayTeamId = awayTeamId;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public Guid HomeTeamId { get; private set; }
    public Guid AwayTeamId { get; private set; }
    public MatchStatus Status { get; private set; } = MatchStatus.Pending;
    public int? HomeScore { get; private set; }
    public int? AwayScore { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; private set; }
    public string? ResultSource { get; private set; }
    public DateTime? StartsAt { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public Guid? PlayingAreaId { get; private set; }
    public Guid? RefereeId { get; private set; }
    public void Schedule(DateTime start, DateTime end, Guid areaId, Guid? refereeId)
    {
        if (Status == MatchStatus.Finished) throw new DomainConflictException("Partida finalizada não pode ser reagendada.");
        ScheduleTime.Validate(start, end);
        StartsAt = start; EndsAt = end; PlayingAreaId = areaId; RefereeId = refereeId;
    }

    public void AwaitConfirmation(bool disputed)
    {
        if (Status == MatchStatus.Finished) throw new DomainConflictException("Official result is already final.");
        Status = disputed ? MatchStatus.Disputed : MatchStatus.AwaitingConfirmation;
    }

    public void RecordResult(int homeScore, int awayScore, string source = "Manual")
    {
        if (homeScore < 0 || awayScore < 0) throw new ArgumentException("Scores cannot be negative.");
        HomeScore = homeScore;
        AwayScore = awayScore;
        ResultSource = source;
        Status = MatchStatus.Finished;
        // A correction replaces the result; this timestamp represents the latest recording.
        FinishedAt = DateTime.UtcNow;
    }
}

public static class RoundRobin
{
    public static IReadOnlyList<Match> Generate(Guid tournamentId, IEnumerable<Guid> teamIds)
    {
        var teams = teamIds.Order().ToArray();
        if (teams.Length < 2) throw new DomainConflictException("At least two teams are required.");
        if (teams.Distinct().Count() != teams.Length) throw new ArgumentException("Teams must be unique.");
        var matches = new List<Match>();
        for (var home = 0; home < teams.Length; home++)
            for (var away = home + 1; away < teams.Length; away++)
                matches.Add(new Match(tournamentId, teams[home], teams[away]));
        return matches;
    }
}
