using System.ComponentModel.DataAnnotations;
namespace Projeto_Campeonato.Api.Features;

public sealed record AutopilotOptions([Required] string HorizonStart, [Required] string HorizonEnd,
    [Range(1, 60)] int GranularityMinutes = 15, bool RequireReferee = false);
public sealed record SolverWindow(DateTime StartsAt, DateTime EndsAt);
public sealed record SolverResource(Guid Id, string Name, SolverWindow[] Availability);
public sealed record SolverMatch(Guid Id, Guid HomeTeamId, Guid AwayTeamId);
public sealed record FixedSchedule(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, DateTime StartsAt, DateTime EndsAt, Guid PlayingAreaId, Guid? RefereeId);
public sealed record SolverInput(Guid TournamentId, DateTime HorizonStart, DateTime HorizonEnd, int MatchDurationMinutes,
    int GranularityMinutes, bool RequireReferee, SolverMatch[] Matches, SolverResource[] Teams, SolverResource[] PlayingAreas,
    SolverResource[] Referees, FixedSchedule[] ExistingSchedule);
public sealed record SolverAssignment(Guid MatchId, string StartsAt, Guid PlayingAreaId, Guid? RefereeId);
public sealed record SolverResult(string Status, SolverAssignment[] ScheduledMatches, string[] Warnings);
public sealed record ScheduleMetrics(int ScheduledCount, int PreservedCount, DateTime? StartsAt, DateTime? EndsAt, int PlayingAreasUsed, int RefereesUsed);
public sealed record ScheduleProposal(string Status, SolverAssignment[] ScheduledMatches, string[] Warnings,
    ScheduleMetrics Metrics, string? ProposalToken, DateTime? ExpiresAt);
public sealed record ApplyProposalRequest([Required, StringLength(1000000)] string ProposalToken);
public sealed record ProtectedProposal(Guid TournamentId, AutopilotOptions Options, string SnapshotHash, SolverAssignment[] Assignments, DateTime ExpiresAt);
