using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

[ApiController]
[Route("api/tournaments/{id:guid}/schedule")]
public sealed class AutopilotController(TournamentDbContext db, ISchedulerClient scheduler, IDataProtectionProvider protection) : ControllerBase
{
    private readonly IDataProtector protector = protection.CreateProtector("TournamentAutopilot.v1");
    [HttpGet("/api/scheduler/health")]
    public async Task<IActionResult> Health(CancellationToken ct) => await scheduler.Healthy(ct) ? Ok(new { status = "healthy" }) : Problem(statusCode: 503, detail: "Scheduler indisponível; demais operações continuam disponíveis.");

    [HttpPost("generate")]
    public async Task<ActionResult<ScheduleProposal>> Generate(Guid id, AutopilotOptions options, CancellationToken ct)
    {
        SchedulingSnapshot? snapshot;
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct))
        { snapshot = await SchedulingSnapshot.Load(db, id, options, ct); await tx.CommitAsync(ct); }
        if (snapshot is null) return NotFound();
        SolverResult result;
        try { result = await scheduler.Generate(snapshot.Input, ct); }
        catch (SchedulerUnavailableException ex) { return Problem(statusCode: ex.Status, detail: ex.Message); }
        if (result is null || result.ScheduledMatches is null || result.Warnings is null || result.Warnings.Any(w => w is null) || result.Status is not ("optimal" or "feasible" or "infeasible" or "timeout" or "error"))
            return Problem(statusCode: 502, detail: "Resposta inválida do scheduler.");
        if (result.Status is not ("optimal" or "feasible"))
        {
            if (result.ScheduledMatches.Length != 0 || result.Fairness is not null || result.Resilience is not null) return Problem(statusCode: 502, detail: "Resposta inconsistente do scheduler.");
            return Ok(new ScheduleProposal(result.Status, [], result.Warnings, new(0, snapshot.PreservedCount, null, null, 0, 0), null, null));
        }
        if (result.Fairness is null || !result.Fairness.IsValid())
            return Problem(statusCode: 502, detail: "Métricas de fairness inválidas do scheduler.");
        var targets = snapshot.Input.Matches.Length + snapshot.Input.ExistingSchedule.Count(m => m.CountsForResilience && m.StartsAt >= snapshot.Input.HorizonStart && m.EndsAt <= snapshot.Input.HorizonEnd);
        var hasReferees = snapshot.Input.RequireReferee && snapshot.Input.Matches.Length > 0
            || snapshot.Input.ExistingSchedule.Any(m => m.CountsForResilience && m.StartsAt >= snapshot.Input.HorizonStart && m.EndsAt <= snapshot.Input.HorizonEnd && m.RefereeId != null);
        if (targets == 0 ? result.Resilience is not null : result.Resilience is null || !result.Resilience.IsValid() || (result.Resilience.RefereeScore != null) != hasReferees)
            return Problem(statusCode: 502, detail: "Análise de resiliência inválida do scheduler.");
        await using var validation = await db.Database.BeginTransactionAsync(ct);
        await LockTournament(id, ct);
        var current = (await SchedulingSnapshot.Load(db, id, options, ct))!;
        if (current.Hash != snapshot.Hash) return Problem(statusCode: 409, detail: "O campeonato mudou durante o cálculo. Gere uma nova proposta.");
        try { await Stage(current, result.ScheduledMatches, false, ct); }
        catch (Exception ex) when (ex is ArgumentException or DomainConflictException)
        { return Problem(statusCode: 502, detail: "O scheduler retornou uma proposta inválida: " + ex.Message); }
        // No SaveChanges: preview never writes matches or audit rows.
        var expiry = DateTime.UtcNow.AddMinutes(10);
        var token = result.ScheduledMatches.Length == 0 ? null : protector.Protect(JsonSerializer.Serialize(new ProtectedProposal(id, options, current.Hash, result.ScheduledMatches, expiry)));
        var starts = result.ScheduledMatches.Select(x => ScheduleTime.Parse(x.StartsAt)).ToArray();
        var metrics = new ScheduleMetrics(starts.Length, current.PreservedCount, starts.Length == 0 ? null : starts.Min(), starts.Length == 0 ? null : starts.Max().AddMinutes(current.Input.MatchDurationMinutes),
            result.ScheduledMatches.Select(x => x.PlayingAreaId).Distinct().Count(), result.ScheduledMatches.Where(x => x.RefereeId != null).Select(x => x.RefereeId).Distinct().Count());
        return Ok(new ScheduleProposal(result.Status, result.ScheduledMatches, result.Warnings, metrics, token, token is null ? null : expiry, result.Fairness, result.Resilience));
    }
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(Guid id, ApplyProposalRequest request, CancellationToken ct)
    {
        ProtectedProposal? proposal;
        try { proposal = JsonSerializer.Deserialize<ProtectedProposal>(protector.Unprotect(request.ProposalToken)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        { return Problem(statusCode: 400, detail: "Identidade da proposta inválida. Gere uma nova proposta."); }
        if (proposal is null || proposal.TournamentId != id) return Problem(statusCode: 400, detail: "Proposta não pertence ao campeonato.");
        if (proposal.ExpiresAt <= DateTime.UtcNow) return Problem(statusCode: 409, detail: "Proposta expirada. Gere uma nova proposta.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockTournament(id, ct);
        var snapshot = await SchedulingSnapshot.Load(db, id, proposal.Options, ct);
        if (snapshot is null) return NotFound();
        if (snapshot.Hash != proposal.SnapshotHash) return Problem(statusCode: 409, detail: "Disponibilidade, recursos ou partidas mudaram desde a geração. Gere uma nova proposta.");
        await Stage(snapshot, proposal.Assignments, true, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { appliedCount = proposal.Assignments.Length });
    }
    private async Task LockTournament(Guid id, CancellationToken ct)
    {
        await ScheduleWriter.Lock(db, ct);
        await db.Tournaments.FromSqlInterpolated($"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    }
    private async Task Stage(SchedulingSnapshot snapshot, SolverAssignment[] assignments, bool audit, CancellationToken ct)
    {
        var input = snapshot.Input;
        if (assignments.Any(x => x is null) || !assignments.Select(x => x.MatchId).Order().SequenceEqual(input.Matches.Select(x => x.Id).Order()))
            throw new DomainConflictException("A proposta deve conter todas as partidas pendentes, uma única vez.");
        var tournament = await db.Tournaments.SingleAsync(x => x.Id == input.TournamentId, ct);
        var writer = new ScheduleWriter(db);
        foreach (var row in assignments)
        {
            var start = ScheduleTime.Parse(row.StartsAt);
            if (start < input.HorizonStart || start.AddMinutes(input.MatchDurationMinutes) > input.HorizonEnd || (start - input.HorizonStart).Ticks % TimeSpan.FromMinutes(input.GranularityMinutes).Ticks != 0)
                throw new DomainConflictException("Horário fora do horizonte ou da grade solicitada.");
            if (input.RequireReferee && row.RefereeId is null || !input.RequireReferee && row.RefereeId is not null)
                throw new DomainConflictException("A proposta não respeita a escolha de árbitros.");
            var match = await db.Matches.SingleAsync(x => x.Id == row.MatchId && x.TournamentId == input.TournamentId, ct);
            if (match.StartsAt is not null) throw new DomainConflictException("Uma partida já agendada não pode ser movida pelo Autopilot.");
            await writer.Stage(match, tournament, new(row.StartsAt, row.PlayingAreaId, row.RefereeId), audit, ct);
        }
    }
}
