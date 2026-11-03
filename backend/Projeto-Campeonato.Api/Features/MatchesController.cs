using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record MatchResponse(Guid Id, Guid TournamentId, Guid HomeTeamId, string HomeTeamName,
    Guid AwayTeamId, string AwayTeamName, MatchStatus Status, int? HomeScore, int? AwayScore, DateTime CreatedAt, DateTime? FinishedAt)
{
    public string? ResultSource { get; init; }
    public IReadOnlyList<SubmissionResponse> Submissions { get; init; } = [];
    public DisputeResponse? Dispute { get; init; }
}
public sealed record RecordResultRequest([Required, Range(0, int.MaxValue)] int? HomeScore, [Required, Range(0, int.MaxValue)] int? AwayScore);

[ApiController]
public sealed class MatchesController(TournamentDbContext db) : ControllerBase
{
    private IQueryable<MatchResponse> MatchQuery(Guid tournamentId, Guid? matchId = null) =>
        from match in db.Matches.AsNoTracking().Where(x => x.TournamentId == tournamentId && (matchId == null || x.Id == matchId))
        join home in db.Teams on match.HomeTeamId equals home.Id
        join away in db.Teams on match.AwayTeamId equals away.Id
        orderby match.HomeTeamId, match.AwayTeamId
        select new MatchResponse(match.Id, match.TournamentId, home.Id, home.Name, away.Id, away.Name,
            match.Status, match.HomeScore, match.AwayScore, match.CreatedAt, match.FinishedAt) { ResultSource = match.ResultSource };

    [HttpGet("api/tournaments/{tournamentId:guid}/matches")]
    public async Task<ActionResult<IReadOnlyList<MatchResponse>>> List(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        // Keep match status and submission/dispute details in the same read snapshot.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var matches = await MatchQuery(tournamentId).ToListAsync(ct);
        var ids = matches.Select(x => x.Id).ToArray();
        var submissions = await db.ScoreSubmissions.AsNoTracking().Where(x => ids.Contains(x.MatchId))
            .Select(x => new SubmissionResponse(x.Id, x.MatchId, x.SubmittedByTeamId, x.HomeScore, x.AwayScore, x.SubmittedAt, x.UpdatedAt)).ToListAsync(ct);
        var disputes = await db.Disputes.AsNoTracking().Where(x => ids.Contains(x.MatchId))
            .Select(x => new DisputeResponse(x.Id, x.MatchId, x.Status, x.OpenedAt, x.ResolvedAt, x.ResolutionNote)).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(matches.Select(x => x with { Submissions = submissions.Where(s => s.MatchId == x.Id).ToArray(), Dispute = disputes.Where(d => d.MatchId == x.Id).OrderByDescending(d => d.OpenedAt).FirstOrDefault() }));
    }

    [HttpPost("api/tournaments/{tournamentId:guid}/matches/generate")]
    public async Task<ActionResult<IReadOnlyList<MatchResponse>>> Generate(Guid tournamentId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize generation and results for the same tournament, including across API instances.
        var tournament = await db.Tournaments.FromSqlInterpolated(
            $"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {tournamentId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (tournament is null) return NotFound();
        if (!await db.Matches.AnyAsync(x => x.TournamentId == tournamentId, ct))
        {
            tournament.StartCompetition();
            var teams = await db.Registrations.Where(x => x.TournamentId == tournamentId).Select(x => x.TeamId).ToListAsync(ct);
            db.Matches.AddRange(RoundRobin.Generate(tournamentId, teams));
            await db.SaveChangesAsync(ct);
        }
        var matches = await MatchQuery(tournamentId).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(matches);
    }

    [HttpPut("api/matches/{matchId:guid}/result")]
    public async Task<ActionResult<MatchResponse>> RecordResult(Guid matchId, RecordResultRequest request, CancellationToken ct)
    {
        var tournamentId = await db.Matches.Where(x => x.Id == matchId).Select(x => (Guid?)x.TournamentId).SingleOrDefaultAsync(ct);
        if (tournamentId is null) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var tournament = await db.Tournaments.FromSqlInterpolated(
            $"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {tournamentId.Value} FOR UPDATE").SingleAsync(ct);
        var match = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        await new ResultWorkflow(db).ApplyOfficial(match, tournament, request.HomeScore!.Value, request.AwayScore!.Value,
            "Manual", "Resultado definido por override administrativo.", ct);
        var result = await MatchQuery(tournament.Id, matchId).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(result);
    }
}
