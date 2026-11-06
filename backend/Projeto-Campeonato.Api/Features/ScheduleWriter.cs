using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;
namespace Projeto_Campeonato.Api.Features;

// Caller owns transaction and scheduling lock. Preview stages changes without SaveChanges.
public sealed class ScheduleWriter(TournamentDbContext db)
{
    public static Task<int> Lock(TournamentDbContext db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(771905)", ct);
    private IQueryable<Availability> Windows(Guid tournament, string kind, Guid resource) => db.Availabilities.Where(x => x.TournamentId == tournament &&
        (kind == "teams" && x.TeamId == resource || kind == "playing-areas" && x.PlayingAreaId == resource || kind == "referees" && x.RefereeId == resource));
    public async Task Stage(Match m, Tournament t, ScheduleRequest request, bool audit, CancellationToken ct)
    {
        var start = ScheduleTime.Parse(request.StartsAt);
        var end = start.AddMinutes(t.MatchDurationMinutes);
        var area = await db.PlayingAreas.SingleOrDefaultAsync(x => x.Id == request.PlayingAreaId && x.TournamentId == t.Id && x.Active, ct);
        if (area is null || !await db.Venues.AnyAsync(x => x.Id == area.VenueId && x.Active, ct)) throw new DomainConflictException("Área inválida ou inativa para este campeonato.");
        var referee = request.RefereeId is null ? null : await db.Referees.SingleOrDefaultAsync(x => x.Id == request.RefereeId && x.TournamentId == t.Id && x.Active, ct);
        if (request.RefereeId.HasValue && referee is null) throw new DomainConflictException("Árbitro inválido ou inativo para este campeonato.");
        var home = await db.Teams.SingleAsync(x => x.Id == m.HomeTeamId, ct); var away = await db.Teams.SingleAsync(x => x.Id == m.AwayTeamId, ct);
        var targets = new List<(string Kind, Guid Id, string Name)> { ("teams", home.Id, home.Name), ("teams", away.Id, away.Name), ("playing-areas", area.Id, area.Name) };
        if (referee is not null) targets.Add(("referees", referee.Id, referee.Name));
        var problems = new List<string>();
        foreach (var target in targets)
            if (!await Windows(t.Id, target.Kind, target.Id).AnyAsync(w => w.StartsAt <= start && w.EndsAt >= end, ct))
                problems.Add($"{target.Name} não possui disponibilidade para todo o período solicitado.");
        var candidates = await db.Matches.Where(x => x.Id != m.Id && x.StartsAt != null &&
            (x.HomeTeamId == home.Id || x.AwayTeamId == home.Id || x.HomeTeamId == away.Id || x.AwayTeamId == away.Id || x.PlayingAreaId == area.Id || (request.RefereeId != null && x.RefereeId == request.RefereeId))).ToListAsync(ct);
        candidates.AddRange(db.Matches.Local.Where(x => x.Id != m.Id && x.StartsAt != null && candidates.All(c => c.Id != x.Id)));
        foreach (var other in candidates.Where(x => ScheduleTime.Overlaps(start, end, x.StartsAt!.Value, x.EndsAt!.Value)))
        {
            foreach (var target in targets)
                if (target.Kind == "teams" && (other.HomeTeamId == target.Id || other.AwayTeamId == target.Id) || target.Kind == "playing-areas" && other.PlayingAreaId == target.Id || target.Kind == "referees" && other.RefereeId == target.Id)
                    problems.Add($"{target.Name} já possui partida entre {other.StartsAt:O} e {other.EndsAt:O}.");
        }
        if (problems.Count > 0) throw new DomainConflictException(string.Join(" ", problems.Distinct()));
        var previous = new { m.StartsAt, m.EndsAt, m.PlayingAreaId, m.RefereeId }; var rescheduled = m.StartsAt.HasValue;
        m.Schedule(start, end, area.Id, referee?.Id);
        if (audit) new ResultWorkflow(db).Audit(m, rescheduled ? "MatchRescheduled" : "MatchScheduled", new { previous, m.StartsAt, m.EndsAt, m.PlayingAreaId, m.RefereeId });
    }
}
