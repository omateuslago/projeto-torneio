using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

[ApiController]
[Route("api/tournaments")]
public sealed class TournamentsController(TournamentDbContext db, IAccessControl access) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TournamentResponse>>> List(CancellationToken ct) =>
        Ok((await db.Tournaments.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct))
            .Select(TournamentResponse.From));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TournamentResponse>> Get(Guid id, CancellationToken ct)
    {
        var tournament = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return tournament is null ? NotFound() : Ok(TournamentResponse.From(tournament));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<TournamentResponse>> Create(CreateTournamentRequest request, CancellationToken ct)
    {
        var tournament = new Tournament(request.Name, request.Sport, request.MaxTeams, request.Format);
        db.Tournaments.Add(tournament);
        db.TournamentOrganizers.Add(new TournamentOrganizer(access.UserId(User), tournament.Id));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, TournamentResponse.From(tournament));
    }

    [HttpPut("{id:guid}/status")]
    [Authorize]
    public async Task<ActionResult<TournamentResponse>> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        // Serializable also protects concurrent status changes and registration requests.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var tournament = await db.Tournaments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (tournament is null) return NotFound();
        tournament.ChangeStatus(request.Status);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(TournamentResponse.From(tournament));
    }
}
