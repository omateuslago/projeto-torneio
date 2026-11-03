using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Tests;

public class MatchDomainTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 6)]
    [InlineData(6, 15)]
    public void RoundRobinGeneratesEveryPairExactlyOnce(int teamCount, int count)
    {
        var teams = Enumerable.Range(0, teamCount).Select(_ => Guid.NewGuid()).ToArray();
        var id = Guid.NewGuid();
        var matches = RoundRobin.Generate(id, teams);
        Assert.Equal(count, matches.Count);
        Assert.All(matches, x => Assert.NotEqual(x.HomeTeamId, x.AwayTeamId));
        Assert.Equal(count, matches.Select(x => (x.HomeTeamId, x.AwayTeamId)).Distinct().Count());
        Assert.Equal(matches.Select(x => (x.HomeTeamId, x.AwayTeamId)),
            RoundRobin.Generate(id, teams.Reverse()).Select(x => (x.HomeTeamId, x.AwayTeamId)));
        Assert.All(teams, team => Assert.Equal(teamCount - 1, matches.Count(x => x.HomeTeamId == team || x.AwayTeamId == team)));
    }

    [Fact]
    public void TooFewTeamsAreRejected() => Assert.Throws<DomainConflictException>(() => RoundRobin.Generate(Guid.NewGuid(), [Guid.NewGuid()]));

    [Fact]
    public void NegativeResultIsRejected()
    {
        var match = RoundRobin.Generate(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()])[0];
        Assert.Throws<ArgumentException>(() => match.RecordResult(-1, 0));
        Assert.Equal(MatchStatus.Pending, match.Status);
        Assert.Null(match.FinishedAt);
    }

    [Fact]
    public void CorrectionReplacesTotalsAndTimestamp()
    {
        var teams = new[] { new Team("Azul"), new Team("Verde") };
        var match = RoundRobin.Generate(Guid.NewGuid(), teams.Select(x => x.Id))[0];
        match.RecordResult(3, 1);
        var first = match.FinishedAt;
        match.RecordResult(0, 0);
        var rows = Standings.Calculate(teams, [match]);
        Assert.All(rows, x => { Assert.Equal(1, x.Played); Assert.Equal(1, x.Draws); Assert.Equal(1, x.Points); Assert.Equal(0, x.GoalsFor); });
        Assert.True(match.FinishedAt >= first);
        Assert.Equal(MatchStatus.Finished, match.Status);
    }

    [Fact]
    public void StandingsCalculateAllStatisticsAndIgnorePendingMatches()
    {
        var teams = new[] { new Team("A"), new Team("B"), new Team("C") };
        var matches = RoundRobin.Generate(Guid.NewGuid(), teams.Select(x => x.Id));
        foreach (var match in matches)
        {
            if (match.HomeTeamId != teams[0].Id && match.AwayTeamId != teams[0].Id) continue;
            if (match.HomeTeamId == teams[0].Id) match.RecordResult(3, 1); else match.RecordResult(1, 3);
        }
        var rows = Standings.Calculate(teams, matches);
        var a = rows[0];
        Assert.Equal(teams[0].Id, a.TeamId);
        Assert.Equal(new StandingRow(teams[0].Id, "A", 2, 2, 0, 0, 6, 2, 4, 6), a);
        Assert.All(rows.Skip(1), x => { Assert.Equal(1, x.Played); Assert.Equal(1, x.Losses); Assert.Equal(0, x.Points); Assert.Equal(-2, x.GoalDifference); });
    }

    [Theory]
    [InlineData(2, 0, 1, 0, "Zebra")]
    [InlineData(3, 1, 2, 0, "Zebra")]
    [InlineData(2, 0, 2, 0, "Alpha")]
    public void TieBreakersUseDifferenceThenGoalsThenName(int firstFor, int firstAgainst, int secondFor, int secondAgainst, string winner)
    {
        var teams = new[] { new Team("Zebra"), new Team("Alpha"), new Team("Opponent Z"), new Team("Opponent A") };
        var matches = RoundRobin.Generate(Guid.NewGuid(), teams.Select(x => x.Id));
        foreach (var match in matches)
        {
            SetResult(teams[0], teams[2], firstFor, firstAgainst);
            SetResult(teams[1], teams[3], secondFor, secondAgainst);
            void SetResult(Team team, Team opponent, int scored, int conceded)
            {
                if (match.HomeTeamId == team.Id && match.AwayTeamId == opponent.Id) match.RecordResult(scored, conceded);
                else if (match.AwayTeamId == team.Id && match.HomeTeamId == opponent.Id) match.RecordResult(conceded, scored);
            }
        }
        Assert.Equal(winner, Standings.Calculate(teams, matches)[0].TeamName);
    }

    [Fact]
    public void LargeValidScoresDoNotOverflowTotals()
    {
        var teams = new[] { new Team("A"), new Team("B"), new Team("C") };
        var matches = RoundRobin.Generate(Guid.NewGuid(), teams.Select(x => x.Id));
        foreach (var match in matches) match.RecordResult(int.MaxValue, int.MaxValue);
        Assert.All(Standings.Calculate(teams, matches), x => Assert.Equal(2L * int.MaxValue, x.GoalsFor));
    }
}
