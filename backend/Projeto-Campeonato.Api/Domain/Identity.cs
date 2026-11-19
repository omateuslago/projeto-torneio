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

public sealed class AccessInvitation
{
    private AccessInvitation() { }

    private AccessInvitation(Guid? tournamentId, Guid? teamId, string invitedEmail,
        string normalizedEmail, Guid createdByUserId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TournamentId = tournamentId;
        TeamId = teamId;
        InvitedEmail = invitedEmail;
        NormalizedEmail = normalizedEmail;
        CreatedByUserId = createdByUserId;
        CreatedAt = now;
        ExpiresAt = now.AddHours(72);
        Active = true;
    }

    public static AccessInvitation ForTournament(Guid tournamentId, string email,
        string normalizedEmail, Guid createdByUserId, DateTimeOffset now) =>
        new(tournamentId, null, email, normalizedEmail, createdByUserId, now);

    public static AccessInvitation ForTeam(Guid teamId, string email,
        string normalizedEmail, Guid createdByUserId, DateTimeOffset now) =>
        new(null, teamId, email, normalizedEmail, createdByUserId, now);

    public Guid Id { get; private set; }
    public Guid? TournamentId { get; private set; }
    public Guid? TeamId { get; private set; }
    public string InvitedEmail { get; private set; } = "";
    public string NormalizedEmail { get; private set; } = "";
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool Active { get; private set; }

    public void Accept(Guid userId, DateTimeOffset now)
    {
        AcceptedAt = now;
        AcceptedByUserId = userId;
        Active = false;
    }

    public void Revoke(DateTimeOffset now)
    {
        RevokedAt = now;
        Active = false;
    }
}
