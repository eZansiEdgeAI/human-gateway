namespace HumanGateway.Core.Cursor;

/// <summary>
/// Pure cursor arithmetic shared by the sync engine. Cursor advancement is <em>safe</em>: a receiver only
/// advances its position over sequences it has actually applied, so a gap in the stream never causes a loss
/// (SYNC-FR-06, SYNC-FR-07). This is the deterministic reordering/convergence primitive behind out-of-order
/// tolerance.
/// </summary>
public static class CursorMath
{
    /// <summary>
    /// Advances <paramref name="prior"/> contiguously over the sequences present in <paramref name="present"/>.
    /// Starting from <c>prior.Sequence + 1</c>, the cursor advances one step at a time while the next sequence
    /// is present; it stops at the first gap. This means out-of-order batches converge without loss: a batch
    /// carrying <c>{1, 3}</c> advances the cursor to 1, and the later batch carrying <c>{2}</c> advances it to 3.
    /// </summary>
    /// <remarks>
    /// Sequences at or below <c>prior.Sequence</c> are ignored (already covered). Callers must supply the
    /// receiver's own prior position (decoded from the echoed <c>sinceCursor</c>), not the batch's span.
    /// </remarks>
    public static CursorPosition AdvanceContiguous(CursorPosition prior, IEnumerable<long> present)
    {
        ArgumentNullException.ThrowIfNull(present);

        var set = present as IReadOnlySet<long> ?? new HashSet<long>(present);
        var cursor = prior.Sequence;
        while (set.Contains(cursor + 1))
        {
            cursor++;
        }
        return new CursorPosition(cursor);
    }

    /// <summary>
    /// Advances <paramref name="prior"/> contiguously over the sequences of the given items
    /// (the per-item <c>sequence</c> field), ignoring items whose sequence is at or below the prior position.
    /// </summary>
    public static CursorPosition AdvanceContiguous(CursorPosition prior, IEnumerable<HumanGateway.Protocol.Models.SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return AdvanceContiguous(prior, items.Select(i => i.Sequence));
    }
}
