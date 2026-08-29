using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Applies the SQLite durability PRAGMAs (WAL, synchronous=NORMAL, foreign_keys, busy_timeout) to every
/// connection EF Core opens. WAL and synchronous are durable database settings but re-applying them is
/// idempotent and cheap; foreign_keys and busy_timeout are per-connection and must be applied each open
/// (EDGE-FR-06/07, NF-04). Stateless, so a single shared instance is safe.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is SqliteConnection sqlite)
        {
            SqliteConnectionFactory.ApplyPragmas(sqlite);
        }
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqliteConnection sqlite)
        {
            SqliteConnectionFactory.ApplyPragmas(sqlite);
        }
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
