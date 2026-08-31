using Microsoft.Data.Sqlite;

namespace HumanGateway.Edge.Storage;

/// <summary>
/// Builds and opens SQLite connections with the durability PRAGMAs required by EDGE-FR-07 / NF-04.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>journal_mode=WAL</c> — write-ahead logging so committed writes survive power loss and readers
///       never block a writer (persisted in the database file).</item>
///   <item><c>synchronous=NORMAL</c> — the durability/performance sweet spot for WAL mode; a power loss may
///       lose the <em>most recent</em> transaction only in the (unlikely) failure window, never a committed
///       one that preceded a checkpoint. A default SQLite connection (<c>synchronous=FULL</c> + rollback
///       journal) can corrupt or lose committed writes on power failure, so this is mandatory.</item>
///   <item><c>foreign_keys=ON</c> — per-connection; enforces the schema's relational invariants.</item>
///   <item><c>busy_timeout</c> — per-connection; concurrent local clients (EDGE-FR-06, NF-01) contend on
///       locks with a bounded wait rather than failing immediately with SQLITE_BUSY.</item>
/// </list>
/// <c>journal_mode</c> and <c>synchronous</c> are durable database settings; re-applying them on every open
/// is idempotent and ensures the settings hold even when the file was created by a different process.
/// </remarks>
public static class SqliteConnectionFactory
{
    private const string DurabilityPragmas =
        """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        PRAGMA foreign_keys=ON;
        PRAGMA busy_timeout=5000;
        """;

    /// <summary>Builds the SQLite connection string for the given database file path.</summary>
    public static string BuildConnectionString(string dataSource)
        => new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

    /// <summary>
    /// The directory containing the SQLite database file named by a connection string, or null when the
    /// string has no usable <c>Data Source</c>. Used to co-locate the gateway secret store with the database
    /// (SP-07) so durable state shares one gitignored/volume-mounted directory.
    /// </summary>
    public static string? DataSourceDirectory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource)
                || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(builder.DataSource);
            return Path.GetDirectoryName(fullPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a connection and applies the durability PRAGMAs. Used by the DI wiring and by tests so that a
    /// single code path owns the pragma set (one source of truth).
    /// </summary>
    public static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        ApplyPragmas(connection);
        return connection;
    }

    /// <summary>Applies the durability PRAGMAs to an already-open connection (idempotent).</summary>
    public static void ApplyPragmas(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = connection.CreateCommand();
        command.CommandText = DurabilityPragmas;
        command.ExecuteNonQuery();
    }
}
