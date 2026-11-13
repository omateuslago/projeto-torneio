using Microsoft.AspNetCore.Identity;
using Projeto_Campeonato.Api.Domain;

namespace Projeto_Campeonato.Api.Features;

public static class AuthBootstrap
{
    public static async Task Seed(IServiceProvider services, IConfiguration config)
    {
        var email = config["Auth:BootstrapAdminEmail"];
        var password = config["Auth:BootstrapAdminPassword"];
        var name = config["Auth:BootstrapAdminDisplayName"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(name))
            return;

        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roles.RoleExistsAsync("PlatformAdmin"))
        {
            var roleResult = await roles.CreateAsync(new IdentityRole<Guid>("PlatformAdmin"));
            EnsureSucceeded(roleResult, "criar o papel PlatformAdmin");
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email.Trim(),
                Email = email.Trim(),
                DisplayName = name.Trim()
            };
            var created = await users.CreateAsync(user, password);
            EnsureSucceeded(created, "criar o PlatformAdmin configurado");
        }

        if (!await users.IsInRoleAsync(user, "PlatformAdmin"))
            EnsureSucceeded(await users.AddToRoleAsync(user, "PlatformAdmin"), "atribuir o papel PlatformAdmin");
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Não foi possível {action}: " +
                string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
