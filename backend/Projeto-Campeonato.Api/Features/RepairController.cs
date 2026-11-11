using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;
namespace Projeto_Campeonato.Api.Features;

public sealed record RepairIncident(string Kind, Guid? ResourceId = null, string? StartsAt = null, string? EndsAt = null, Guid? MatchId = null, int? DelayMinutes = null);
public sealed record RepairOptions([Required] AutopilotOptions Horizon, [Required] RepairIncident Incident);
public sealed record RepairInput(SolverInput Schedule, RepairIncident Incident);
public sealed record RepairChange(SolverAssignment Before, SolverAssignment After);
public sealed record RepairMetrics(int ImpactedCount, int ChangedCount, int PreservedCount, double TotalDisplacementMinutes, double MaxDisplacementMinutes, int AreaChanges, int RefereeChanges);
public sealed record RepairProposal(string Status, RepairIncident Incident, RepairChange[] Changes, RepairMetrics Metrics, string[] Warnings, ScheduleFairness? Fairness, ScheduleResilience? Resilience, string? ProposalToken, DateTime? ExpiresAt);
public sealed record ProtectedRepair(Guid TournamentId, RepairOptions Options, string Hash, SolverAssignment[] Assignments, DateTime ExpiresAt);

[ApiController]
[Route("api/tournaments/{id:guid}/schedule/repair")]
public sealed class RepairController(TournamentDbContext db, ISchedulerClient scheduler, IDataProtectionProvider protection, IAccessControl access) : ControllerBase
{
    private readonly IDataProtector protector = protection.CreateProtector("ScheduleRepair.v1");
    private static FixedSchedule[] Targets(SolverInput input, RepairIncident incident) => input.ExistingSchedule.Where(m => m.CountsForResilience && m.StartsAt >= input.HorizonStart && m.EndsAt <= input.HorizonEnd && m.MatchId != incident.MatchId).ToArray();
    private static SolverAssignment Row(FixedSchedule m) => new(m.MatchId, m.StartsAt.ToString("O"), m.PlayingAreaId, m.RefereeId);
    private static bool Same(SolverAssignment a, SolverAssignment b) => a.MatchId == b.MatchId && ScheduleTime.Parse(a.StartsAt) == ScheduleTime.Parse(b.StartsAt) && a.PlayingAreaId == b.PlayingAreaId && a.RefereeId == b.RefereeId;
    private static bool Conflict(FixedSchedule a, FixedSchedule b) => a.MatchId != b.MatchId && ScheduleTime.Overlaps(a.StartsAt, a.EndsAt, b.StartsAt, b.EndsAt) && (a.HomeTeamId == b.HomeTeamId || a.HomeTeamId == b.AwayTeamId || a.AwayTeamId == b.HomeTeamId || a.AwayTeamId == b.AwayTeamId || a.PlayingAreaId == b.PlayingAreaId || a.RefereeId != null && a.RefereeId == b.RefereeId);
    private static FixedSchedule? ValidateIncident(SolverInput input, RepairIncident i)
    {
        if (i.Kind == "matchDelayed")
        {
            if (i.DelayMinutes is null or < 1 or > 240 || i.MatchId is null || i.ResourceId != null || i.StartsAt != null || i.EndsAt != null) throw new ArgumentException("Informe partida e atraso entre 1 e 240 minutos.");
            var m = input.ExistingSchedule.SingleOrDefault(x => x.MatchId == i.MatchId && x.CountsForResilience);
            if (m is null) throw new ArgumentException("Partida incidente inválida ou finalizada.");
            return m with { EndsAt = m.EndsAt.AddMinutes(i.DelayMinutes.Value) };
        }
        if (i.Kind is not ("playingAreaUnavailable" or "refereeUnavailable" or "teamUnavailable") || i.ResourceId is null || i.MatchId != null || i.DelayMinutes != null) throw new ArgumentException("Incidente inválido.");
        var start = ScheduleTime.Parse(i.StartsAt!); var end = ScheduleTime.Parse(i.EndsAt!);
        if (start >= end || end-start > TimeSpan.FromDays(31)) throw new ArgumentException("Intervalo do incidente inválido.");
        var resources = i.Kind == "playingAreaUnavailable" ? input.PlayingAreas : i.Kind == "refereeUnavailable" ? input.Referees : input.Teams;
        if (!resources.Any(x => x.Id == i.ResourceId)) throw new ArgumentException("Recurso não pertence ao campeonato ou está inativo.");
        return null;
    }
    private static bool Affected(FixedSchedule m, RepairIncident i, FixedSchedule? delayed) => delayed != null ? Conflict(m, delayed) : ScheduleTime.Overlaps(m.StartsAt, m.EndsAt, ScheduleTime.Parse(i.StartsAt!), ScheduleTime.Parse(i.EndsAt!)) && (i.Kind == "playingAreaUnavailable" && m.PlayingAreaId == i.ResourceId || i.Kind == "refereeUnavailable" && m.RefereeId == i.ResourceId || i.Kind == "teamUnavailable" && (m.HomeTeamId == i.ResourceId || m.AwayTeamId == i.ResourceId));
    private async Task Lock(Guid id, CancellationToken ct)
    {
        await ScheduleWriter.Lock(db, ct);
        await db.Tournaments.FromSqlInterpolated($"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
    }
    [HttpPost("generate")]
    [Authorize]
    public async Task<ActionResult<RepairProposal>> Generate(Guid id, RepairOptions options, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        SchedulingSnapshot? snapshot;
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct))
        { snapshot = await SchedulingSnapshot.Load(db, id, options.Horizon, ct); await tx.CommitAsync(ct); }
        if (snapshot is null) return NotFound();
        var input = snapshot.Input; var delayed = ValidateIncident(input, options.Incident);
        var targets = Targets(input, options.Incident);
        if (targets.Length > 200) throw new ArgumentException("Limite de 200 partidas para reparo.");
        var impacted = input.ExistingSchedule.Count(m => Affected(m, options.Incident, delayed));
        SolverResult result;
        try { result = await scheduler.Repair(new(input, options.Incident), ct); }
        catch (SchedulerUnavailableException ex) { return Problem(statusCode: ex.Status, detail: ex.Message); }
        if (result is null || result.ScheduledMatches is null || result.Warnings is null || result.Warnings.Any(x => x is null) || result.Status is not ("optimal" or "feasible" or "infeasible" or "timeout" or "error")) return Problem(statusCode: 502, detail: "Resposta de reparo inválida.");
        var emptyMetrics = new RepairMetrics(impacted, 0, snapshot.PreservedCount, 0, 0, 0, 0);
        if (result.Status is not ("optimal" or "feasible"))
        {
            if (result.ScheduledMatches.Length != 0 || result.Fairness != null || result.Resilience != null) return Problem(statusCode: 502, detail: "Resposta inconsistente.");
            return Ok(new RepairProposal(result.Status, options.Incident, [], emptyMetrics, result.Warnings, null, null, null, null));
        }
        await using var validation = await db.Database.BeginTransactionAsync(ct); await Lock(id, ct);
        var current = await SchedulingSnapshot.Load(db, id, options.Horizon, ct);
        if (current is null || current.Hash != snapshot.Hash) return Problem(statusCode: 409, detail: "Agenda mudou. Gere uma nova proposta.");
        if (impacted == 0)
        {
            if (result.ScheduledMatches.Length != 0 && (result.ScheduledMatches.Length != targets.Length || result.ScheduledMatches.Any(a => a is null || !targets.Any(m => Same(Row(m), a))) || result.ScheduledMatches.Select(a => a.MatchId).Distinct().Count() != result.ScheduledMatches.Length)) return Problem(statusCode: 502, detail: "Incidente sem impacto não deve alterar a agenda.");
            return Ok(new RepairProposal("optimal", options.Incident, [], emptyMetrics, ["Nenhuma alteração necessária."], null, null, null, null));
        }
        RepairChange[] changes;
        try { changes = await Stage(current, options.Incident, result.ScheduledMatches, false, ct); }
        catch (Exception ex) when (ex is ArgumentException or DomainConflictException) { return Problem(statusCode: 502, detail: "Reparo inválido: " + ex.Message); }
        if (result.Fairness is null || !result.Fairness.IsValid() || result.Resilience is null || !result.Resilience.IsValid()) return Problem(statusCode: 502, detail: "Diagnósticos de reparo inválidos.");
        var displacement = changes.Select(c => Math.Abs((ScheduleTime.Parse(c.After.StartsAt)-ScheduleTime.Parse(c.Before.StartsAt)).TotalMinutes)).ToArray();
        var metrics = new RepairMetrics(impacted, changes.Length, snapshot.PreservedCount-changes.Length, displacement.Sum(), displacement.DefaultIfEmpty().Max(), changes.Count(c => c.Before.PlayingAreaId != c.After.PlayingAreaId), changes.Count(c => c.Before.RefereeId != c.After.RefereeId));
        var expiry = DateTime.UtcNow.AddMinutes(10);
        var token = changes.Length == 0 ? null : protector.Protect(JsonSerializer.Serialize(new ProtectedRepair(id, options, current.Hash, result.ScheduledMatches, expiry)));
        return Ok(new RepairProposal(result.Status, options.Incident, changes, metrics, result.Warnings, result.Fairness, result.Resilience, token, token == null ? null : expiry));
    }
    [HttpPost("apply")]
    [Authorize]
    public async Task<IActionResult> Apply(Guid id, ApplyProposalRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        ProtectedRepair? p;
        try { p = JsonSerializer.Deserialize<ProtectedRepair>(protector.Unprotect(request.ProposalToken)); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException) { return Problem(statusCode: 400, detail: "Token de reparo inválido."); }
        if (p is null || p.TournamentId != id) return Problem(statusCode: 400, detail: "Proposta pertence a outro campeonato.");
        if (p.ExpiresAt <= DateTime.UtcNow) return Problem(statusCode: 409, detail: "Proposta expirada.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(id, ct);
        var snapshot = await SchedulingSnapshot.Load(db, id, p.Options.Horizon, ct);
        if (snapshot is null || snapshot.Hash != p.Hash) return Problem(statusCode: 409, detail: "Agenda mudou. Gere uma nova proposta.");
        var changes = await Stage(snapshot, p.Options.Incident, p.Assignments, true, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { appliedCount = changes.Length });
    }
    private async Task<RepairChange[]> Stage(SchedulingSnapshot snapshot, RepairIncident incident, SolverAssignment[] assignments, bool audit, CancellationToken ct)
    {
        var input = snapshot.Input; var delayed = ValidateIncident(input, incident); var targets = Targets(input, incident);
        if (assignments.Any(a => a is null) || !assignments.Select(a => a.MatchId).Order().SequenceEqual(targets.Select(t => t.MatchId).Order())) throw new DomainConflictException("Reparo deve conter exatamente as partidas candidatas.");
        var final = input.ExistingSchedule.ToDictionary(m => m.MatchId);
        if (delayed != null) final[delayed.MatchId] = delayed;
        var changes = new List<RepairChange>();
        foreach (var row in assignments)
        {
            var old = targets.Single(m => m.MatchId == row.MatchId); var start = ScheduleTime.Parse(row.StartsAt);
            if (old.EndsAt-old.StartsAt != TimeSpan.FromMinutes(input.MatchDurationMinutes) || start < input.HorizonStart || start.AddMinutes(input.MatchDurationMinutes) > input.HorizonEnd || (start-input.HorizonStart).Ticks % TimeSpan.FromMinutes(input.GranularityMinutes).Ticks != 0 || (old.RefereeId != null) != (row.RefereeId != null)) throw new DomainConflictException("Reparo fora do horizonte/grade ou alterando modo de arbitragem.");
            final[row.MatchId] = old with { StartsAt = start, EndsAt = start.AddMinutes(input.MatchDurationMinutes), PlayingAreaId = row.PlayingAreaId, RefereeId = row.RefereeId };
            if (!Same(Row(old), row)) changes.Add(new(Row(old), row));
        }
        var all = final.Values.ToArray();
        foreach (var m in all)
        {
            if (Affected(m, incident, delayed)) throw new DomainConflictException("Proposta não respeita o incidente.");
            if (targets.Any(t => t.MatchId == m.MatchId) && all.Any(other => Conflict(m, other))) throw new DomainConflictException("Colisão na agenda reparada.");
        }
        var tournament = await db.Tournaments.SingleAsync(t => t.Id == input.TournamentId, ct);
        var changedIds = changes.Select(c => c.After.MatchId).ToArray();
        foreach (var row in assignments)
        {
            var match = await db.Matches.SingleAsync(m => m.Id == row.MatchId && m.TournamentId == input.TournamentId, ct);
            await new ScheduleWriter(db).Stage(match, tournament, new(row.StartsAt, row.PlayingAreaId, row.RefereeId), audit && changedIds.Contains(row.MatchId), ct, changedIds);
        }
        return changes.ToArray();
    }
}
