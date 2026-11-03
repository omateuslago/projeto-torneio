using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Tests;

public class DomainTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(129)]
    public void InvalidCapacityIsRejected(int capacity) =>
        Assert.Throws<ArgumentException>(() => new Tournament("Cup", "Futsal", capacity, TournamentFormat.RoundRobin));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTeamNameIsRejected(string name) => Assert.Throws<ArgumentException>(() => new Team(name));

    [Fact]
    public void NamesAreTrimmed()
    {
        var tournament = new Tournament(" Cup ", " Futsal ", 8, TournamentFormat.SingleElimination);
        Assert.Equal("Cup", tournament.Name);
        Assert.Equal("Futsal", tournament.Sport);
        Assert.Equal("Team", new Team(" Team ").Name);
    }

    [Fact]
    public void DraftCannotAcceptRegistrations() =>
        Assert.Throws<DomainConflictException>(() => NewTournament().EnsureCanRegister(0));

    [Fact]
    public void ClosedCannotReopen()
    {
        var tournament = NewTournament();
        tournament.ChangeStatus(TournamentStatus.Open);
        tournament.ChangeStatus(TournamentStatus.Closed);
        Assert.Throws<DomainConflictException>(() => tournament.ChangeStatus(TournamentStatus.Open));
    }

    [Fact]
    public void FullTournamentRejectsRegistration()
    {
        var tournament = NewTournament();
        tournament.ChangeStatus(TournamentStatus.Open);
        Assert.Throws<DomainConflictException>(() => tournament.EnsureCanRegister(2));
    }

    [Fact]
    public void RegistrationRequiresBothIdentifiers() =>
        Assert.Throws<ArgumentException>(() => new Registration(Guid.NewGuid(), Guid.Empty));

    private static Tournament NewTournament() => new("Cup", "Futsal", 2, TournamentFormat.RoundRobin);
}
