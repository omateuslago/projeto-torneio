using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projeto_Campeonato.Api.Infrastructure;

namespace Projeto_Campeonato.Api.Tests;

// A separate database per fixture makes tests safe for any PostgreSQL server supplied by the developer.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string databaseName = "tournament_tests_" + Guid.NewGuid().ToString("N");
    private string adminConnection = "";
    private string testConnection = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Tournaments"] = testConnection }));
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
    }

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
