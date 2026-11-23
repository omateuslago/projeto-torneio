using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record BracketTeamResponse(Guid Id, string Name);
public sealed record BracketMatchResponse(Guid Id, MatchStatus Status, int? TopScore, int? BottomScore,
    Guid? WinnerTeamId, DateTime? StartsAt, Guid? PlayingAreaId);
public sealed record BracketNodeResponse(Guid Id, int Position, BracketTeamResponse? TopTeam,
    BracketTeamResponse? BottomTeam, bool IsBye, BracketMatchResponse? Match);
public sealed record BracketRoundResponse(int Number, string Name, IReadOnlyList<BracketNodeResponse> Nodes);
public sealed record BracketResponse(Guid TournamentId, TournamentStatus Status,
    IReadOnlyList<BracketRoundResponse> Rounds, BracketTeamResponse? Champion);

[ApiController]
public sealed class BracketController(TournamentDbContext db) : ControllerBase
{
    [HttpGet("api/tournaments/{tournamentId:guid}/bracket")]
    public async Task<ActionResult<BracketResponse>> Get(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tournamentId, ct);
        if (tournament is null) return NotFound();
        if (tournament.Format != TournamentFormat.SingleElimination)
            return Problem(statusCode: 409, detail: "A chave eliminatória está disponível apenas para campeonatos mata-mata.");

        var nodes = await db.BracketNodes.AsNoTracking().Where(x => x.TournamentId == tournamentId)
            .OrderBy(x => x.RoundNumber).ThenBy(x => x.Position).ToListAsync(ct);
        if (nodes.Count == 0)
            return Ok(new BracketResponse(tournament.Id, tournament.Status, [], null));

        var teamIds = nodes.SelectMany(x => new[] { x.TopTeamId, x.BottomTeamId }).Where(x => x is not null)
            .Select(x => x!.Value).Distinct().ToArray();
        var teams = await db.Teams.AsNoTracking().Where(x => teamIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new BracketTeamResponse(x.Id, x.Name), ct);
        var matchIds = nodes.Where(x => x.MatchId is not null).Select(x => x.MatchId!.Value).ToArray();
        var matches = await db.Matches.AsNoTracking().Where(x => matchIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var totalRounds = nodes.Max(x => x.RoundNumber);

        var rounds = nodes.GroupBy(x => x.RoundNumber).OrderBy(x => x.Key).Select(round =>
            new BracketRoundResponse(round.Key, RoundName(round.Key, totalRounds), round.Select(node =>
            {
                BracketMatchResponse? matchResponse = null;
                if (node.MatchId is not null && matches.TryGetValue(node.MatchId.Value, out var match))
                {
                    var topScore = node.TopTeamId == match.HomeTeamId ? match.HomeScore : match.AwayScore;
                    var bottomScore = node.BottomTeamId == match.HomeTeamId ? match.HomeScore : match.AwayScore;
                    var winner = match.Status == MatchStatus.Finished && match.HomeScore != match.AwayScore
                        ? match.HomeScore > match.AwayScore ? match.HomeTeamId : match.AwayTeamId
                        : (Guid?)null;
                    matchResponse = new BracketMatchResponse(match.Id, match.Status, topScore, bottomScore,
                        winner, match.StartsAt, match.PlayingAreaId);
                }
                return new BracketNodeResponse(node.Id, node.Position,
                    node.TopTeamId is not null ? teams[node.TopTeamId.Value] : null,
                    node.BottomTeamId is not null ? teams[node.BottomTeamId.Value] : null,
                    node.IsBye, matchResponse);
            }).ToArray())).ToArray();

        var final = nodes.Single(x => x.RoundNumber == totalRounds);
        BracketTeamResponse? champion = null;
        if (final.MatchId is not null && matches.TryGetValue(final.MatchId.Value, out var finalMatch)
            && finalMatch.Status == MatchStatus.Finished && finalMatch.HomeScore != finalMatch.AwayScore)
        {
            var championId = finalMatch.HomeScore > finalMatch.AwayScore ? finalMatch.HomeTeamId : finalMatch.AwayTeamId;
            champion = teams[championId];
        }
        return Ok(new BracketResponse(tournament.Id, tournament.Status, rounds, champion));
    }

    private static string RoundName(int round, int totalRounds) => (totalRounds - round) switch
    {
        0 => "Final",
        1 => "Semifinais",
        2 => "Quartas de final",
        3 => "Oitavas de final",
        4 => "16 avos de final",
        var distance => $"Fase de {1 << (distance + 1)}"
    };
}
