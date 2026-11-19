using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record AcceptInvitationRequest([Required] string Token);
public sealed record InvitationPreviewResponse(string Type, string TargetName, Guid TargetId,
    DateTimeOffset ExpiresAt, string Status);
public sealed record AcceptedInvitationResponse(string Type, Guid TargetId);

[ApiController]
[Authorize]
[Route("api/access-invitations")]
public sealed class AccessInvitationsController(TournamentDbContext db, UserManager<ApplicationUser> users,
    IAccessInvitationTokens tokens) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult<InvitationPreviewResponse>> Preview([FromQuery] string token, CancellationToken ct)
    {
        var loaded = await Load(token, ct);
        if (loaded.Error is not null) return loaded.Error;
        var invitation = loaded.Invitation!;
        var state = ValidateState(invitation);
        if (state is not null) return state;
        if (!await MatchesCurrentEmail(invitation)) return Forbid();
        if (invitation.TournamentId.HasValue)
        {
            var target = await db.Tournaments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invitation.TournamentId, ct);
            if (target is null) return NotFound();
            return Ok(new InvitationPreviewResponse("TournamentOrganizer", target.Name, target.Id, invitation.ExpiresAt, "Pending"));
        }
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invitation.TeamId, ct);
        if (team is null) return NotFound();
        return Ok(new InvitationPreviewResponse("TeamManager", team.Name, team.Id, invitation.ExpiresAt, "Pending"));
    }

    [HttpPost("accept")]
    public async Task<ActionResult<AcceptedInvitationResponse>> Accept(AcceptInvitationRequest request, CancellationToken ct)
    {
        if (!tokens.TryRead(request.Token, out var invitationId)) return BadRequestProblem();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var invitation = await db.AccessInvitations.SingleOrDefaultAsync(x => x.Id == invitationId, ct);
        if (invitation is null) return BadRequestProblem();
        var state = ValidateState(invitation);
        if (state is not null) return state;
        var user = await users.GetUserAsync(User);
        if (user is null || user.NormalizedEmail != invitation.NormalizedEmail) return Forbid();
        if (invitation.TournamentId.HasValue)
        {
            if (await db.TournamentOrganizers.AnyAsync(x => x.TournamentId == invitation.TournamentId && x.UserId == user.Id, ct))
                return ConflictProblem("User is already a tournament organizer.");
            db.TournamentOrganizers.Add(new TournamentOrganizer(user.Id, invitation.TournamentId.Value));
        }
        else
        {
            if (await db.TeamManagers.AnyAsync(x => x.TeamId == invitation.TeamId && x.UserId == user.Id, ct))
                return ConflictProblem("User is already a team manager.");
            db.TeamManagers.Add(new TeamManager(user.Id, invitation.TeamId!.Value));
        }
        invitation.Accept(user.Id, DateTimeOffset.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ConflictProblem("Invitation was already accepted.");
        }
        return Ok(new AcceptedInvitationResponse(
            invitation.TournamentId.HasValue ? "TournamentOrganizer" : "TeamManager",
            invitation.TournamentId ?? invitation.TeamId!.Value));
    }

    private async Task<(AccessInvitation? Invitation, ActionResult? Error)> Load(string token, CancellationToken ct)
    {
        if (!tokens.TryRead(token, out var id)) return (null, BadRequestProblem());
        var invitation = await db.AccessInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return invitation is null ? (null, BadRequestProblem()) : (invitation, null);
    }

    private ActionResult? ValidateState(AccessInvitation invitation)
    {
        if (!invitation.Active || invitation.AcceptedAt.HasValue || invitation.RevokedAt.HasValue)
            return ConflictProblem("Invitation is no longer pending.");
        return invitation.ExpiresAt <= DateTimeOffset.UtcNow
            ? ConflictProblem("Invitation has expired.") : null;
    }

    private async Task<bool> MatchesCurrentEmail(AccessInvitation invitation)
    {
        var user = await users.GetUserAsync(User);
        return user?.NormalizedEmail == invitation.NormalizedEmail;
    }

    private ObjectResult BadRequestProblem() => Problem(statusCode: 400, detail: "Invalid invitation token.");
    private ObjectResult ConflictProblem(string detail) => Problem(statusCode: 409, detail: detail);
}
