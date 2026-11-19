using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record CreateAccessInvitationRequest([Required, EmailAddress, StringLength(256)] string Email);
public sealed record AccessMemberResponse(Guid UserId, string DisplayName, string Email);
public sealed record PendingInvitationResponse(Guid Id, string Email, DateTimeOffset ExpiresAt);
public sealed record AccessMembersResponse(AccessMemberResponse[] Members, PendingInvitationResponse[] PendingInvitations);
public sealed record CreatedInvitationResponse(Guid Id, string Token, DateTimeOffset ExpiresAt, string RelativePath);

[ApiController]
[Authorize]
[Route("api")]
public sealed class AccessManagementController(TournamentDbContext db, IAccessControl access,
    ILookupNormalizer normalizer, IAccessInvitationTokens tokens) : ControllerBase
{
    [HttpGet("tournaments/{id:guid}/organizers")]
    public async Task<ActionResult<AccessMembersResponse>> TournamentMembers(Guid id, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        if (!await db.Tournaments.AnyAsync(x => x.Id == id, ct)) return NotFound();
        return Ok(await TournamentResponse(id, ct));
    }

    [HttpPost("tournaments/{id:guid}/organizer-invitations")]
    public async Task<ActionResult<CreatedInvitationResponse>> InviteTournament(Guid id,
        CreateAccessInvitationRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        if (!await db.Tournaments.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var email = request.Email.Trim();
        var normalized = normalizer.NormalizeEmail(email);
        var existing = await db.Users.Where(x => x.NormalizedEmail == normalized).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (existing.HasValue && await db.TournamentOrganizers.AnyAsync(
                x => x.TournamentId == id && x.UserId == existing.Value, ct))
            return ConflictProblem("User is already a tournament organizer.");
        await ExpireOldInvitations(id, null, normalized, ct);
        if (await db.AccessInvitations.AnyAsync(x => x.Active && x.TournamentId == id && x.NormalizedEmail == normalized, ct))
            return ConflictProblem("An active invitation already exists for this email.");
        var invitation = AccessInvitation.ForTournament(id, email, normalized, access.UserId(User), DateTimeOffset.UtcNow);
        db.AccessInvitations.Add(invitation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return ConflictProblem("An active invitation already exists for this email."); }
        return CreatedInvitation(invitation);
    }

    [HttpDelete("tournaments/{id:guid}/organizer-invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeTournament(Guid id, Guid invitationId, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        var invitation = await db.AccessInvitations.SingleOrDefaultAsync(
            x => x.Id == invitationId && x.TournamentId == id, ct);
        return await Revoke(invitation, ct);
    }

    [HttpDelete("tournaments/{id:guid}/organizers/{userId:guid}")]
    public async Task<IActionResult> RemoveTournamentMember(Guid id, Guid userId, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, id, ct)) return Forbid();
        if (access.UserId(User) == userId) return ConflictProblem("You cannot remove yourself.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var member = await db.TournamentOrganizers.SingleOrDefaultAsync(x => x.TournamentId == id && x.UserId == userId, ct);
        if (member is null) return NotFound();
        if (await db.TournamentOrganizers.CountAsync(x => x.TournamentId == id, ct) <= 1)
            return ConflictProblem("The last tournament organizer cannot be removed.");
        db.TournamentOrganizers.Remove(member);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("teams/{id:guid}/managers")]
    public async Task<ActionResult<AccessMembersResponse>> TeamMembers(Guid id, CancellationToken ct)
    {
        if (!await access.CanManageTeam(User, id, ct)) return Forbid();
        if (!await db.Teams.AnyAsync(x => x.Id == id, ct)) return NotFound();
        return Ok(await TeamResponse(id, ct));
    }

    [HttpPost("teams/{id:guid}/manager-invitations")]
    public async Task<ActionResult<CreatedInvitationResponse>> InviteTeam(Guid id,
        CreateAccessInvitationRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTeam(User, id, ct)) return Forbid();
        if (!await db.Teams.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var email = request.Email.Trim();
        var normalized = normalizer.NormalizeEmail(email);
        var existing = await db.Users.Where(x => x.NormalizedEmail == normalized).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (existing.HasValue && await db.TeamManagers.AnyAsync(x => x.TeamId == id && x.UserId == existing.Value, ct))
            return ConflictProblem("User is already a team manager.");
        await ExpireOldInvitations(null, id, normalized, ct);
        if (await db.AccessInvitations.AnyAsync(x => x.Active && x.TeamId == id && x.NormalizedEmail == normalized, ct))
            return ConflictProblem("An active invitation already exists for this email.");
        var invitation = AccessInvitation.ForTeam(id, email, normalized, access.UserId(User), DateTimeOffset.UtcNow);
        db.AccessInvitations.Add(invitation);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return ConflictProblem("An active invitation already exists for this email."); }
        return CreatedInvitation(invitation);
    }

    [HttpDelete("teams/{id:guid}/manager-invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeTeam(Guid id, Guid invitationId, CancellationToken ct)
    {
        if (!await access.CanManageTeam(User, id, ct)) return Forbid();
        var invitation = await db.AccessInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.TeamId == id, ct);
        return await Revoke(invitation, ct);
    }

    [HttpDelete("teams/{id:guid}/managers/{userId:guid}")]
    public async Task<IActionResult> RemoveTeamMember(Guid id, Guid userId, CancellationToken ct)
    {
        if (!await access.CanManageTeam(User, id, ct)) return Forbid();
        if (access.UserId(User) == userId) return ConflictProblem("You cannot remove yourself.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var member = await db.TeamManagers.SingleOrDefaultAsync(x => x.TeamId == id && x.UserId == userId, ct);
        if (member is null) return NotFound();
        if (await db.TeamManagers.CountAsync(x => x.TeamId == id, ct) <= 1)
            return ConflictProblem("The last team manager cannot be removed.");
        db.TeamManagers.Remove(member);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    private async Task<AccessMembersResponse> TournamentResponse(Guid id, CancellationToken ct) => new(
        await (from membership in db.TournamentOrganizers
               join user in db.Users on membership.UserId equals user.Id
               where membership.TournamentId == id
               orderby user.DisplayName, user.Email
               select new AccessMemberResponse(user.Id, user.DisplayName, user.Email!)).ToArrayAsync(ct),
        await Pending(x => x.TournamentId == id, ct));

    private async Task<AccessMembersResponse> TeamResponse(Guid id, CancellationToken ct) => new(
        await (from membership in db.TeamManagers
               join user in db.Users on membership.UserId equals user.Id
               where membership.TeamId == id
               orderby user.DisplayName, user.Email
               select new AccessMemberResponse(user.Id, user.DisplayName, user.Email!)).ToArrayAsync(ct),
        await Pending(x => x.TeamId == id, ct));

    private Task<PendingInvitationResponse[]> Pending(System.Linq.Expressions.Expression<Func<AccessInvitation, bool>> target,
        CancellationToken ct) => db.AccessInvitations.AsNoTracking().Where(target)
        .Where(x => x.Active && x.ExpiresAt > DateTimeOffset.UtcNow).OrderBy(x => x.InvitedEmail)
        .Select(x => new PendingInvitationResponse(x.Id, x.InvitedEmail, x.ExpiresAt)).ToArrayAsync(ct);

    private async Task ExpireOldInvitations(Guid? tournamentId, Guid? teamId, string normalizedEmail, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.AccessInvitations.Where(x => x.Active && x.ExpiresAt <= now &&
            x.TournamentId == tournamentId && x.TeamId == teamId && x.NormalizedEmail == normalizedEmail).ToListAsync(ct);
        foreach (var invitation in expired) invitation.Revoke(now);
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    private ActionResult<CreatedInvitationResponse> CreatedInvitation(AccessInvitation invitation)
    {
        var token = tokens.Create(invitation.Id);
        return StatusCode(StatusCodes.Status201Created, new CreatedInvitationResponse(invitation.Id, token,
            invitation.ExpiresAt, $"/convites/aceitar?token={Uri.EscapeDataString(token)}"));
    }

    private async Task<IActionResult> Revoke(AccessInvitation? invitation, CancellationToken ct)
    {
        if (invitation is null) return NotFound();
        if (!invitation.Active) return ConflictProblem("Invitation is no longer pending.");
        invitation.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ObjectResult ConflictProblem(string detail) => Problem(statusCode: 409, detail: detail);
}
