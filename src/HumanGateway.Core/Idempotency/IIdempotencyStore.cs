namespace HumanGateway.Core.Idempotency;

/// <summary>
/// Deduplication port for replayed sync batches (SYNC-FR-02, NF-05). A retry of the same batch MUST reuse the
/// same <c>batchId</c> AND <c>idempotencyKey</c>; changing either creates a new logical batch
/// (syncbatch.schema.json). The store makes applying a batch idempotent so at-least-once delivery yields
/// exactly-once effect.
/// </summary>
/// <remarks>
/// Implementations must be atomic under concurrency (e.g. a unique index on <c>(batchId, idempotencyKey)</c>
/// in SQLite/PostgreSQL). The in-memory reference implementation in this assembly locks for single-process use.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>Returns true when the batch has already been applied (duplicate replay).</summary>
    Task<bool> WasAppliedAsync(string batchId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Records the batch as applied. Must be a no-op (or idempotent) if already recorded.</summary>
    Task RecordAsync(string batchId, string idempotencyKey, CancellationToken ct = default);
}
