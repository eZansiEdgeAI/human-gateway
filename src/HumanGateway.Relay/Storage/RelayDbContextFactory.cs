using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HumanGateway.Relay.Storage;

/// <summary>
/// Design-time factory used by the EF Core tooling (<c>dotnet ef migrations add</c>) to construct a
/// <see cref="RelayDbContext"/> without hosting the web application. The connection string is taken from the
/// first argument (for scripting) or defaults to the local development PostgreSQL described in the README.
/// </summary>
public sealed class RelayDbContextFactory : IDesignTimeDbContextFactory<RelayDbContext>
{
    /// <inheritdoc />
    public RelayDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0
            ? args[0]
            : "Host=localhost;Port=5432;Database=humangateway_relay;Username=humangateway;Password=humangateway";

        var options = new DbContextOptionsBuilder<RelayDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new RelayDbContext(options);
    }
}
