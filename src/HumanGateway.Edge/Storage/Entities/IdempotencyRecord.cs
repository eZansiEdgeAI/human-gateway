namespace HumanGateway.Edge.Storage.Entities;

/// <summary>
/// Durable idempotency record (SYNC-FR-02, NF-05): proves a sync batch has already been applied. The
/// composite primary key <c>(batchId, idempotencyKey)</c> is unique, so recording the same logical batch
/// twice is a no-op (the unique-constraint violation is swallowed by <c>SqliteIdempotencyStore</c>) and a
/// replayed batch is detected before any item is re-applied.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Durable batch identity (part of the composite primary key).</summary>
    public string BatchId { get; set; } = null!;

    /// <summary>Stable per-batch key (part of the composite primary key).</summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>When the batch was recorded as applied.</summary>
    public DateTimeOffset AppliedAtUtc { get; set; }
}
