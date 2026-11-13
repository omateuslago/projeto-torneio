using Microsoft.AspNetCore.Identity;

namespace Projeto_Campeonato.Api.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
}

public sealed class TournamentOrganizer
{
    private TournamentOrganizer() { }
    public TournamentOrganizer(Guid userId, Guid tournamentId)
    { UserId = userId; TournamentId = tournamentId; }
    public Guid UserId { get; private set; }
    public Guid TournamentId { get; private set; }
}

public sealed class TeamManager
{
    private TeamManager() { }
    public TeamManager(Guid userId, Guid teamId)
    { UserId = userId; TeamId = teamId; }
    public Guid UserId { get; private set; }
    public Guid TeamId { get; private set; }
}
