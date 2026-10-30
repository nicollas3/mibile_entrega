using Brasa.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;
namespace Brasa.Tests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "BrasaLocal!2026", WebhookSecret = "test-only-webhook-secret-not-for-production";
    readonly string database = "brasa_test_" + Guid.NewGuid().ToString("N");
    string BaseConnection => Environment.GetEnvironmentVariable("BRASA_TEST_DATABASE") ?? "Host=localhost;Database=postgres;Username=brasa;Password=local-development-only";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cs = new NpgsqlConnectionStringBuilder(BaseConnection) { Database = database };
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Database", cs.ConnectionString);
        builder.UseSetting("Jwt:Key", "test-only-random-signing-material-at-least-32-bytes");
        builder.UseSetting("Payments:WebhookSecret", WebhookSecret);
        builder.UseSetting("Database:Migrate", "true");
        builder.UseSetting("Demo:Seed", "true");
        builder.UseSetting("Demo:Password", Password);
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
        builder.ConfigureServices(services => services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options => { options.GlobalLimiter = null; }));
        builder.ConfigureServices(services => { services.RemoveAll<IAccountEmail>(); services.AddSingleton<TestEmail>(); services.AddSingleton<IAccountEmail>(sp => sp.GetRequiredService<TestEmail>()); });
    }
    public async Task InitializeAsync()
    {
        await using var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(BaseConnection) { Database = "postgres" }.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {database}", c);
        await cmd.ExecuteNonQueryAsync();
        using var client = CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(BaseConnection) { Database = "postgres" }.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }
}
[CollectionDefinition("database", DisableParallelization = true)] public sealed class DatabaseCollection : ICollectionFixture<ApiFixture>;
public sealed class TestEmail : IAccountEmail
{
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Tokens = new();
    public Task SendReset(string email, string token, CancellationToken ct)
    {
        Tokens[email] = token;
        return Task.CompletedTask;
    }
}
