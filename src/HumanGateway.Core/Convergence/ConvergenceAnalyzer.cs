using HumanGateway.Core.Cursor;

namespace HumanGateway.Core.Convergence;

/// <summary>A closed (inclusive) range of missing per-gateway sequence numbers below the applied high-watermark.</summary>
public readonly record struct SequenceGap(long Start, long End)
{
    /// <summary>The number of missing sequences covered by this gap.</summary>
    public long Count => End - Start + 1;
}

/// <summary>
/// The convergence status of one gateway's applied sequence stream (SYNC-FR-04, SYNC-FR-06, product vision §11).
/// "Converged" means the applied set is contiguous from sequence 1 up to <see cref="HighWatermark"/> — every
/// message has been applied exactly once and nothing is waiting on a missing predecessor. A gap below the
/// high-watermark is the signature of a partial failure or a lost batch, and it is exactly what must be filled
/// before the receiver can advance its cursor past the hole.
/// </summary>
public sealed record ConvergenceState
{
    /// <summary>The highest contiguous applied sequence (0 when nothing has been applied).</summary>
    public long ContiguousCursor { get; init; }

    /// <summary>The highest applied sequence, contiguous or not (0 when the stream is empty).</summary>
    public long HighWatermark { get; init; }

    /// <summary>Closed ranges of missing sequences below <see cref="HighWatermark"/> (empty when converged).</summary>
    public IReadOnlyList<SequenceGap> Gaps { get; init; } = Array.Empty<SequenceGap>();

    /// <summary>True when the stream is empty (nothing applied yet).</summary>
    public bool IsEmpty => HighWatermark == 0;

    /// <summary>True when any sequence below the high-watermark is missing (a partial failure / lost batch).</summary>
    public bool HasGaps => Gaps.Count > 0;

    /// <summary>
    /// True when the stream has converged: it is empty, or contiguous from 1 to <see cref="HighWatermark"/>
    /// with no gaps (SYNC-FR-06 — "converge without loss or duplication").
    /// </summary>
    public bool IsConverged => !HasGaps;

    /// <summary>The smallest missing sequence (the first gap), or null when converged.</summary>
    public long? FirstGap => HasGaps ? Gaps[0].Start : null;
}

/// <summary>
/// Pure, deterministic convergence analysis for a gateway's applied sequence stream (SYNC-FR-04, SYNC-FR-06).
/// This is the long-disconnect / partial-failure primitive: given the full set of sequences a receiver has
/// applied, it reports the contiguous cursor (reusing <see cref="CursorMath"/>), the applied high-watermark,
/// and the exact missing ranges below it. The receiver is converged iff there are no such gaps.
/// </summary>
/// <remarks>
/// <para>
/// Analysis is <em>order-independent</em> and <em>idempotent</em>: the same multiset of applied sequences
/// always yields the same <see cref="ConvergenceState"/> regardless of arrival order, and duplicates collapse.
/// This makes the result safe to property-test by shuffling, and safe to recompute from a durable inbox that
/// may contain replays.
/// </para>
/// <para>
/// The gap list is represented as closed ranges rather than an enumeration of individual sequences, so a
/// pathological stream (e.g. applied <c>{1, 1_000_000_000}</c>) yields a single gap range instead of a
/// billion-entry list. The number of ranges is bounded by the number of distinct applied sequences.
/// </para>
/// </remarks>
public static class ConvergenceAnalyzer
{
    /// <summary>
    /// Analyzes the applied sequence set for a gateway (the receiver's own full applied history, from the
    /// beginning — see <see cref="Inbox.IInbox.GetSequencesAsync"/>). Sequences at or below 0 are ignored
    /// (unset). The result reports whether the stream has converged and, if not, exactly which sequences are
    /// missing below the applied high-watermark.
    /// </summary>
    public static ConvergenceState Analyze(IEnumerable<long> appliedSequences)
    {
        ArgumentNullException.ThrowIfNull(appliedSequences);

        var distinct = appliedSequences
            .Where(s => s >= 1)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        if (distinct.Count == 0)
        {
            return new ConvergenceState
            {
                ContiguousCursor = 0,
                HighWatermark = 0,
                Gaps = Array.Empty<SequenceGap>(),
            };
        }

        var highWatermark = distinct[^1];

        // The contiguous cursor is the highest n such that 1..n are all present. This is the same primitive the
        // sync engine uses to advance its position, so the two can never disagree about where the stream has
        // converged (SYNC-FR-03/06/07).
        var contiguousCursor = CursorMath.AdvanceContiguous(CursorPosition.Start, distinct).Sequence;

        // Walk the sorted applied set; every jump over an integer is a gap range [expected, current - 1].
        var gaps = new List<SequenceGap>();
        long expected = 1;
        foreach (var sequence in distinct)
        {
            if (sequence > expected)
            {
                gaps.Add(new SequenceGap(expected, sequence - 1));
            }
            expected = sequence + 1;
        }

        return new ConvergenceState
        {
            ContiguousCursor = contiguousCursor,
            HighWatermark = highWatermark,
            Gaps = gaps,
        };
    }
}
