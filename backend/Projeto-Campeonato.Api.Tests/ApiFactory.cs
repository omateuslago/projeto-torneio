using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projeto_Campeonato.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Projeto_Campeonato.Api.Domain;
using System.Net.Http.Json;
using Projeto_Campeonato.Api.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Projeto_Campeonato.Api.Tests;

// A separate database per fixture makes tests safe for any PostgreSQL server supplied by the developer.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AdminEmail = "tests-admin@brakvyo.local";
    private const string AdminPassword = "Tests-admin-2026";
    private readonly string databaseName = "tournament_tests_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    private string testConnection = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Tournaments"] = testConnection }));
        builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
    }

    public async Task InitializeAsync()
    {
        adminConnection = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("Set TEST_POSTGRES_CONNECTION to a PostgreSQL connection with CREATEDB permission. See README.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
        testConnection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName }.ConnectionString;
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TournamentDbContext>().Database.MigrateAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("PlatformAdmin"))).Succeeded);
        var admin = new ApplicationUser { Id = Guid.NewGuid(), UserName = AdminEmail, Email = AdminEmail, DisplayName = "Test Admin" };
        Assert.True((await users.CreateAsync(admin, AdminPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "PlatformAdmin")).Succeeded);
    }

    public new HttpClient CreateClient() => Authenticate(base.CreateClient(new() { HandleCookies = true }));
    public HttpClient CreateAnonymousClient() => base.CreateClient(new() { HandleCookies = true });
    public async Task<(HttpClient Client, CurrentUserResponse User)> RegisterUser(string? email = null, string displayName = "Test User")
    {
        var client = CreateAnonymousClient();
        AddCsrf(client);
        email ??= $"user-{Guid.NewGuid():N}@brakvyo.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Tests-user-2026",
            displayName
        });
        response.EnsureSuccessStatusCode();
        AddCsrf(client);
        return (client, (await response.Content.ReadFromJsonAsync<CurrentUserResponse>())!);
    }

    public async Task GrantTournament(Guid userId, Guid tournamentId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        db.TournamentOrganizers.Add(new TournamentOrganizer(userId, tournamentId));
        await db.SaveChangesAsync();
    }

    public async Task GrantTeam(Guid userId, Guid teamId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TournamentDbContext>();
        db.TeamManagers.Add(new TeamManager(userId, teamId));
        await db.SaveChangesAsync();
    }

    public HttpClient Authenticate(HttpClient client, string email = AdminEmail, string password = AdminPassword)
    {
        AddCsrf(client);
        var response = client.PostAsJsonAsync("/api/auth/login", new { email, password, rememberMe = false }).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        AddCsrf(client);
        return client;
    }
    public void AddCsrf(HttpClient client)
    {
        var response = client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf").GetAwaiter().GetResult()!;
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", response.Token);
    }
    private sealed record CsrfResponse(string Token);

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (string.IsNullOrEmpty(adminConnection)) return;
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
