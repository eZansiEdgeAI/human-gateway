using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Hosts the real Relay <c>Program</c> over a disposable Testcontainers PostgreSQL (cloud-relay §6: the
/// integration tier runs against real PostgreSQL). Program.cs applies EF Core migrations on startup, so the
/// first request materialises the full schema in the throwaway container database. Each test class gets its
/// own container instance via <see cref="IClassFixture{TFixture}"/>; the container is torn down afterwards.
/// </summary>
public sealed class PostgresRelayFixture : IAsyncLifetime
{
    /// <summary>
    /// Unique database name per run — guarantees a clean schema even if a previous run's container was not
    /// removed (Testcontainers under podman can leave a named container behind on hard interruption).
    /// </summary>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("humangateway_relay_test_" + Guid.NewGuid().ToString("N")[..12])
        .WithUsername("humangateway")
        .WithPassword("humangateway")
        .Build();

    /// <summary>The running PostgreSQL container.</summary>
    public PostgreSqlContainer Container => _container;

    /// <inheritdoc />
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A <c>WebApplicationFactory&lt;Program&gt;</c> pointed at the fixture's Testcontainers PostgreSQL by
/// overriding <c>ConnectionStrings:Relay</c> — the exact setting Program.cs reads to build its context.
/// </summary>
public sealed class RelayApiFactory : WebApplicationFactory<Program>
{
    private readonly PostgresRelayFixture _fixture;

    public RelayApiFactory(PostgresRelayFixture fixture) => _fixture = fixture;

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting writes a HOST setting, which has higher precedence than any appsettings source in the
        // WebApplicationBuilder configuration chain. This is essential here: appsettings.Development.json
        // defines ConnectionStrings:Relay (the localhost dev database), and the factory host runs in the
        // Development environment by default — so a plain ConfigureAppConfiguration override would be shadowed
        // and the test Relay would connect to the developer's local database instead of the fixture container.
        builder.UseSetting("ConnectionStrings:Relay", _fixture.Container.GetConnectionString());
    }
}
