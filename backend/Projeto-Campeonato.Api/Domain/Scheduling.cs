using System.Globalization;
using System.Text.RegularExpressions;

namespace Projeto_Campeonato.Api.Domain;

public static class ScheduleTime
{
    public static DateTime Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"T.*(Z|[+-]\d{2}:\d{2})$") ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            throw new ArgumentException("Informe data/hora ISO 8601 com offset explícito ou Z.");
        return instant.UtcDateTime;
    }
    public static bool Overlaps(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd) => start < otherEnd && otherStart < end;
    public static bool Contains(DateTime start, DateTime end, DateTime innerStart, DateTime innerEnd) => start <= innerStart && innerEnd <= end;
    public static void Validate(DateTime start, DateTime end)
    {
        if (start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || start >= end)
            throw new ArgumentException("O início deve ser anterior ao fim, com instantes UTC válidos.");
    }
}

public sealed class Venue
{
    private Venue() { }
    public Venue(Guid tournamentId, string name, string address) { TournamentId = tournamentId; Name = DomainRules.Name(name, 120, nameof(name)); Address = DomainRules.Name(address, 300, nameof(address)); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public bool Active { get; private set; } = true;
}
public sealed class PlayingArea
{
    private PlayingArea() { }
    public PlayingArea(Guid tournamentId, Guid venueId, string name) { TournamentId = tournamentId; VenueId = venueId; Name = DomainRules.Name(name, 120, nameof(name)); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = "";
    public bool Active { get; private set; } = true;
}
public sealed class Referee
{
    private Referee() { }
    public Referee(Guid tournamentId, string name) { TournamentId = tournamentId; Name = DomainRules.Name(name, 120, nameof(name)); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public string Name { get; private set; } = "";
    public bool Active { get; private set; } = true;
}
public sealed class Availability
{
    private Availability() { }
    public Availability(Guid tournamentId, string kind, Guid resourceId, DateTime start, DateTime end)
    {
        TournamentId = tournamentId;
        switch (kind) { case "teams": TeamId = resourceId; break; case "playing-areas": PlayingAreaId = resourceId; break; case "referees": RefereeId = resourceId; break; default: throw new ArgumentException("Tipo de recurso inválido."); }
        Update(start, end);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public Guid? TeamId { get; private set; }
    public Guid? PlayingAreaId { get; private set; }
    public Guid? RefereeId { get; private set; }
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public void Update(DateTime start, DateTime end) { ScheduleTime.Validate(start, end); StartsAt = start; EndsAt = end; }
}
