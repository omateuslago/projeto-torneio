using System.ComponentModel.DataAnnotations;
using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Features;

public sealed record CreateTournamentRequest(
    [Required, StringLength(120)] string Name,
    [Required, StringLength(80)] string Sport,
    [Range(2, 128)] int MaxTeams,
    [EnumDataType(typeof(TournamentFormat))] TournamentFormat Format);
public sealed record ChangeStatusRequest([EnumDataType(typeof(TournamentStatus))] TournamentStatus Status);
public sealed record CreateTeamRequest([Required, StringLength(120)] string Name);
public sealed record RegisterTeamRequest(Guid TeamId);
public sealed record TournamentResponse(Guid Id, string Name, string Sport, int MaxTeams, TournamentFormat Format, TournamentStatus Status)
{
    public static TournamentResponse From(Tournament value) => new(value.Id, value.Name, value.Sport, value.MaxTeams, value.Format, value.Status);
}
public sealed record TeamResponse(Guid Id, string Name);
public sealed record RegistrationResponse(Guid Id, Guid TournamentId, Guid TeamId, string TeamName, DateTime RegisteredAt);
