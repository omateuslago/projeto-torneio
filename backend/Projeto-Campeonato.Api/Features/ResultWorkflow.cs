using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

// Caller owns the transaction and must lock Tournament before reading mutable match state.
public sealed class ResultWorkflow(TournamentDbContext db)
{
    public void Audit(Match match, string type, object data, Guid? teamId = null) =>
        db.AuditLogs.Add(new AuditLog(match.Id, type, JsonSerializer.Serialize(data), teamId));

    public async Task ApplyOfficial(Match match, Tournament tournament, int home, int away, string source, string note, CancellationToken ct)
    {
        if (tournament.Format == TournamentFormat.SingleElimination && home == away)
            throw new DomainConflictException("Partidas eliminatórias precisam de um vencedor; placares empatados não são permitidos.");

        var previous = new { match.HomeScore, match.AwayScore, match.ResultSource };
        var previousWinner = Winner(match.HomeTeamId, match.AwayTeamId, match.HomeScore, match.AwayScore);
        var nextWinner = Winner(match.HomeTeamId, match.AwayTeamId, home, away);
        BracketNode? bracketNode = null;

        if (tournament.Format == TournamentFormat.SingleElimination)
        {
            bracketNode = await db.BracketNodes.SingleAsync(x => x.MatchId == match.Id, ct);
            if (previousWinner is not null && previousWinner != nextWinner)
                await EnsureSafeWinnerCorrection(bracketNode, previousWinner.Value, nextWinner!.Value, ct);
        }

        var dispute = await db.Disputes.SingleOrDefaultAsync(x => x.MatchId == match.Id && x.Status == DisputeStatus.Open, ct);
        if (dispute is not null)
        {
            dispute.Resolve(note);
            Audit(match, "DisputeResolved", new { dispute.Id, note, home, away, source });
        }
        match.RecordResult(home, away, source);
        Audit(match, source == "Bilateral" ? "AutomaticallyConfirmed" : source == "Manual" ? "AdministrativeResult" : "OfficialResult", new { previous, home, away, source });

        if (bracketNode is not null && (previousWinner is null || previousWinner != nextWinner))
            await AdvanceWinner(bracketNode, previousWinner, nextWinner!.Value, tournament, match, ct);

        await db.SaveChangesAsync(ct);
        if (tournament.Format == TournamentFormat.RoundRobin
            && !await db.Matches.AnyAsync(x => x.TournamentId == tournament.Id && x.Status != MatchStatus.Finished, ct)
            && !await db.Disputes.AnyAsync(x => x.Status == DisputeStatus.Open && db.Matches.Any(m => m.Id == x.MatchId && m.TournamentId == tournament.Id), ct))
        {
            tournament.FinishCompetition();
            await db.SaveChangesAsync(ct);
        }
    }

    private static Guid? Winner(Guid homeTeamId, Guid awayTeamId, int? home, int? away)
    {
        if (home is null || away is null || home == away) return null;
        return home > away ? homeTeamId : awayTeamId;
    }

    private async Task EnsureSafeWinnerCorrection(BracketNode source, Guid previousWinner, Guid nextWinner, CancellationToken ct)
    {
        if (source.NextNodeId is null) return;
        var nextNode = await db.BracketNodes.SingleAsync(x => x.Id == source.NextNodeId, ct);
        if (nextNode.MatchId is null)
        {
            nextNode.ReplaceTeam(previousWinner, nextWinner);
            return;
        }

        var nextMatch = await db.Matches.SingleAsync(x => x.Id == nextNode.MatchId, ct);
        var hasActivity = nextMatch.Status != MatchStatus.Pending || nextMatch.StartsAt is not null
            || nextMatch.PlayingAreaId is not null || nextMatch.RefereeId is not null
            || await db.ScoreSubmissions.AnyAsync(x => x.MatchId == nextMatch.Id, ct)
            || await db.Disputes.AnyAsync(x => x.MatchId == nextMatch.Id, ct);
        if (hasActivity)
            throw new DomainConflictException("O confronto seguinte já possui atividade. Remova a atividade antes de corrigir o vencedor.");
        nextNode.ReplaceTeam(previousWinner, nextWinner);
        nextMatch.ReplaceParticipant(previousWinner, nextWinner);
    }

    private async Task AdvanceWinner(BracketNode source, Guid? previousWinner, Guid winner, Tournament tournament,
        Match sourceMatch, CancellationToken ct)
    {
        if (source.NextNodeId is null)
        {
            tournament.FinishCompetition();
            Audit(sourceMatch, "TournamentChampionDefined", new { tournament.Id, winner }, winner);
            return;
        }

        if (previousWinner is not null)
        {
            Audit(sourceMatch, "TeamAdvanced", new { source.NextNodeId, previousWinner, winner, corrected = true }, winner);
            return;
        }

        var nextNode = await db.BracketNodes.SingleAsync(x => x.Id == source.NextNodeId, ct);
        nextNode.PlaceTeam(source.NextSlot!.Value, winner);
        Audit(sourceMatch, "TeamAdvanced", new { source.NextNodeId, winner }, winner);
        if (nextNode.TopTeamId is not null && nextNode.BottomTeamId is not null && nextNode.MatchId is null)
        {
            var first = nextNode.TopTeamId.Value;
            var second = nextNode.BottomTeamId.Value;
            var nextMatch = first.CompareTo(second) < 0
                ? new Match(tournament.Id, first, second)
                : new Match(tournament.Id, second, first);
            nextNode.AttachMatch(nextMatch.Id);
            db.Matches.Add(nextMatch);
        }
    }
}
