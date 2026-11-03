namespace Projeto_Campeonato.Api.Domain;

// Derived read model, never persisted. Long totals avoid overflow when summing valid int scores.
public sealed record StandingRow(Guid TeamId, string TeamName, int Played, int Wins, int Draws, int Losses,
    long GoalsFor, long GoalsAgainst, long GoalDifference, int Points);

public static class Standings
{
    public static IReadOnlyList<StandingRow> Calculate(IEnumerable<Team> teams, IEnumerable<Match> matches)
    {
        var rows = teams.ToDictionary(x => x.Id, x => new StandingRow(x.Id, x.Name, 0, 0, 0, 0, 0, 0, 0, 0));
        foreach (var match in matches.Where(x => x.Status == MatchStatus.Finished))
        {
            Add(match.HomeTeamId, match.HomeScore!.Value, match.AwayScore!.Value);
            Add(match.AwayTeamId, match.AwayScore!.Value, match.HomeScore!.Value);
        }
        return rows.Values.OrderByDescending(x => x.Points).ThenByDescending(x => x.GoalDifference)
            .ThenByDescending(x => x.GoalsFor).ThenBy(x => x.TeamName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TeamName, StringComparer.Ordinal).ThenBy(x => x.TeamId).ToArray();

        void Add(Guid teamId, int scored, int conceded)
        {
            var previous = rows[teamId];
            rows[teamId] = previous with
            {
                Played = previous.Played + 1,
                Wins = previous.Wins + (scored > conceded ? 1 : 0),
                Draws = previous.Draws + (scored == conceded ? 1 : 0),
                Losses = previous.Losses + (scored < conceded ? 1 : 0),
                GoalsFor = previous.GoalsFor + scored,
                GoalsAgainst = previous.GoalsAgainst + conceded,
                GoalDifference = previous.GoalDifference + (long)scored - conceded,
                Points = previous.Points + (scored > conceded ? 3 : scored == conceded ? 1 : 0)
            };
        }
    }
}
