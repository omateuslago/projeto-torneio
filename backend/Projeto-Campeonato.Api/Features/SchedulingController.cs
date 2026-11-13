using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record ScheduleSettings(int MatchDurationMinutes, string TimeZoneId);
public sealed record VenueRequest([Required, StringLength(120)] string Name, [Required, StringLength(300)] string Address);
public sealed record ResourceRequest([Required, StringLength(120)] string Name);
public sealed record AvailabilityRequest([Required] string StartsAt, [Required] string EndsAt);
public sealed record ScheduleRequest([Required] string StartsAt, Guid PlayingAreaId, Guid? RefereeId);
public sealed record VenueDto(Guid Id, string Name, string Address, bool Active);
public sealed record AreaDto(Guid Id, Guid VenueId, string Name, bool Active);
public sealed record RefereeDto(Guid Id, string Name, bool Active);
public sealed record AvailabilityDto(Guid Id, DateTime StartsAt, DateTime EndsAt);
public sealed record ScheduleDto(Guid MatchId, Guid HomeTeamId, string HomeTeam, Guid AwayTeamId, string AwayTeam,
    DateTime? StartsAt, DateTime? EndsAt, Guid? PlayingAreaId, string? PlayingArea, string? Venue, Guid? RefereeId, string? Referee, MatchStatus MatchStatus);

[ApiController]
[Route("api/tournaments/{tournamentId:guid}")]
public sealed class SchedulingController(TournamentDbContext db, IAccessControl access,
    INotificationOutbox notifications) : ControllerBase
{
    // All scheduling writes use this transaction-scoped PostgreSQL lock, including
    // availability edits. Global scope also protects teams shared across tournaments.
    private async Task Lock(CancellationToken ct) => await ScheduleWriter.Lock(db, ct);
    private async Task<Tournament?> Tournament(Guid id, CancellationToken ct) => await db.Tournaments
        .FromSqlInterpolated($"SELECT * FROM \"Tournaments\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    [HttpGet("schedule-settings")]
    public async Task<ActionResult<ScheduleSettings>> Settings(Guid tournamentId, CancellationToken ct)
    {
        var t = await db.Tournaments.FindAsync([tournamentId], ct);
        return t is null ? NotFound() : Ok(new ScheduleSettings(t.MatchDurationMinutes, t.TimeZoneId));
    }
    [HttpPut("schedule-settings")]
    [Authorize]
    public async Task<ActionResult<ScheduleSettings>> Settings(Guid tournamentId, ScheduleSettings request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, tournamentId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var t = await Tournament(tournamentId, ct); if (t is null) return NotFound();
        t.ConfigureSchedule(request.MatchDurationMinutes, request.TimeZoneId);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new ScheduleSettings(t.MatchDurationMinutes, t.TimeZoneId));
    }
    [HttpGet("venues")]
    public async Task<ActionResult<IReadOnlyList<VenueDto>>> Venues(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        return Ok(await db.Venues.Where(x => x.TournamentId == tournamentId).OrderBy(x => x.Name).Select(x => new VenueDto(x.Id, x.Name, x.Address, x.Active)).ToListAsync(ct));
    }
    [HttpPost("venues")]
    [Authorize]
    public async Task<ActionResult<VenueDto>> Venue(Guid tournamentId, VenueRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, tournamentId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        var v = new Venue(tournamentId, request.Name, request.Address); db.Venues.Add(v); await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Created($"/api/tournaments/{tournamentId}/venues", new VenueDto(v.Id, v.Name, v.Address, v.Active));
    }
    [HttpGet("playing-areas")]
    public async Task<ActionResult<IReadOnlyList<AreaDto>>> Areas(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        return Ok(await db.PlayingAreas.Where(x => x.TournamentId == tournamentId).OrderBy(x => x.Name).Select(x => new AreaDto(x.Id, x.VenueId, x.Name, x.Active)).ToListAsync(ct));
    }
    [HttpPost("venues/{venueId:guid}/playing-areas")]
    [Authorize]
    public async Task<ActionResult<AreaDto>> Area(Guid tournamentId, Guid venueId, ResourceRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, tournamentId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        if (!await db.Venues.AnyAsync(x => x.Id == venueId && x.TournamentId == tournamentId && x.Active, ct)) return NotFound();
        var a = new PlayingArea(tournamentId, venueId, request.Name); db.PlayingAreas.Add(a); await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Created($"/api/tournaments/{tournamentId}/playing-areas", new AreaDto(a.Id, a.VenueId, a.Name, a.Active));
    }
    [HttpGet("referees")]
    public async Task<ActionResult<IReadOnlyList<RefereeDto>>> Referees(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        return Ok(await db.Referees.Where(x => x.TournamentId == tournamentId).OrderBy(x => x.Name).Select(x => new RefereeDto(x.Id, x.Name, x.Active)).ToListAsync(ct));
    }
    [HttpPost("referees")]
    [Authorize]
    public async Task<ActionResult<RefereeDto>> Referee(Guid tournamentId, ResourceRequest request, CancellationToken ct)
    {
        if (!await access.CanManageTournament(User, tournamentId, ct)) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        var r = new Referee(tournamentId, request.Name); db.Referees.Add(r); await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Created($"/api/tournaments/{tournamentId}/referees", new RefereeDto(r.Id, r.Name, r.Active));
    }
    private IQueryable<Availability> Windows(Guid tournament, string kind, Guid resource) => db.Availabilities.Where(x => x.TournamentId == tournament &&
        (kind == "teams" && x.TeamId == resource || kind == "playing-areas" && x.PlayingAreaId == resource || kind == "referees" && x.RefereeId == resource));
    private async Task<bool> Exists(Guid t, string kind, Guid id, CancellationToken ct) => kind switch
    {
        "teams" => await db.Registrations.AnyAsync(x => x.TournamentId == t && x.TeamId == id, ct),
        "playing-areas" => await db.PlayingAreas.AnyAsync(x => x.TournamentId == t && x.Id == id && x.Active, ct),
        "referees" => await db.Referees.AnyAsync(x => x.TournamentId == t && x.Id == id && x.Active, ct),
        _ => false
    };
    [HttpGet("{kind}/{resourceId:guid}/availability")]
    public async Task<ActionResult<IReadOnlyList<AvailabilityDto>>> Availability(Guid tournamentId, string kind, Guid resourceId, CancellationToken ct)
    {
        if (!await Exists(tournamentId, kind, resourceId, ct)) return NotFound();
        return Ok(await Windows(tournamentId, kind, resourceId).OrderBy(x => x.StartsAt).Select(x => new AvailabilityDto(x.Id, x.StartsAt, x.EndsAt)).ToListAsync(ct));
    }
    [HttpPost("{kind}/{resourceId:guid}/availability")]
    [Authorize]
    public Task<ActionResult<AvailabilityDto>> AddAvailability(Guid tournamentId, string kind, Guid resourceId, AvailabilityRequest request, CancellationToken ct) =>
        SaveAvailability(tournamentId, kind, resourceId, null, request, ct);
    [HttpPut("{kind}/{resourceId:guid}/availability/{id:guid}")]
    [Authorize]
    public Task<ActionResult<AvailabilityDto>> EditAvailability(Guid tournamentId, string kind, Guid resourceId, Guid id, AvailabilityRequest request, CancellationToken ct) =>
        SaveAvailability(tournamentId, kind, resourceId, id, request, ct);
    private async Task<ActionResult<AvailabilityDto>> SaveAvailability(Guid t, string kind, Guid resource, Guid? id, AvailabilityRequest request, CancellationToken ct)
    {
        var permitted = kind == "teams"
            ? await access.CanManageTournament(User, t, ct) || await access.CanManageTeam(User, resource, ct)
            : await access.CanManageTournament(User, t, ct);
        if (!permitted) return Forbid();
        var start = ScheduleTime.Parse(request.StartsAt); var end = ScheduleTime.Parse(request.EndsAt); ScheduleTime.Validate(start, end);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        if (!await Exists(t, kind, resource, ct)) return NotFound();
        var windows = await Windows(t, kind, resource).ToListAsync(ct);
        var item = windows.SingleOrDefault(x => x.Id == id);
        if (id.HasValue && item is null) return NotFound();
        if (windows.Any(x => x.Id != id && x.StartsAt == start && x.EndsAt == end)) return Problem(statusCode: 409, detail: "Disponibilidade idêntica já cadastrada.");
        if (item is null) { item = new Availability(t, kind, resource, start, end); db.Availabilities.Add(item); windows.Add(item); }
        else item.Update(start, end);
        await EnsureExistingSchedules(t, kind, resource, windows, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var dto = new AvailabilityDto(item.Id, item.StartsAt, item.EndsAt);
        return id.HasValue ? Ok(dto) : Created($"/api/tournaments/{t}/{kind}/{resource}/availability", dto);
    }
    [HttpDelete("{kind}/{resourceId:guid}/availability/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteAvailability(Guid tournamentId, string kind, Guid resourceId, Guid id, CancellationToken ct)
    {
        var permitted = kind == "teams"
            ? await access.CanManageTournament(User, tournamentId, ct) || await access.CanManageTeam(User, resourceId, ct)
            : await access.CanManageTournament(User, tournamentId, ct);
        if (!permitted) return Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var windows = await Windows(tournamentId, kind, resourceId).ToListAsync(ct);
        var item = windows.SingleOrDefault(x => x.Id == id); if (item is null) return NotFound();
        windows.Remove(item); await EnsureExistingSchedules(tournamentId, kind, resourceId, windows, ct);
        db.Availabilities.Remove(item); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    private async Task EnsureExistingSchedules(Guid t, string kind, Guid resource, List<Availability> windows, CancellationToken ct)
    {
        var matches = await db.Matches.Where(m => m.TournamentId == t && m.StartsAt != null &&
            (kind == "teams" && (m.HomeTeamId == resource || m.AwayTeamId == resource) || kind == "playing-areas" && m.PlayingAreaId == resource || kind == "referees" && m.RefereeId == resource)).ToListAsync(ct);
        if (matches.Any(m => !windows.Any(w => ScheduleTime.Contains(w.StartsAt, w.EndsAt, m.StartsAt!.Value, m.EndsAt!.Value))))
            throw new DomainConflictException("Alteração deixaria uma partida agendada sem disponibilidade. Reagende a partida primeiro.");
    }
    private IQueryable<ScheduleDto> Agenda(Guid t, Guid? matchId = null) =>
        from m in db.Matches
        where m.TournamentId == t && (matchId == null || m.Id == matchId)
        join h in db.Teams on m.HomeTeamId equals h.Id
        join a in db.Teams on m.AwayTeamId equals a.Id
        join p in db.PlayingAreas on m.PlayingAreaId equals p.Id into ps
        from p in ps.DefaultIfEmpty()
        join v in db.Venues on p.VenueId equals v.Id into vs
        from v in vs.DefaultIfEmpty()
        join r in db.Referees on m.RefereeId equals r.Id into rs
        from r in rs.DefaultIfEmpty()
        orderby m.StartsAt == null, m.StartsAt, m.Id
        select new ScheduleDto(m.Id, h.Id, h.Name, a.Id, a.Name, m.StartsAt, m.EndsAt, m.PlayingAreaId, p == null ? null : p.Name, v == null ? null : v.Name, m.RefereeId, r == null ? null : r.Name, m.Status);
    [HttpGet("schedule")]
    public async Task<ActionResult<IReadOnlyList<ScheduleDto>>> Schedule(Guid tournamentId, CancellationToken ct)
    {
        if (!await db.Tournaments.AnyAsync(x => x.Id == tournamentId, ct)) return NotFound();
        return Ok(await Agenda(tournamentId).ToListAsync(ct));
    }
    [HttpPut("/api/matches/{matchId:guid}/schedule")]
    [Authorize]
    public async Task<ActionResult<ScheduleDto>> ScheduleMatch(Guid matchId, ScheduleRequest request, CancellationToken ct)
    {
        var start = ScheduleTime.Parse(request.StartsAt);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var tId = await db.Matches.Where(x => x.Id == matchId).Select(x => (Guid?)x.TournamentId).SingleOrDefaultAsync(ct);
        if (tId is null) return NotFound();
        if (!await access.CanManageTournament(User, tId.Value, ct)) return Forbid();
        var t = (await Tournament(tId.Value, ct))!;
        var m = await db.Matches.SingleAsync(x => x.Id == matchId, ct);
        var rescheduled = m.StartsAt.HasValue;
        await new ScheduleWriter(db).Stage(m, t, request, true, ct);
        await notifications.MatchScheduled(m.Id, rescheduled, ct);
        await db.SaveChangesAsync(ct);
        var result = await Agenda(t.Id, matchId).SingleAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(result);
    }
}
