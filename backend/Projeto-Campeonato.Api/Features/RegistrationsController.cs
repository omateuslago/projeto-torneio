using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

[ApiController]
[Route("api/tournaments/{tournamentId:guid}/registrations")]
public sealed class RegistrationsController(TournamentDbContext db, IAccessControl access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegistrationResponse>>> List(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        return Ok(await (from registration in db.Registrations.AsNoTracking()
                         join team in db.Teams on registration.TeamId equals team.Id
                         where registration.TournamentId == tournamentId
                         orderby registration.RegisteredAt, registration.Id
                         select new RegistrationResponse(registration.Id, tournamentId, team.Id, team.Name, registration.RegisteredAt))
                         .ToListAsync(ct));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<RegistrationResponse>> Create(Guid tournamentId, RegisterTeamRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, tournamentId, ct)) return Forbid();
        if (request.TeamId == Guid.Empty) return Problem(statusCode: 400, detail: "TeamId is required.");
        // PostgreSQL SSI prevents two requests from both taking the last available slot.
        // Conflicting transactions return 409; callers may retry after reloading the tournament.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var tournament = await db.Tournaments.SingleOrDefaultAsync(x => x.Id == tournamentId, ct);
        if (tournament is null) return NotFound();
        var team = await db.Teams.SingleOrDefaultAsync(x => x.Id == request.TeamId, ct);
        if (team is null) return NotFound();
        if (await db.Registrations.AnyAsync(x => x.TournamentId == tournamentId && x.TeamId == request.TeamId, ct))
            return Problem(statusCode: 409, detail: "Team is already registered.");
        tournament.EnsureCanRegister(await db.Registrations.CountAsync(x => x.TournamentId == tournamentId, ct));
        var registration = new Registration(tournamentId, request.TeamId);
        db.Registrations.Add(registration);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(List), new { tournamentId },
            new RegistrationResponse(registration.Id, tournamentId, team.Id, team.Name, registration.RegisteredAt));
    }
}
