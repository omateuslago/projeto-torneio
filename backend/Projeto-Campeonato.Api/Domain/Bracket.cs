namespace Projeto_Campeonato.Api.Domain;

public enum BracketSlot { Top, Bottom }

public sealed class BracketNode
{
    private BracketNode() { }

    public BracketNode(Guid tournamentId, int roundNumber, int position)
    {
        if (tournamentId == Guid.Empty) throw new ArgumentException("Tournament is required.");
        if (roundNumber < 1 || position < 1) throw new ArgumentException("Round and position must be positive.");
        TournamentId = tournamentId;
        RoundNumber = roundNumber;
        Position = position;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TournamentId { get; private set; }
    public int RoundNumber { get; private set; }
    public int Position { get; private set; }
    public Guid? TopTeamId { get; private set; }
    public Guid? BottomTeamId { get; private set; }
    public Guid? MatchId { get; private set; }
    public Guid? NextNodeId { get; private set; }
    public BracketSlot? NextSlot { get; private set; }
    public bool IsBye { get; private set; }

    public void ConfigureNext(Guid nextNodeId, BracketSlot nextSlot)
    {
        if (nextNodeId == Guid.Empty || nextNodeId == Id) throw new ArgumentException("A valid next node is required.");
        NextNodeId = nextNodeId;
        NextSlot = nextSlot;
    }

    public void Seed(Guid? topTeamId, Guid? bottomTeamId)
    {
        ValidateTeams(topTeamId, bottomTeamId);
        TopTeamId = topTeamId;
        BottomTeamId = bottomTeamId;
    }

    public void PlaceTeam(BracketSlot slot, Guid teamId)
    {
        if (teamId == Guid.Empty) throw new ArgumentException("Team is required.");
        var current = slot == BracketSlot.Top ? TopTeamId : BottomTeamId;
        if (current is not null && current != teamId)
            throw new DomainConflictException("A chave já possui uma equipe nessa posição.");
        var other = slot == BracketSlot.Top ? BottomTeamId : TopTeamId;
        if (other == teamId) throw new DomainConflictException("Uma equipe não pode enfrentar a si mesma.");
        if (slot == BracketSlot.Top) TopTeamId = teamId;
        else BottomTeamId = teamId;
    }

    public void ReplaceTeam(Guid previousTeamId, Guid nextTeamId)
    {
        if (nextTeamId == Guid.Empty) throw new ArgumentException("Team is required.");
        if (TopTeamId == previousTeamId)
        {
            if (BottomTeamId == nextTeamId) throw new DomainConflictException("Uma equipe não pode enfrentar a si mesma.");
            TopTeamId = nextTeamId;
            return;
        }
        if (BottomTeamId == previousTeamId)
        {
            if (TopTeamId == nextTeamId) throw new DomainConflictException("Uma equipe não pode enfrentar a si mesma.");
            BottomTeamId = nextTeamId;
            return;
        }
        throw new DomainConflictException("A equipe anterior não ocupa essa etapa da chave.");
    }

    public void AttachMatch(Guid matchId)
    {
        if (matchId == Guid.Empty || TopTeamId is null || BottomTeamId is null)
            throw new ArgumentException("A match requires two known teams.");
        if (MatchId is not null && MatchId != matchId)
            throw new DomainConflictException("A etapa da chave já possui uma partida.");
        MatchId = matchId;
    }

    public void MarkBye()
    {
        if ((TopTeamId is null) == (BottomTeamId is null))
            throw new DomainConflictException("A bye requires exactly one known team.");
        IsBye = true;
    }

    private static void ValidateTeams(Guid? topTeamId, Guid? bottomTeamId)
    {
        if (topTeamId == Guid.Empty || bottomTeamId == Guid.Empty)
            throw new ArgumentException("Empty team identifiers are not valid.");
        if (topTeamId is not null && topTeamId == bottomTeamId)
            throw new ArgumentException("Teams must be distinct.");
    }
}

public sealed record BracketGeneration(IReadOnlyList<BracketNode> Nodes, IReadOnlyList<Match> Matches);

public static class SingleEliminationBracket
{
    public static BracketGeneration Generate(Guid tournamentId, IEnumerable<Guid> orderedTeamIds)
    {
        var teams = orderedTeamIds.ToArray();
        if (teams.Length < 2) throw new DomainConflictException("At least two teams are required.");
        if (teams.Any(x => x == Guid.Empty) || teams.Distinct().Count() != teams.Length)
            throw new ArgumentException("Teams must be valid and unique.");

        var bracketSize = 2;
        while (bracketSize < teams.Length) bracketSize *= 2;
        var roundCount = 0;
        for (var size = bracketSize; size > 1; size /= 2) roundCount++;

        var nodes = new List<BracketNode>();
        var byRound = new Dictionary<(int Round, int Position), BracketNode>();
        for (var round = 1; round <= roundCount; round++)
        {
            var count = bracketSize >> round;
            for (var position = 1; position <= count; position++)
            {
                var node = new BracketNode(tournamentId, round, position);
                nodes.Add(node);
                byRound[(round, position)] = node;
            }
        }

        for (var round = 1; round < roundCount; round++)
        {
            var count = bracketSize >> round;
            for (var position = 1; position <= count; position++)
            {
                var nextPosition = (position + 1) / 2;
                var slot = position % 2 == 1 ? BracketSlot.Top : BracketSlot.Bottom;
                byRound[(round, position)].ConfigureNext(byRound[(round + 1, nextPosition)].Id, slot);
            }
        }

        var seedPositions = BalancedSeedPositions(bracketSize);
        var matches = new List<Match>();
        for (var position = 1; position <= bracketSize / 2; position++)
        {
            var node = byRound[(1, position)];
            var topSeed = seedPositions[(position - 1) * 2];
            var bottomSeed = seedPositions[(position - 1) * 2 + 1];
            node.Seed(TeamAtSeed(teams, topSeed), TeamAtSeed(teams, bottomSeed));

            if (node.TopTeamId is not null && node.BottomTeamId is not null)
            {
                AttachMatch(node, tournamentId, matches);
            }
            else
            {
                node.MarkBye();
                var advancingTeam = node.TopTeamId ?? node.BottomTeamId!.Value;
                AdvanceBye(node, advancingTeam, nodes, tournamentId, matches);
            }
        }

        return new BracketGeneration(nodes, matches);
    }

    private static Guid? TeamAtSeed(IReadOnlyList<Guid> teams, int seed) => seed <= teams.Count ? teams[seed - 1] : null;

    private static int[] BalancedSeedPositions(int bracketSize)
    {
        var positions = new List<int> { 1, 2 };
        for (var size = 2; size < bracketSize; size *= 2)
        {
            var next = new List<int>(size * 2);
            foreach (var seed in positions)
            {
                next.Add(seed);
                next.Add(size * 2 + 1 - seed);
            }
            positions = next;
        }
        return positions.ToArray();
    }

    private static void AdvanceBye(BracketNode source, Guid teamId, IReadOnlyList<BracketNode> nodes,
        Guid tournamentId, ICollection<Match> matches)
    {
        if (source.NextNodeId is null || source.NextSlot is null) return;
        var next = nodes.Single(x => x.Id == source.NextNodeId.Value);
        next.PlaceTeam(source.NextSlot.Value, teamId);
        if (next.TopTeamId is not null && next.BottomTeamId is not null && next.MatchId is null)
            AttachMatch(next, tournamentId, matches);
    }

    private static void AttachMatch(BracketNode node, Guid tournamentId, ICollection<Match> matches)
    {
        var first = node.TopTeamId!.Value;
        var second = node.BottomTeamId!.Value;
        var match = first.CompareTo(second) < 0
            ? new Match(tournamentId, first, second)
            : new Match(tournamentId, second, first);
        node.AttachMatch(match.Id);
        matches.Add(match);
    }
}
