namespace HumanGateway.Core.Convergence;

/// <summary>
/// The sender-side safe-acknowledgement rule for the durable outbox (SYNC-FR-04, SYNC-FR-06, NF-05). An
/// outbox entry may be marked sent — and discarded from the durable outbox — only once the receiver's
/// <em>contiguous</em> cursor covers its sequence. This is the complement to <see cref="ConvergenceAnalyzer"/>
/// on the sender's side of the store-and-forward loop.
/// </summary>
/// <remarks>
/// <para>
/// Marking an entry sent on <em>any</em> successful response, regardless of the receiver's cursor, can strand a
/// sequence past a gap: if the sender discards entries the receiver has not contiguously acknowledged, a partial
/// failure leaves a permanent hole that no retry can fill (the entry is gone, and the receiver never advances
/// past the missing predecessor). Retaining everything past the contiguous cursor guarantees the next push
/// re-sends the tail, so a gap is always fillable from the sender's side (SYNC-FR-06).
/// </para>
/// <para>
/// The receiver's cursor is opaque on the wire; the sender decodes its own issued position via
/// <see cref="Cursor.CursorCodec"/> before applying this policy. This policy is pure — it decides from the two
/// numbers (entry sequence, receiver contiguous cursor) and carries no I/O.
/// </para>
/// </remarks>
public static class ConvergenceAckPolicy
{
    /// <summary>
    /// True when an outbox entry with <paramref name="sequence"/> is safe to mark sent: the receiver has
    /// contiguously applied through (and including) that sequence, so it cannot be lost.
    /// </summary>
    public static bool IsSafeToMarkSent(long sequence, long receiverContiguousCursor)
        => sequence <= receiverContiguousCursor;

    /// <summary>
    /// Partitions a batch's entry sequences into those safe to mark sent (contiguously covered by the
    /// receiver's cursor) and those that must be retained for a later push (past the cursor — either a gap or an
    /// in-flight tail the receiver has not yet contiguously acknowledged). The result is order-independent and
    /// deduplicated.
    /// </summary>
    public static AckPartition Partition(IEnumerable<long> entrySequences, long receiverContiguousCursor)
    {
        ArgumentNullException.ThrowIfNull(entrySequences);

        var safe = new List<long>();
        var retained = new List<long>();

        foreach (var sequence in entrySequences.Distinct().OrderBy(s => s))
        {
            if (IsSafeToMarkSent(sequence, receiverContiguousCursor))
            {
                safe.Add(sequence);
            }
            else
            {
                retained.Add(sequence);
            }
        }

        return new AckPartition { SafeToMarkSent = safe, RetainForRetry = retained };
    }
}

/// <summary>The result of <see cref="ConvergenceAckPolicy.Partition"/> (see <see cref="ConvergenceAckPolicy"/>).</summary>
public sealed record AckPartition
{
    /// <summary>Entry sequences safe to mark sent (contiguously covered by the receiver's cursor).</summary>
    public IReadOnlyList<long> SafeToMarkSent { get; init; } = Array.Empty<long>();

    /// <summary>Entry sequences that must remain pending (past the receiver's contiguous cursor).</summary>
    public IReadOnlyList<long> RetainForRetry { get; init; } = Array.Empty<long>();
}
