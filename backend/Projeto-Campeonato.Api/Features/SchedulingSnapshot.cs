using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;
namespace Projeto_Campeonato.Api.Features;

public sealed record SchedulingSnapshot(SolverInput Input, string Hash, int PreservedCount)
{
    public static async Task<SchedulingSnapshot?> Load(TournamentDbContext db, Guid id, AutopilotOptions options, CancellationToken ct)
    {
        var start = ScheduleTime.Parse(options.HorizonStart); var end = ScheduleTime.Parse(options.HorizonEnd);
        if (end <= start || end - start > TimeSpan.FromDays(31)) throw new ArgumentException("O horizonte deve ter entre um instante e 31 dias.");
        if (options.GranularityMinutes is < 1 or > 60) throw new ArgumentException("Granularidade deve estar entre 1 e 60 minutos.");
        if (options.PreferredRestMinutes is < 0 or > 240) throw new ArgumentException("Descanso preferido deve estar entre 0 e 240 minutos.");
        var t = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (t is null) return null;
        var matches = await db.Matches.AsNoTracking().Where(x => x.TournamentId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var teamIds = await db.Registrations.Where(x => x.TournamentId == id).Select(x => x.TeamId).ToArrayAsync(ct);
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(ct);
        var venues = await db.Venues.AsNoTracking().Where(x => x.TournamentId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var areas = await db.PlayingAreas.AsNoTracking().Where(x => x.TournamentId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var referees = await db.Referees.AsNoTracking().Where(x => x.TournamentId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var windows = await db.Availabilities.AsNoTracking().Where(x => x.TournamentId == id).OrderBy(x => x.Id).ToArrayAsync(ct);
        var fixedMatches = await db.Matches.AsNoTracking().Where(x => x.StartsAt != null && (x.TournamentId == id || teamIds.Contains(x.HomeTeamId) || teamIds.Contains(x.AwayTeamId))).OrderBy(x => x.Id).ToArrayAsync(ct);
        SolverWindow[] Windows(Func<Availability, bool> predicate) => windows.Where(predicate).Select(w => new SolverWindow(w.StartsAt, w.EndsAt)).ToArray();
        var input = new SolverInput(id, start, end, t.MatchDurationMinutes, options.GranularityMinutes, options.RequireReferee,
            matches.Where(m => m.StartsAt == null && m.Status != MatchStatus.Finished).Select(m => new SolverMatch(m.Id, m.HomeTeamId, m.AwayTeamId)).ToArray(),
            teams.Select(x => new SolverResource(x.Id, x.Name, Windows(w => w.TeamId == x.Id))).ToArray(),
            areas.Where(x => x.Active && venues.Any(v => v.Id == x.VenueId && v.Active)).Select(x => new SolverResource(x.Id, x.Name, Windows(w => w.PlayingAreaId == x.Id))).ToArray(),
            referees.Where(x => x.Active).Select(x => new SolverResource(x.Id, x.Name, Windows(w => w.RefereeId == x.Id))).ToArray(),
            fixedMatches.Select(m => new FixedSchedule(m.Id, m.HomeTeamId, m.AwayTeamId, m.StartsAt!.Value, m.EndsAt!.Value, m.PlayingAreaId!.Value, m.RefereeId, m.TournamentId == id)).ToArray(), options.PreferredRestMinutes);
        if (input.Matches.Length > 200 || areas.Length > 100 || referees.Length > 100 || windows.Length > 10000 || fixedMatches.Length > 20000)
            throw new ArgumentException("Limite do Autopilot v1: 200 partidas pendentes, 100 áreas/árbitros, 10000 disponibilidades e 20000 ocupações fixas.");
        if (input.Teams.Concat(input.PlayingAreas).Concat(input.Referees).Any(x => x.Availability.Length > 1000))
            throw new ArgumentException("Limite de 1000 intervalos por recurso no Autopilot v1.");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { t, matches, teams, venues, areas, referees, windows, fixedMatches, options })));
        return new(input, hash, matches.Count(m => m.StartsAt != null));
    }
}
