using System.ComponentModel.DataAnnotations;
namespace Projeto_Campeonato.Api.Features;

public sealed record AutopilotOptions([Required] string HorizonStart, [Required] string HorizonEnd,
    [Range(1, 60)] int GranularityMinutes = 15, bool RequireReferee = false,
    [Range(0, 240)] int PreferredRestMinutes = 60);
public sealed record SolverWindow(DateTime StartsAt, DateTime EndsAt);
public sealed record SolverResource(Guid Id, string Name, SolverWindow[] Availability);
public sealed record SolverMatch(Guid Id, Guid HomeTeamId, Guid AwayTeamId);
public sealed record FixedSchedule(Guid MatchId, Guid HomeTeamId, Guid AwayTeamId, DateTime StartsAt, DateTime EndsAt, Guid PlayingAreaId, Guid? RefereeId, bool CountsForFairness = false);
public sealed record SolverInput(Guid TournamentId, DateTime HorizonStart, DateTime HorizonEnd, int MatchDurationMinutes,
    int GranularityMinutes, bool RequireReferee, SolverMatch[] Matches, SolverResource[] Teams, SolverResource[] PlayingAreas,
    SolverResource[] Referees, FixedSchedule[] ExistingSchedule, int PreferredRestMinutes = 60);
public sealed record SolverAssignment(Guid MatchId, string StartsAt, Guid PlayingAreaId, Guid? RefereeId);
public sealed record ScheduleFairness(
    [property: System.Text.Json.Serialization.JsonRequired] int Score,
    [property: System.Text.Json.Serialization.JsonRequired] int RestScore,
    [property: System.Text.Json.Serialization.JsonRequired] int BackToBackScore,
    [property: System.Text.Json.Serialization.JsonRequired] int TimeBalanceScore,
    [property: System.Text.Json.Serialization.JsonRequired] string[] Observations)
{
    public bool IsValid() => Score is >= 0 and <= 100 && RestScore is >= 0 and <= 100
        && BackToBackScore is >= 0 and <= 100 && TimeBalanceScore is >= 0 and <= 100
        && Observations is { Length: > 0 and <= 300 }
        && Observations.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 500);
}
public sealed record SolverResult(string Status, SolverAssignment[] ScheduledMatches, string[] Warnings, ScheduleFairness? Fairness = null);
public sealed record ScheduleMetrics(int ScheduledCount, int PreservedCount, DateTime? StartsAt, DateTime? EndsAt, int PlayingAreasUsed, int RefereesUsed);
public sealed record ScheduleProposal(string Status, SolverAssignment[] ScheduledMatches, string[] Warnings,
    ScheduleMetrics Metrics, string? ProposalToken, DateTime? ExpiresAt, ScheduleFairness? Fairness = null);
public sealed record ApplyProposalRequest([Required, StringLength(1000000)] string ProposalToken);
public sealed record ProtectedProposal(Guid TournamentId, AutopilotOptions Options, string SnapshotHash, SolverAssignment[] Assignments, DateTime ExpiresAt);
