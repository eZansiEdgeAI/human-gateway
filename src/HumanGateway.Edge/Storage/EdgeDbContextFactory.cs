using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Design-time factory used by the EF Core tooling (<c>dotnet ef migrations add</c>) to construct an
/// <see cref="EdgeDbContext"/> without hosting the web application. The data source is taken from the first
/// argument (for scripting) or defaults to the on-site <c>data/edge.db</c> path.
/// </summary>
public sealed class EdgeDbContextFactory : IDesignTimeDbContextFactory<EdgeDbContext>
{
    /// <inheritdoc />
    public EdgeDbContext CreateDbContext(string[] args)
    {
        var dataSource = args.Length > 0 ? args[0] : "data/edge.db";
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(SqliteConnectionFactory.BuildConnectionString(dataSource))
            .Options;
        return new EdgeDbContext(options);
    }
}
