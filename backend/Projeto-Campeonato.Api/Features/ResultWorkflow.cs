using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

// Caller owns the transaction and must lock Tournament before reading mutable match state.
public sealed class ResultWorkflow(TournamentDbContext db)
{
    public void Audit(Match match, string type, object data, Guid? teamId = null) =>
        db.AuditLogs.Add(new AuditLog(match.Id, type, JsonSerializer.Serialize(data), teamId));

    public async Task ApplyOfficial(Match match, Tournament tournament, int home, int away, string source, string note, CancellationToken ct)
    {
        var previous = new { match.HomeScore, match.AwayScore, match.ResultSource };
        var dispute = await db.Disputes.SingleOrDefaultAsync(x => x.MatchId == match.Id && x.Status == DisputeStatus.Open, ct);
        if (dispute is not null)
        {
            dispute.Resolve(note);
            Audit(match, "DisputeResolved", new { dispute.Id, note, home, away, source });
        }
        match.RecordResult(home, away, source);
        Audit(match, source == "Bilateral" ? "AutomaticallyConfirmed" : source == "Manual" ? "AdministrativeResult" : "OfficialResult", new { previous, home, away, source });
        await db.SaveChangesAsync(ct);
        if (!await db.Matches.AnyAsync(x => x.TournamentId == tournament.Id && x.Status != MatchStatus.Finished, ct)
            && !await db.Disputes.AnyAsync(x => x.Status == DisputeStatus.Open && db.Matches.Any(m => m.Id == x.MatchId && m.TournamentId == tournament.Id), ct))
        {
            tournament.FinishCompetition();
            await db.SaveChangesAsync(ct);
        }
    }
}
