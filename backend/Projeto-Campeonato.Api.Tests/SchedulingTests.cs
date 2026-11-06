using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Features;
namespace Projeto_Campeonato.Api.Tests;

public sealed class SchedulingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static async Task<T> Read<T>(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<T>(Json))!; }
    private sealed record Setup(Guid T, MatchResponse[] Matches, AreaDto Area, AreaDto OtherArea, RefereeDto Referee);
    private async Task<Setup> Create(Guid? sharedTeam = null)
    {
        var t = await Read<TournamentResponse>(await client.PostAsJsonAsync("/api/tournaments", new { name = "Agenda", sport = "Futsal", maxTeams = 4, format = "RoundRobin" }));
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Open" })).EnsureSuccessStatusCode();
        for (var i = 0; i < 4; i++)
        {
            var team = i == 0 && sharedTeam.HasValue ? new TeamResponse(sharedTeam.Value, "Compartilhada") : await Read<TeamResponse>(await client.PostAsJsonAsync("/api/teams", new { name = $"Equipe {i}" }));
            (await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/registrations", new { teamId = team.Id })).EnsureSuccessStatusCode();
            (await Window(t.Id, "teams", team.Id)).EnsureSuccessStatusCode();
        }
        (await client.PutAsJsonAsync($"/api/tournaments/{t.Id}/status", new { status = "Closed" })).EnsureSuccessStatusCode();
        var matches = await Read<MatchResponse[]>(await client.PostAsync($"/api/tournaments/{t.Id}/matches/generate", null));
        var v = await Read<VenueDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/venues", new { name = "Ginásio", address = "Rua 1" }));
        var a = await Read<AreaDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/venues/{v.Id}/playing-areas", new { name = "Quadra 1" }));
        var b = await Read<AreaDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/venues/{v.Id}/playing-areas", new { name = "Quadra 2" }));
        var r = await Read<RefereeDto>(await client.PostAsJsonAsync($"/api/tournaments/{t.Id}/referees", new { name = "Carlos" }));
        foreach (var id in new[] { a.Id, b.Id }) (await Window(t.Id, "playing-areas", id)).EnsureSuccessStatusCode();
        (await Window(t.Id, "referees", r.Id)).EnsureSuccessStatusCode();
        return new(t.Id, matches, a, b, r);
    }
    private string Url(Guid t, string kind, Guid id) => $"/api/tournaments/{t}/{kind}/{id}/availability";
    private Task<HttpResponseMessage> Window(Guid t, string kind, Guid id, string start = "2026-11-14T08:00:00-03:00", string end = "2026-11-14T22:00:00-03:00") => client.PostAsJsonAsync(Url(t, kind, id), new { startsAt = start, endsAt = end });
    private Task<HttpResponseMessage> Put(MatchResponse m, Guid area, Guid? referee = null, string start = "2026-11-14T18:00:00-03:00") => client.PutAsJsonAsync($"/api/matches/{m.Id}/schedule", new { startsAt = start, playingAreaId = area, refereeId = referee });
    private async Task<ScheduleDto[]> Agenda(Guid t) => await Read<ScheduleDto[]>(await client.GetAsync($"/api/tournaments/{t}/schedule"));

    [Theory]
    [InlineData("2026-11-14T19:00:00Z", false)]
    [InlineData("2026-11-14T18:30:00Z", true)]
    [InlineData("2026-11-14T17:00:00Z", false)]
    public void HalfOpenIntervals(string start, bool overlaps) => Assert.Equal(overlaps, ScheduleTime.Overlaps(ScheduleTime.Parse("2026-11-14T18:00:00Z"), ScheduleTime.Parse("2026-11-14T19:00:00Z"), ScheduleTime.Parse(start), ScheduleTime.Parse(start).AddHours(1)));

    [Fact]
    public async Task AvailabilityCrudDuplicateAndRegistration()
    {
        var s = await Create(); var url = Url(s.T, "teams", s.Matches[0].HomeTeamId);
        Assert.Equal(HttpStatusCode.Conflict, (await Window(s.T, "teams", s.Matches[0].HomeTeamId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Window(s.T, "teams", Guid.NewGuid())).StatusCode);
        var w = Assert.Single(await Read<AvailabilityDto[]>(await client.GetAsync(url)));
        (await client.PutAsJsonAsync($"{url}/{w.Id}", new { startsAt = "2026-11-15T08:00:00Z", endsAt = "2026-11-15T20:00:00Z" })).EnsureSuccessStatusCode();
        Assert.Equal(15, Assert.Single(await Read<AvailabilityDto[]>(await client.GetAsync(url))).StartsAt.Day);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{url}/{w.Id}")).StatusCode);
        Assert.Empty(await Read<AvailabilityDto[]>(await client.GetAsync(url)));
    }
    [Theory]
    [InlineData("2026-11-14T20:00:00Z", "2026-11-14T19:00:00Z")]
    [InlineData("2026-11-14T20:00:00Z", "2026-11-14T20:00:00Z")]
    [InlineData("2026-11-14T18:00:00", "2026-11-14T20:00:00Z")]
    public async Task InvalidIntervals(string start, string end) { var s = await Create(); Assert.Equal(HttpStatusCode.BadRequest, (await Window(s.T, "teams", s.Matches[0].HomeTeamId, start, end)).StatusCode); }
    [Fact]
    public async Task ValidScheduleUtcDurationRescheduleAuditAndPersistence()
    {
        var s = await Create();
        (await client.PutAsJsonAsync($"/api/tournaments/{s.T}/schedule-settings", new { matchDurationMinutes = 50, timeZoneId = "America/Sao_Paulo" })).EnsureSuccessStatusCode();
        var result = await Read<ScheduleDto>(await Put(s.Matches[0], s.Area.Id, s.Referee.Id));
        Assert.Equal(21, result.StartsAt!.Value.Hour); Assert.Equal(TimeSpan.FromMinutes(50), result.EndsAt - result.StartsAt);
        await Read<ScheduleDto>(await Put(s.Matches[0], s.OtherArea.Id, null, "2026-11-14T19:00:00-03:00"));
        var saved = (await Agenda(s.T)).First(); Assert.Equal(s.OtherArea.Id, saved.PlayingAreaId); Assert.Null(saved.RefereeId);
        var audit = await Read<AuditResponse[]>(await client.GetAsync($"/api/matches/{s.Matches[0].Id}/audit-log"));
        Assert.Contains(audit, x => x.EventType == "MatchScheduled"); Assert.Contains(audit, x => x.EventType == "MatchRescheduled");
        Assert.Equal(5, (await Agenda(s.T)).Count(x => x.StartsAt is null));
    }
    [Theory]
    [InlineData("home")]
    [InlineData("away")]
    [InlineData("area")]
    [InlineData("referee")]
    public async Task EntireMatchMustFitEachAvailability(string target)
    {
        var s = await Create(); var m = s.Matches[0]; var kind = target is "home" or "away" ? "teams" : target == "area" ? "playing-areas" : "referees";
        var id = target == "home" ? m.HomeTeamId : target == "away" ? m.AwayTeamId : target == "area" ? s.Area.Id : s.Referee.Id;
        var w = Assert.Single(await Read<AvailabilityDto[]>(await client.GetAsync(Url(s.T, kind, id))));
        (await client.PutAsJsonAsync($"{Url(s.T, kind, id)}/{w.Id}", new { startsAt = "2026-11-14T18:00:00-03:00", endsAt = "2026-11-14T18:30:00-03:00" })).EnsureSuccessStatusCode();
        var response = await Put(m, s.Area.Id, s.Referee.Id); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("disponibilidade", await response.Content.ReadAsStringAsync());
        Assert.All(await Agenda(s.T), x => Assert.Null(x.StartsAt));
    }
    [Theory]
    [InlineData("team")]
    [InlineData("area")]
    [InlineData("referee")]
    public async Task ResourceConflictsAndAdjacentMatches(string resource)
    {
        var s = await Create(); var first = s.Matches[0];
        var next = resource == "team" ? s.Matches.First(x => x.Id != first.Id && (x.HomeTeamId == first.HomeTeamId || x.AwayTeamId == first.HomeTeamId)) :
            s.Matches.Single(x => x.HomeTeamId != first.HomeTeamId && x.AwayTeamId != first.HomeTeamId && x.HomeTeamId != first.AwayTeamId && x.AwayTeamId != first.AwayTeamId);
        (await Put(first, s.Area.Id, s.Referee.Id)).EnsureSuccessStatusCode();
        var area = resource == "area" ? s.Area.Id : s.OtherArea.Id; Guid? referee = resource == "referee" ? s.Referee.Id : null;
        Assert.Equal(HttpStatusCode.Conflict, (await Put(next, area, referee, "2026-11-14T18:30:00-03:00")).StatusCode);
        (await Put(next, area, referee, "2026-11-14T19:00:00-03:00")).EnsureSuccessStatusCode();
        Assert.Equal(2, (await Agenda(s.T)).Count(x => x.StartsAt != null));
    }
    [Fact]
    public async Task InvalidRescheduleAndAvailabilityRemovalPreserveSchedule()
    {
        var s = await Create(); var m = s.Matches[0]; var before = await Read<ScheduleDto>(await Put(m, s.Area.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await Put(m, s.Area.Id, null, "2026-11-14T23:00:00-03:00")).StatusCode);
        var url = Url(s.T, "playing-areas", s.Area.Id); var w = Assert.Single(await Read<AvailabilityDto[]>(await client.GetAsync(url)));
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"{url}/{w.Id}")).StatusCode);
        Assert.Equal(before, (await Agenda(s.T)).First());
    }
    [Fact]
    public async Task ConcurrentReservationsHaveOnlyOneWinner()
    {
        var s = await Create(); var m = s.Matches[0]; var n = s.Matches.Single(x => x.HomeTeamId != m.HomeTeamId && x.AwayTeamId != m.HomeTeamId && x.HomeTeamId != m.AwayTeamId && x.AwayTeamId != m.AwayTeamId);
        var results = await Task.WhenAll(Put(m, s.Area.Id), Put(n, s.Area.Id));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(await Agenda(s.T), x => x.StartsAt != null);
    }
    [Fact]
    public async Task ForeignResourcesAndFinishedMatchRejected()
    {
        var s = await Create(); var other = await Create();
        Assert.Equal(HttpStatusCode.Conflict, (await Put(s.Matches[0], other.Area.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(s.Matches[0], s.Area.Id, other.Referee.Id)).StatusCode);
        (await client.PutAsJsonAsync($"/api/matches/{s.Matches[0].Id}/result", new { homeScore = 1, awayScore = 0 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Put(s.Matches[0], s.Area.Id)).StatusCode);
    }
    [Theory]
    [InlineData(0, "UTC")]
    [InlineData(481, "UTC")]
    [InlineData(50, "Unknown/Zone")]
    public async Task InvalidSettings(int duration, string zone) { var s = await Create(); Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/tournaments/{s.T}/schedule-settings", new { matchDurationMinutes = duration, timeZoneId = zone })).StatusCode); }
    [Fact]
    public async Task SwaggerHasScheduleContracts()
    {
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/matches/{matchId}/schedule", json); Assert.Contains("ScheduleDto", json); Assert.Contains("AvailabilityDto", json);
    }
    [Fact]
    public async Task SharedTeamCannotPlayAcrossTournamentsAtSameTime()
    {
        var first = await Create(); var m = first.Matches[0]; var second = await Create(m.HomeTeamId);
        var next = second.Matches.First(x => x.HomeTeamId == m.HomeTeamId || x.AwayTeamId == m.HomeTeamId);
        var results = await Task.WhenAll(Put(m, first.Area.Id), Put(next, second.Area.Id));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task AvailabilityDeletionRacingReservationCannotLeaveInvalidSchedule()
    {
        var s = await Create(); var url = Url(s.T, "playing-areas", s.Area.Id);
        var w = Assert.Single(await Read<AvailabilityDto[]>(await client.GetAsync(url)));
        var results = await Task.WhenAll(Put(s.Matches[0], s.Area.Id), client.DeleteAsync($"{url}/{w.Id}"));
        Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var windows = await Read<AvailabilityDto[]>(await client.GetAsync(url));
        var scheduled = (await Agenda(s.T)).Where(x => x.StartsAt != null).ToArray();
        Assert.Equal(windows.Length, scheduled.Length);
    }
    [Fact]
    public async Task EquivalentOffsetsAreDuplicatesAndSettingsKeepExistingEnd()
    {
        var s = await Create();
        Assert.Equal(HttpStatusCode.Conflict, (await Window(s.T, "playing-areas", s.Area.Id, "2026-11-14T11:00:00Z", "2026-11-15T01:00:00Z")).StatusCode);
        var saved = await Read<ScheduleDto>(await Put(s.Matches[0], s.Area.Id));
        (await client.PutAsJsonAsync($"/api/tournaments/{s.T}/schedule-settings", new { matchDurationMinutes = 90, timeZoneId = "UTC" })).EnsureSuccessStatusCode();
        Assert.Equal(saved.EndsAt, (await Agenda(s.T)).First().EndsAt);
    }
}
