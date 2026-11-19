using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projeto_Campeonato.Api.Domain;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Features;

public sealed record RegisterRequest([Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(200, MinimumLength = 8)] string Password,
    [Required, StringLength(120, MinimumLength = 1)] string DisplayName);
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password, bool RememberMe = false);
public sealed record CurrentUserResponse(bool Authenticated, Guid? Id, string? Email, string? DisplayName,
    bool IsPlatformAdmin, Guid[] OrganizerTournamentIds, Guid[] ManagedTeamIds);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    TournamentDbContext db, IAntiforgery antiforgery, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = !environment.IsDevelopment() || Request.IsHttps,
            Path = "/"
        });
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("register")]
    public async Task<ActionResult<CurrentUserResponse>> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim()
        };
        if (user.DisplayName.Length == 0) return ValidationProblem("Informe o nome.");
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
                { ["registration"] = result.Errors.Select(x => x.Description).ToArray() }));
        await signIn.SignInAsync(user, isPersistent: false);
        return Ok(await Current(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<CurrentUserResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return Problem(statusCode: 401, detail: "Email ou senha inválidos.");
        await signIn.SignInAsync(user, request.RememberMe);
        return Ok(await Current(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return NoContent(); }

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Ok(new CurrentUserResponse(false, null, null, null, false, [], [])) : Ok(await Current(user));
    }

    private async Task<CurrentUserResponse> Current(ApplicationUser user) => new(true, user.Id, user.Email,
        user.DisplayName, await users.IsInRoleAsync(user, "PlatformAdmin"),
        await db.TournamentOrganizers.Where(x => x.UserId == user.Id).Select(x => x.TournamentId).ToArrayAsync(),
        await db.TeamManagers.Where(x => x.UserId == user.Id).Select(x => x.TeamId).ToArrayAsync());
}
