using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
namespace Projeto_Campeonato.Api.Infrastructure;

internal static class SchedulingMapping
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Tournament>().Property(x => x.MatchDurationMinutes).HasDefaultValue(60);
        model.Entity<Tournament>().Property(x => x.TimeZoneId).HasMaxLength(100).HasDefaultValue("UTC");
        model.Entity<Tournament>().ToTable(t => t.HasCheckConstraint("CK_Tournament_Duration", "\"MatchDurationMinutes\" BETWEEN 5 AND 480"));
        model.Entity<Venue>(e =>
        {
            e.HasAlternateKey(x => new { x.TournamentId, x.Id });
            e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Address).HasMaxLength(300);
            e.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PlayingArea>(e =>
        {
            e.HasAlternateKey(x => new { x.TournamentId, x.Id }); e.Property(x => x.Name).HasMaxLength(120);
            e.HasOne<Venue>().WithMany().HasForeignKey(x => new { x.TournamentId, x.VenueId }).HasPrincipalKey(x => new { x.TournamentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Referee>(e =>
        {
            e.HasAlternateKey(x => new { x.TournamentId, x.Id }); e.Property(x => x.Name).HasMaxLength(120);
            e.HasOne<Tournament>().WithMany().HasForeignKey(x => x.TournamentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Availability>(e =>
        {
            e.HasOne<Registration>().WithMany().HasForeignKey(x => new { x.TournamentId, x.TeamId }).HasPrincipalKey(x => new { x.TournamentId, x.TeamId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PlayingArea>().WithMany().HasForeignKey(x => new { x.TournamentId, x.PlayingAreaId }).HasPrincipalKey(x => new { x.TournamentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Referee>().WithMany().HasForeignKey(x => new { x.TournamentId, x.RefereeId }).HasPrincipalKey(x => new { x.TournamentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TournamentId, x.TeamId, x.StartsAt, x.EndsAt }).IsUnique();
            e.HasIndex(x => new { x.TournamentId, x.PlayingAreaId, x.StartsAt, x.EndsAt }).IsUnique();
            e.HasIndex(x => new { x.TournamentId, x.RefereeId, x.StartsAt, x.EndsAt }).IsUnique();
            e.ToTable(t => { t.HasCheckConstraint("CK_Availability_Target", "num_nonnulls(\"TeamId\", \"PlayingAreaId\", \"RefereeId\") = 1"); t.HasCheckConstraint("CK_Availability_Time", "\"StartsAt\" < \"EndsAt\""); });
        });
        model.Entity<Match>(e =>
        {
            e.HasOne<PlayingArea>().WithMany().HasForeignKey(x => new { x.TournamentId, x.PlayingAreaId }).HasPrincipalKey(x => new { x.TournamentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Referee>().WithMany().HasForeignKey(x => new { x.TournamentId, x.RefereeId }).HasPrincipalKey(x => new { x.TournamentId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StartsAt);
            e.ToTable(t => t.HasCheckConstraint("CK_Match_Schedule", "(\"StartsAt\" IS NULL AND \"EndsAt\" IS NULL AND \"PlayingAreaId\" IS NULL AND \"RefereeId\" IS NULL) OR (\"StartsAt\" IS NOT NULL AND \"EndsAt\" IS NOT NULL AND \"PlayingAreaId\" IS NOT NULL AND \"StartsAt\" < \"EndsAt\")"));
        });
    }
}
