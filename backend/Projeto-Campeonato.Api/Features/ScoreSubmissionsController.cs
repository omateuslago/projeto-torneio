using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record SubmitScoreRequest([Required, Range(0, int.MaxValue)] int? HomeScore, [Required, Range(0, int.MaxValue)] int? AwayScore);
public sealed record ResolveDisputeRequest([Required, Range(0, int.MaxValue)] int? HomeScore, [Required, Range(0, int.MaxValue)] int? AwayScore, [Required, StringLength(2000)] string ResolutionNote);
public sealed record SubmissionResponse(Guid Id, Guid MatchId, Guid SubmittedByTeamId, int HomeScore, int AwayScore, DateTime SubmittedAt, DateTime? UpdatedAt);
public sealed record DisputeResponse(Guid Id, Guid MatchId, DisputeStatus Status, DateTime OpenedAt, DateTime? ResolvedAt, string? ResolutionNote);
public sealed record AuditResponse(Guid Id, Guid MatchId, Guid? TeamId, string EventType, DateTime OccurredAt, string Data);

[ApiController]
public sealed class ScoreSubmissionsController(TournamentDbContext db, IAccessControl access) : ControllerBase
{
    [HttpPut("api/matches/{matchId:guid}/score-submissions/{teamId:guid}")]
    [Authorize]
    public async Task<ActionResult<SubmissionResponse>> Submit(Guid matchId, Guid teamId, SubmitScoreRequest request, CancellationToken ct)
    {
        var tournamentId = await db.Matches.Where(x => x.Id == matchId).Select(x => (Guid?)x.TournamentId).SingleOrDefaultAsync(ct);
        if (tournamentId is null) return NotFound();
        if (!await access.CanManageTeam(User, teamId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tournament = await db.Tournaments.FromSqlInterpolated($"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {tournamentId.Value} FOR UPDATE").SingleAsync(ct);
        var match = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        if (teamId != match.HomeTeamId && teamId != match.AwayTeamId) return Problem(statusCode: 403, detail: "Only a participating team can submit a score.");
        if (match.Status == MatchStatus.Finished) return Problem(statusCode: 409, detail: "Official result is already final.");
        var workflow = new ResultWorkflow(db);
        var submissions = await db.ScoreSubmissions.Where(x => x.MatchId == matchId).ToListAsync(ct);
        var submission = submissions.SingleOrDefault(x => x.SubmittedByTeamId == teamId);
        var previous = submission is null ? null : new { submission.HomeScore, submission.AwayScore };
        if (submission is null)
        {
            submission = new ScoreSubmission(match, teamId, request.HomeScore!.Value, request.AwayScore!.Value);
            db.ScoreSubmissions.Add(submission); submissions.Add(submission);
        }
        else submission.Update(request.HomeScore!.Value, request.AwayScore!.Value);
        workflow.Audit(match, previous is null ? "ScoreSubmitted" : "ScoreUpdated", new { previous, submission.HomeScore, submission.AwayScore }, teamId);
        var disputed = await db.Disputes.AnyAsync(x => x.MatchId == matchId && x.Status == DisputeStatus.Open, ct);
        if (!disputed && submissions.Count == 2 && submissions[0].HomeScore == submissions[1].HomeScore && submissions[0].AwayScore == submissions[1].AwayScore)
            await workflow.ApplyOfficial(match, tournament, submission.HomeScore, submission.AwayScore, "Bilateral", "", ct);
        else
        {
            if (!disputed && submissions.Count == 2)
            {
                var dispute = new Dispute(matchId); db.Disputes.Add(dispute); disputed = true;
                workflow.Audit(match, "DisputeOpened", new { dispute.Id });
            }
            match.AwaitConfirmation(disputed);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Ok(new SubmissionResponse(submission.Id, matchId, teamId, submission.HomeScore, submission.AwayScore, submission.SubmittedAt, submission.UpdatedAt));
    }

    [HttpPut("api/disputes/{disputeId:guid}/resolve")]
    [Authorize]
    public async Task<ActionResult<DisputeResponse>> Resolve(Guid disputeId, ResolveDisputeRequest request, CancellationToken ct)
    {
        var target = await (from d in db.Disputes join m in db.Matches on d.MatchId equals m.Id where d.Id == disputeId select new { m.Id, m.TournamentId }).SingleOrDefaultAsync(ct);
        if (target is null) return NotFound();
        if (!await access.CanManageTournament(User, target.TournamentId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var tournament = await db.Tournaments.FromSqlInterpolated($"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {target.TournamentId} FOR UPDATE").SingleAsync(ct);
        var dispute = await db.Disputes.SingleAsync(x => x.Id == disputeId, ct);
        if (dispute.Status != DisputeStatus.Open) return Problem(statusCode: 409, detail: "Dispute is already resolved.");
        var match = await db.Matches.SingleAsync(x => x.Id == target.Id, ct);
        await new ResultWorkflow(db).ApplyOfficial(match, tournament, request.HomeScore!.Value, request.AwayScore!.Value, "DisputeResolution", request.ResolutionNote, ct);
        await tx.CommitAsync(ct);
        return Ok(new DisputeResponse(dispute.Id, dispute.MatchId, dispute.Status, dispute.OpenedAt, dispute.ResolvedAt, dispute.ResolutionNote));
    }

    [HttpGet("api/matches/{matchId:guid}/audit-log")]
    public async Task<ActionResult<IReadOnlyList<AuditResponse>>> Audit(Guid matchId, CancellationToken ct)
    {
        if (!await db.Matches.AnyAsync(x => x.Id == matchId, ct)) return NotFound();
        return Ok(await db.AuditLogs.AsNoTracking().Where(x => x.MatchId == matchId).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            .Select(x => new AuditResponse(x.Id, x.MatchId, x.TeamId, x.EventType, x.OccurredAt, x.Data)).ToListAsync(ct));
    }
}
