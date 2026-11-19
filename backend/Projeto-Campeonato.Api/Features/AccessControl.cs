using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public interface IAccessControl
{
    Guid UserId(ClaimsPrincipal user);
    Task<bool> IsPlatformAdmin(ClaimsPrincipal user);
    Task<bool> CanManageTournament(ClaimsPrincipal user, Guid tournamentId, CancellationToken ct);
    Task<bool> CanManageTeam(ClaimsPrincipal user, Guid teamId, CancellationToken ct);
}

public sealed class AccessControl(TournamentDbContext db, UserManager<ApplicationUser> users) : IAccessControl
{
    public Guid UserId(ClaimsPrincipal user) => Guid.TryParse(users.GetUserId(user), out var id)
        ? id : throw new InvalidOperationException("Authenticated user has no valid identifier.");
    public Task<bool> IsPlatformAdmin(ClaimsPrincipal user) => Task.FromResult(user.IsInRole("PlatformAdmin"));
    public async Task<bool> CanManageTournament(ClaimsPrincipal user, Guid tournamentId, CancellationToken ct) =>
        user.IsInRole("PlatformAdmin") || await db.TournamentOrganizers.AnyAsync(
            x => x.UserId == UserId(user) && x.TournamentId == tournamentId, ct);
    public async Task<bool> CanManageTeam(ClaimsPrincipal user, Guid teamId, CancellationToken ct) =>
        user.IsInRole("PlatformAdmin") || await db.TeamManagers.AnyAsync(
            x => x.UserId == UserId(user) && x.TeamId == teamId, ct);
}
