using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Infrastructure;

public sealed class TournamentDbContext(DbContextOptions<TournamentDbContext> options) : DbContext(options)
{
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<Match> Matches => Set<Match>();
    public DbSet<ScoreSubmission> ScoreSubmissions => Set<ScoreSubmission>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<PlayingArea> PlayingAreas => Set<PlayingArea>();
    public DbSet<Referee> Referees => Set<Referee>();
    public DbSet<Availability> Availabilities => Set<Availability>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        SchedulingMapping.Configure(model);
        model.Entity<Tournament>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Sport).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Format).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.ToTable("Tournaments", table => table.HasCheckConstraint("CK_Tournaments_MaxTeams", "\"MaxTeams\" BETWEEN 2 AND 128"));
        });
        model.Entity<Team>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        });
        model.Entity<Match>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ResultSource).HasMaxLength(32);
            entity.HasIndex(x => new { x.TournamentId, x.HomeTeamId, x.AwayTeamId }).IsUnique();
            entity.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Registration>().WithMany().HasForeignKey(x => new { x.TournamentId, x.HomeTeamId })
                .HasPrincipalKey(x => new { x.TournamentId, x.TeamId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Registration>().WithMany().HasForeignKey(x => new { x.TournamentId, x.AwayTeamId })
                .HasPrincipalKey(x => new { x.TournamentId, x.TeamId }).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable("Matches", table =>
            {
                table.HasCheckConstraint("CK_Matches_Teams", "\"HomeTeamId\" < \"AwayTeamId\"");
                table.HasCheckConstraint("CK_Matches_Result", "(\"Status\" IN ('Pending', 'AwaitingConfirmation', 'Disputed') AND \"HomeScore\" IS NULL AND \"AwayScore\" IS NULL AND \"FinishedAt\" IS NULL) OR (\"Status\" = 'Finished' AND \"HomeScore\" IS NOT NULL AND \"AwayScore\" IS NOT NULL AND \"HomeScore\" >= 0 AND \"AwayScore\" >= 0 AND \"FinishedAt\" IS NOT NULL)");
            });
        });
        model.Entity<ScoreSubmission>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.MatchId, x.SubmittedByTeamId }).IsUnique();
            entity.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Team>().WithMany().HasForeignKey(x => x.SubmittedByTeamId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable("ScoreSubmissions", t => t.HasCheckConstraint("CK_Submission_Scores", "\"HomeScore\" >= 0 AND \"AwayScore\" >= 0"));
        });
        model.Entity<Dispute>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ResolutionNote).HasMaxLength(2000);
            entity.HasIndex(x => x.MatchId).IsUnique().HasFilter("\"Status\" = 'Open'");
        });
        model.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.EventType).HasMaxLength(64);
            entity.Property(x => x.Data).HasColumnType("text");
            entity.HasIndex(x => new { x.MatchId, x.OccurredAt });
        });
        model.Entity<Registration>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.TournamentId, x.TeamId });
            entity.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
