using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
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
        return tournament is null ? NotFound() : Ok(ForUser(tournament));
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<TournamentResponse>> Create(CreateTournamentRequest request, CancellationToken ct)
    {
        var userId = access.UserId(User);
        var tournament = new Tournament(request.Name, request.Sport, request.MaxTeams, request.Format, userId);
        db.Tournaments.Add(tournament);
        db.TournamentOrganizers.Add(new TournamentOrganizer(userId, tournament.Id));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = tournament.Id }, ForUser(tournament));
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
        return Ok(ForUser(tournament));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tournament = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (tournament is null) return NotFound();
        if (!CanDelete(tournament)) return Forbid();

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await ScheduleWriter.Lock(db, ct);
            tournament = await db.Tournaments.FromSqlInterpolated(
                $"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (tournament is null) return NotFound();
            if (!CanDelete(tournament)) return Forbid();

            var matches = db.Matches.Where(x => x.TournamentId == id);
            var matchIds = matches.Select(x => x.Id);
            await db.Notifications.Where(x => x.TournamentId == id ||
                (x.MatchId.HasValue && matchIds.Contains(x.MatchId.Value))).ExecuteDeleteAsync(ct);
            await db.AuditLogs.Where(x => matchIds.Contains(x.MatchId)).ExecuteDeleteAsync(ct);
            await db.ScoreSubmissions.Where(x => matchIds.Contains(x.MatchId)).ExecuteDeleteAsync(ct);
            await db.Disputes.Where(x => matchIds.Contains(x.MatchId)).ExecuteDeleteAsync(ct);

            // Both columns must be cleared together before deleting the self-referencing nodes.
            var nodes = db.BracketNodes.Where(x => x.TournamentId == id);
            await nodes.Where(x => x.NextNodeId != null).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.NextNodeId, x => null)
                .SetProperty(x => x.NextSlot, x => null), ct);
            await nodes.ExecuteDeleteAsync(ct);
            await matches.ExecuteDeleteAsync(ct);
            await db.Availabilities.Where(x => x.TournamentId == id).ExecuteDeleteAsync(ct);
            await db.PlayingAreas.Where(x => x.TournamentId == id).ExecuteDeleteAsync(ct);
            await db.Venues.Where(x => x.TournamentId == id).ExecuteDeleteAsync(ct);
            await db.Referees.Where(x => x.TournamentId == id).ExecuteDeleteAsync(ct);
            await db.Registrations.Where(x => x.TournamentId == id).ExecuteDeleteAsync(ct);
            // Organizer memberships and invitations cascade from the tournament.
            await db.Tournaments.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return NoContent();
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.ForeignKeyViolation
            or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            return Problem(statusCode: 409, detail: "Não foi possível concluir a exclusão. Tente novamente.");
        }
    }

    private bool CanDelete(Tournament tournament) => tournament.OwnerUserId.HasValue &&
        User.Identity?.IsAuthenticated == true && tournament.OwnerUserId == access.UserId(User);

    private TournamentResponse ForUser(Tournament tournament) =>
        TournamentResponse.From(tournament) with { CanDelete = CanDelete(tournament) };
}
