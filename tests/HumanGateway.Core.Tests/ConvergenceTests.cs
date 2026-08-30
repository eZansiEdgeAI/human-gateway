using HumanGateway.Core.Convergence;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Property tests for convergence after long disconnects and partial failures (SYNC-FR-04, SYNC-FR-06): gap
/// detection, contiguous-cursor derivation, convergence status, and the sender-side safe-acknowledgement rule.
/// These pin the "converge without loss or duplication after a multi-day outage" contract before it is wired to
/// transport, so the chaos suite (duplication, out-of-order, multi-day outage) can assert against them.
/// </summary>
public class ConvergenceTests
{
    // ------------------------------------------------------------------------------------------------
    // ConvergenceAnalyzer — gap detection and convergence status
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void Analyze_empty_stream_is_converged_and_empty()
    {
        var state = ConvergenceAnalyzer.Analyze(Array.Empty<long>());

        Assert.True(state.IsEmpty);
        Assert.True(state.IsConverged);
        Assert.False(state.HasGaps);
        Assert.Empty(state.Gaps);
        Assert.Equal(0, state.ContiguousCursor);
        Assert.Equal(0, state.HighWatermark);
        Assert.Null(state.FirstGap);
    }

    [Fact]
    public void Analyze_contiguous_stream_is_converged_with_no_gaps()
    {
        var state = ConvergenceAnalyzer.Analyze(new long[] { 1, 2, 3, 4, 5 });

        Assert.True(state.IsConverged);
        Assert.False(state.HasGaps);
        Assert.Empty(state.Gaps);
        Assert.Equal(5, state.ContiguousCursor);
        Assert.Equal(5, state.HighWatermark);
        Assert.Null(state.FirstGap);
    }

    [Fact]
    public void Analyze_detects_a_single_gap()
    {
        // {1, 3}: contiguous cursor 1, high-watermark 3, one missing sequence (2).
        var state = ConvergenceAnalyzer.Analyze(new long[] { 1, 3 });

        Assert.False(state.IsConverged);
        Assert.True(state.HasGaps);
        Assert.Equal(1, state.ContiguousCursor);
        Assert.Equal(3, state.HighWatermark);
        Assert.Equal(2, state.FirstGap);

        var gap = Assert.Single(state.Gaps);
        Assert.Equal(2, gap.Start);
        Assert.Equal(2, gap.End);
        Assert.Equal(1, gap.Count);
    }

    [Fact]
    public void Analyze_detects_a_gap_at_the_start()
    {
        // {2, 3}: sequence 1 is missing, so the cursor cannot advance past 0.
        var state = ConvergenceAnalyzer.Analyze(new long[] { 2, 3 });

        Assert.False(state.IsConverged);
        Assert.Equal(0, state.ContiguousCursor);
        Assert.Equal(3, state.HighWatermark);
        Assert.Equal(1, state.FirstGap);

        var gap = Assert.Single(state.Gaps);
        Assert.Equal(1, gap.Start);
        Assert.Equal(1, gap.End);
    }

    [Fact]
    public void Analyze_detects_multiple_gaps_as_ranges()
    {
        // {1, 2, 5, 6, 10}: gaps [3,4] and [7,9].
        var state = ConvergenceAnalyzer.Analyze(new long[] { 1, 2, 5, 6, 10 });

        Assert.False(state.IsConverged);
        Assert.Equal(2, state.ContiguousCursor);
        Assert.Equal(10, state.HighWatermark);
        Assert.Equal(3, state.FirstGap);
        Assert.Equal(2, state.Gaps.Count);

        Assert.Equal(3, state.Gaps[0].Start);
        Assert.Equal(4, state.Gaps[0].End);
        Assert.Equal(7, state.Gaps[1].Start);
        Assert.Equal(9, state.Gaps[1].End);
    }

    [Fact]
    public void Analyze_is_invariant_under_every_permutation()
    {
        var applied = new long[] { 10, 1, 3, 7, 4 };

        var expected = ConvergenceAnalyzer.Analyze(applied);

        foreach (var permutation in Permutations(applied.ToList()))
        {
            var actual = ConvergenceAnalyzer.Analyze(permutation);
            Assert.Equal(expected.ContiguousCursor, actual.ContiguousCursor);
            Assert.Equal(expected.HighWatermark, actual.HighWatermark);
            Assert.Equal(expected.Gaps.Select(g => (g.Start, g.End)),
                         actual.Gaps.Select(g => (g.Start, g.End)));
            Assert.Equal(expected.FirstGap, actual.FirstGap);
        }
    }

    [Fact]
    public void Analyze_duplicates_collapse_idempotently()
    {
        // Duplicate applied sequences (replays) never change the result.
        var state = ConvergenceAnalyzer.Analyze(new long[] { 1, 1, 2, 2, 2, 3 });

        Assert.True(state.IsConverged);
        Assert.Equal(3, state.ContiguousCursor);
        Assert.Equal(3, state.HighWatermark);
    }

    [Fact]
    public void Analyze_sparse_high_watermark_is_a_single_gap_range()
    {
        // A pathological sparse stream yields one gap range, not an enumerated list (bounded memory).
        var state = ConvergenceAnalyzer.Analyze(new long[] { 1, 1_000_000_000 });

        Assert.Equal(1, state.ContiguousCursor);
        Assert.Equal(1_000_000_000, state.HighWatermark);
        var gap = Assert.Single(state.Gaps);
        Assert.Equal(2, gap.Start);
        Assert.Equal(1_000_000_000 - 1, gap.End);
    }

    [Fact]
    public void Analyze_multi_day_outage_converges_when_gap_filled()
    {
        // Several days of traffic applied, minus a small partial-failure hole ({501,502,503} lost).
        var applied = Enumerable.Range(1, 500).Select(i => (long)i).Concat(new long[] { 504 }).ToList();

        var partial = ConvergenceAnalyzer.Analyze(applied);
        Assert.False(partial.IsConverged);
        Assert.Equal(500, partial.ContiguousCursor);
        Assert.Equal(504, partial.HighWatermark);
        Assert.Equal(501, partial.FirstGap);
        Assert.Equal((501L, 503L), Assert.Single(partial.Gaps.Select(g => (g.Start, g.End))));

        // The missing range arrives (a reconnect converges within one sync cycle): the stream is now contiguous.
        applied.AddRange(new long[] { 501, 502, 503 });
        var converged = ConvergenceAnalyzer.Analyze(applied);
        Assert.True(converged.IsConverged);
        Assert.Equal(504, converged.ContiguousCursor);
        Assert.Empty(converged.Gaps);
    }

    // ------------------------------------------------------------------------------------------------
    // ConvergenceAckPolicy — the sender-side safe-acknowledgement rule
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void IsSafeToMarkSent_covers_only_sequences_up_to_the_cursor()
    {
        Assert.True(ConvergenceAckPolicy.IsSafeToMarkSent(3, 3));
        Assert.True(ConvergenceAckPolicy.IsSafeToMarkSent(1, 3));
        Assert.False(ConvergenceAckPolicy.IsSafeToMarkSent(4, 3));
        Assert.False(ConvergenceAckPolicy.IsSafeToMarkSent(2, 0)); // no progress yet
    }

    [Fact]
    public void Partition_retains_everything_past_a_gap()
    {
        // Receiver applied {1, 3} → cursor 1. The sender must not discard 2 or 3: 2 is a gap, 3 is past it.
        var partition = ConvergenceAckPolicy.Partition(new long[] { 1, 2, 3 }, receiverContiguousCursor: 1);

        Assert.Equal(new long[] { 1 }, partition.SafeToMarkSent);
        Assert.Equal(new long[] { 2, 3 }, partition.RetainForRetry);
    }

    [Fact]
    public void Partition_empty_cursor_retains_everything()
    {
        var partition = ConvergenceAckPolicy.Partition(new long[] { 1, 2 }, receiverContiguousCursor: 0);

        Assert.Empty(partition.SafeToMarkSent);
        Assert.Equal(new long[] { 1, 2 }, partition.RetainForRetry);
    }

    [Fact]
    public void Partition_marks_all_sent_only_when_contiguously_covered()
    {
        var partition = ConvergenceAckPolicy.Partition(new long[] { 3, 1, 2 }, receiverContiguousCursor: 3);

        Assert.Equal(new long[] { 1, 2, 3 }, partition.SafeToMarkSent);
        Assert.Empty(partition.RetainForRetry);
    }

    [Fact]
    public void Partition_is_order_independent_and_deduplicated()
    {
        var expected = ConvergenceAckPolicy.Partition(new long[] { 1, 2, 3, 4 }, receiverContiguousCursor: 2);

        var shuffled = ConvergenceAckPolicy.Partition(new long[] { 4, 4, 2, 1, 3 }, receiverContiguousCursor: 2);

        Assert.Equal(expected.SafeToMarkSent, shuffled.SafeToMarkSent);
        Assert.Equal(expected.RetainForRetry, shuffled.RetainForRetry);
    }

    // ------------------------------------------------------------------------------------------------
    // Engine integration — convergence status surfaced on ApplyBatchResult
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ApplyBatch_reports_convergence_status_and_recovers_from_a_gap()
    {
        var outbox = new InMemoryOutbox();
        var inbox = new InMemoryInbox();
        var engine = new SyncEngine(outbox, inbox, new HumanGateway.Core.Idempotency.InMemoryIdempotencyStore());
        var receiver = TestData.Receiver;

        // First batch carries {1, 3}: both applied, cursor stops at 1, a gap at 2 is reported (SYNC-FR-04/06).
        var r1 = await engine.ApplyBatchAsync(MakeBatch("batch-0001", new[] { MessageItem(3, "msg-0003"), MessageItem(1, "msg-0001") }),
            new ApplyBatchRequest { Receiver = receiver, Now = TestData.FixedNow });

        Assert.True(r1.IsValid);
        Assert.Equal(1, r1.Position.Sequence);
        Assert.NotNull(r1.Convergence);
        Assert.False(r1.Convergence!.IsConverged);
        Assert.Equal(2, r1.Convergence.FirstGap);

        // The gap-filling batch converges the stream in one step (the cursor is derived from the full history).
        var r2 = await engine.ApplyBatchAsync(MakeBatch("batch-0002", new[] { MessageItem(2, "msg-0002") }, r1.Cursor),
            new ApplyBatchRequest { Receiver = receiver, Now = TestData.FixedNow });

        Assert.Equal(3, r2.Position.Sequence);
        Assert.True(r2.Convergence!.IsConverged);
        Assert.Empty(r2.Convergence.Gaps);
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private const string GatewayId = "edge:00000000-0000-0000-0000-000000000001";

    private static SyncItem MessageItem(long sequence, string id)
        => TestData.MessageItem(TestData.NewMessage(id), sequence);

    private static SyncBatch MakeBatch(string batchId, SyncItem[] items, string? sinceCursor = null)
    {
        var ordered = items.OrderBy(i => i.Sequence).ToArray();
        return new SyncBatch
        {
            BatchId = batchId,
            GatewayId = GatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = sinceCursor,
            IdempotencyKey = HumanGateway.Core.Idempotency.IdempotencyKeys.Derive(batchId, items),
            SequenceStart = ordered.Length == 0 ? null : ordered[0].Sequence,
            SequenceEnd = ordered.Length == 0 ? null : ordered[^1].Sequence,
            Items = ordered.ToList(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
    }

    private static IEnumerable<List<long>> Permutations(List<long> items)
    {
        if (items.Count == 0)
        {
            yield return new List<long>();
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var head = items[i];
            var tail = items.Take(i).Concat(items.Skip(i + 1)).ToList();
            foreach (var rest in Permutations(tail))
            {
                rest.Insert(0, head);
                yield return rest;
            }
        }
    }
}
