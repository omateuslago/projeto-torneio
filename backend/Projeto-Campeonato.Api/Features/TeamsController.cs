using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

[ApiController]
[Route("api/teams")]
public sealed class TeamsController(TournamentDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamResponse>>> List(CancellationToken ct) =>
        Ok(await db.Teams.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new TeamResponse(x.Id, x.Name)).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TeamResponse>> Get(Guid id, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return team is null ? NotFound() : Ok(new TeamResponse(team.Id, team.Name));
    }

    [HttpPost]
    public async Task<ActionResult<TeamResponse>> Create(CreateTeamRequest request, CancellationToken ct)
    {
        var team = new Team(request.Name);
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = team.Id }, new TeamResponse(team.Id, team.Name));
    }
}
