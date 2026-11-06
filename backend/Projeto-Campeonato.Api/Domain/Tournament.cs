namespace Projeto_Campeonato.Api.Domain;

public enum TournamentFormat { SingleElimination, RoundRobin }
public enum TournamentStatus { Draft, Open, Closed, InProgress, Finished }

public sealed class Tournament
{
    private Tournament() { }

    public Tournament(string name, string sport, int maxTeams, TournamentFormat format)
    {
        Name = DomainRules.Name(name, 120, nameof(name));
        Sport = DomainRules.Name(sport, 80, nameof(sport));
        if (maxTeams is < 2 or > 128) throw new ArgumentException("Capacity must be between 2 and 128.");
        if (!Enum.IsDefined(format)) throw new ArgumentException("Unsupported format.");
        MaxTeams = maxTeams;
        Format = format;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = "";
    public string Sport { get; private set; } = "";
    public int MaxTeams { get; private set; }
    public TournamentFormat Format { get; private set; }
    public TournamentStatus Status { get; private set; } = TournamentStatus.Draft;
    public int MatchDurationMinutes { get; private set; } = 60;
    public string TimeZoneId { get; private set; } = "UTC";
    public void ConfigureSchedule(int minutes, string zone)
    {
        if (minutes is < 5 or > 480) throw new ArgumentException("A duração deve estar entre 5 e 480 minutos.");
        if (string.IsNullOrWhiteSpace(zone) || zone.Length > 100 || (zone != "UTC" && !zone.Contains('/')))
            throw new ArgumentException("Informe um fuso IANA válido, como America/Sao_Paulo.");
        try { TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Fuso horário desconhecido."); }
        MatchDurationMinutes = minutes; TimeZoneId = zone;
    }

    public void ChangeStatus(TournamentStatus next)
    {
        if (next == Status) return;
        if (!((Status == TournamentStatus.Draft && next == TournamentStatus.Open) ||
              (Status == TournamentStatus.Open && next == TournamentStatus.Closed)))
            throw new DomainConflictException("Allowed transitions: Draft → Open → Closed.");
        Status = next;
    }

    public void StartCompetition()
    {
        if (Format != TournamentFormat.RoundRobin) throw new DomainConflictException("Only RoundRobin is supported for match generation.");
        if (Status != TournamentStatus.Closed) throw new DomainConflictException("Close registration before generating matches.");
        Status = TournamentStatus.InProgress;
    }

    public void FinishCompetition()
    {
        if (Status is not (TournamentStatus.InProgress or TournamentStatus.Finished))
            throw new DomainConflictException("Competition has not started.");
        Status = TournamentStatus.Finished;
    }

    public void EnsureCanRegister(int registeredTeams)
    {
        if (Status != TournamentStatus.Open) throw new DomainConflictException("Registration is not open.");
        if (registeredTeams >= MaxTeams) throw new DomainConflictException("Tournament is full.");
    }
}

internal static class DomainRules
{
    public static string Name(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new ArgumentException($"{field} is required and must have at most {maxLength} characters.");
        return value.Trim();
    }
}
