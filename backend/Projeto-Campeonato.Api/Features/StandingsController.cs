using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

[ApiController]
[Route("api/tournaments/{tournamentId:guid}/standings")]
public sealed class StandingsController(TournamentDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StandingRow>>> Get(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tournamentId, ct);
        if (tournament is null) return NotFound();
        if (tournament.Format != TournamentFormat.RoundRobin)
            return Problem(statusCode: 409, detail: "Standings are only available for RoundRobin.");
        var teams = await (from registration in db.Registrations
                           join team in db.Teams on registration.TeamId equals team.Id
                           where registration.TournamentId == tournamentId
                           select team).AsNoTracking().ToListAsync(ct);
        var matches = await db.Matches.AsNoTracking().Where(x => x.TournamentId == tournamentId).ToListAsync(ct);
        return Ok(Standings.Calculate(teams, matches));
    }
}
