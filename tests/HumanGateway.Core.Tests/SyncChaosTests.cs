using HumanGateway.Core.Convergence;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// The sync/chaos suite (synchronisation §6 #2..#5, SYNC-FR-02/04/06/07, product vision §11): drives the
/// <see cref="SyncEngine"/> end-to-end over in-memory ports through the three chaos scenarios —
/// <list type="bullet">
///   <item><b>Duplication in transit</b> → deduplicated to exactly-once effect (NF-05, SYNC-FR-02);</item>
///   <item><b>Out-of-order arrival</b> → deterministically reordered, no loss, no duplication (SYNC-FR-07);</item>
///   <item><b>Multi-day disconnection</b> → convergence within one sync cycle after reconnect (SYNC-FR-04/06).</item>
/// </list>
/// Every scenario is seeded and repeatable, and each asserts the product vision §11 success metrics: 0 lost /
/// 0 duplicate messages, and convergence within one sync cycle after reconnect.
/// </summary>
public class SyncChaosTests
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-000000000001";

    private static readonly DateTimeOffset Now = TestData.FixedNow;
    private static readonly Participant Receiver = TestData.Receiver;

    // ------------------------------------------------------------------------------------------------
    // Scenario 1: duplication in transit → exactly-once effect (SYNC-FR-02, NF-05)
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Duplication_replayed_batch_has_exactly_once_effect()
    {
        const int total = 25;
        var sequences = Enumerable.Range(1, total).Select(s => (long)s).ToArray();
        var batch = MakeBatch("chaos-dup-batch", sequences.Select(MessageItem).ToArray());

        var (engine, _, inbox, _) = NewEngine();

        var first = await ApplyAsync(engine, batch);
        Assert.False(first.IsDuplicate);
        Assert.Equal(total, first.AppliedItems.Count);
        Assert.Equal(total, first.DeliveryAcks.Count);

        // The transport replays the same batch several times (at-least-once delivery, NF-05): each replay is
        // collapsed by the batch idempotency key with no further effect (SYNC-FR-02).
        for (var i = 0; i < 5; i++)
        {
            var replay = await ApplyAsync(engine, batch);
            Assert.True(replay.IsDuplicate);
            Assert.Empty(replay.AppliedItems);
            Assert.Empty(replay.DeliveryAcks);
        }

        // 0 lost / 0 duplicate (product vision §11).
        await AssertExactlyOnceAsync(inbox, sequences);
    }

    [Fact]
    public async Task Duplication_message_resent_in_fresh_batches_has_no_second_effect()
    {
        const int total = 20;
        var sequences = Enumerable.Range(1, total).Select(s => (long)s).ToArray();
        var items = sequences.Select(MessageItem).ToArray();

        var (engine, _, inbox, _) = NewEngine();
        await ApplyAsync(engine, MakeBatch("chaos-dup-original", items));

        // Each message is re-sent under a brand-new batch identity (a retry that lost its batch id, or a second
        // relay re-delivering the same message). The per-message dedup — not just batch idempotency — must still
        // collapse it (SYNC-FR-02, NF-05).
        foreach (var item in items)
        {
            var resent = MakeBatch($"chaos-dup-resent-{item.Sequence:D5}", new[] { item });
            var result = await ApplyAsync(engine, resent);
            Assert.Empty(result.AppliedItems);
        }

        await AssertExactlyOnceAsync(inbox, sequences);
    }

    // ------------------------------------------------------------------------------------------------
    // Scenario 2: out-of-order arrival → deterministic reorder, no loss, no duplication (SYNC-FR-07)
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(98765)]
    public async Task OutOfOrder_random_partitions_converge_exactly_once(int seed)
    {
        const int total = 60;
        var rng = new Random(seed);
        var sequences = Enumerable.Range(1, total).Select(s => (long)s).ToArray();

        var (engine, _, inbox, _) = NewEngine();

        // The stream is shredded into random-length batches and applied in random order — out-of-order arrival
        // is normal, never a bug (SYNC-FR-07).
        var batches = RandomBatches("chaos-oob", sequences, rng);
        foreach (var batch in batches.OrderBy(_ => rng.Next()))
        {
            var result = await ApplyAsync(engine, batch);
            Assert.True(result.IsValid);

            // Within each batch, applied items are deterministically reordered by sequence number.
            var applied = result.AppliedItems.Select(i => i.Sequence).ToArray();
            Assert.Equal(applied.OrderBy(s => s).ToArray(), applied);
        }

        // 0 lost / 0 duplicate, and the stream converged: contiguous cursor == high watermark == total, no gaps.
        await AssertExactlyOnceAsync(inbox, sequences);
        var state = ConvergenceAnalyzer.Analyze(await inbox.GetSequencesAsync(GatewayId));
        Assert.True(state.IsConverged);
        Assert.Equal(total, state.ContiguousCursor);
        Assert.Equal(total, state.HighWatermark);
    }

    [Fact]
    public async Task OutOfOrder_final_state_is_independent_of_arrival_order()
    {
        const int total = 40;
        var sequences = Enumerable.Range(1, total).Select(s => (long)s).ToArray();

        // A fixed partition into contiguous chunks — the same multiset of batches, just delivered in a different
        // order. The converged end state must be identical either way (deterministic convergence, SYNC-FR-07).
        var batches = sequences
            .Chunk(5)
            .Select((chunk, i) => MakeBatch($"chaos-part-{i:D3}", chunk.Select(MessageItem).ToArray()))
            .ToList();

        var firstEngine = NewEngine().Engine;
        var secondEngine = NewEngine().Engine;

        var first = await ApplyInOrderAsync(firstEngine, batches, new Random(7));
        var second = await ApplyInOrderAsync(secondEngine, batches, new Random(99));

        Assert.True(first.Convergence!.IsConverged);
        Assert.True(second.Convergence!.IsConverged);
        Assert.Equal(first.Cursor, second.Cursor);
        Assert.Equal(first.Position.Sequence, second.Position.Sequence);
        Assert.Equal(first.Convergence.ContiguousCursor, second.Convergence.ContiguousCursor);
        Assert.Equal(first.Convergence.HighWatermark, second.Convergence.HighWatermark);
    }

    // ------------------------------------------------------------------------------------------------
    // Scenario 3: multi-day disconnection → convergence within one sync cycle (SYNC-FR-04, SYNC-FR-06)
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MultiDay_partial_failure_converges_in_one_sync_cycle()
    {
        const int days = 3;
        const int perDay = 50;
        const int total = days * perDay; // 150 messages across 3 days

        // A batch lost mid-transit: the middle of day 2 (a contiguous hole, SYNC-FR-04/06).
        var lost = Enumerable.Range(71, 10).Select(s => (long)s).ToArray(); // 71..80
        var retained = Enumerable.Range(1, total).Select(s => (long)s).Except(lost).ToArray();

        var (engine, _, inbox, _) = NewEngine();

        // Several days of traffic arrive out of order with the middle chunk lost: everything is applied, the
        // cursor stalls at the hole, and the tail past the gap is retained (never dropped, SYNC-FR-07).
        var rng = new Random(2026);
        foreach (var batch in RandomBatches("chaos-multiday", retained, rng).OrderBy(_ => rng.Next()))
        {
            await ApplyAsync(engine, batch);
        }

        var partial = ConvergenceAnalyzer.Analyze(await inbox.GetSequencesAsync(GatewayId));
        Assert.False(partial.IsConverged);
        Assert.Equal(70, partial.ContiguousCursor);
        Assert.Equal(total, partial.HighWatermark); // the tail past the gap was still applied
        Assert.Equal(71, partial.FirstGap);

        // The reconnect fills the hole in one sync cycle → the stream converges with no loss, no duplication.
        var reconnect = MakeBatch("chaos-multiday-reconnect", lost.Select(MessageItem).ToArray());
        var result = await ApplyAsync(engine, reconnect);

        Assert.True(result.Convergence!.IsConverged);
        Assert.Equal(total, result.Position.Sequence);
        Assert.Equal(total, result.Convergence.HighWatermark);
        Assert.Empty(result.Convergence.Gaps);

        // 0 lost / 0 duplicate (product vision §11).
        await AssertExactlyOnceAsync(inbox, Enumerable.Range(1, total).Select(s => (long)s));
    }

    [Fact]
    public async Task MultiDay_full_outage_flushes_and_converges_in_one_sync_cycle()
    {
        const int days = 4;
        const int perDay = 50;
        const int total = days * perDay; // 200 messages queued across 4 days offline

        var sender = NewEngine();
        var receiver = NewEngine();

        // The edge queues several days of traffic while offline; nothing leaves the durable outbox.
        for (var s = 1; s <= total; s++)
        {
            await sender.Outbox.EnqueueAsync(GatewayId, MessageItem(s));
        }

        // Reconnect: the first (single) sync cycle pushes the entire backlog in one batch.
        var push = await sender.Engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = GatewayId,
            Now = Now,
            Limit = Batch.BatchSequenceValidator.MaxItemsPerBatch,
        });

        Assert.Equal(total, push.Batch.Items!.Count);
        Assert.Equal(1, push.Batch.SequenceStart);
        Assert.Equal(total, push.Batch.SequenceEnd);

        var apply = await ApplyAsync(receiver.Engine, push.Batch);

        // The receiver converged in one sync cycle: contiguous cursor == high watermark == total, no gaps
        // (product vision §11: "all messages converge within one sync cycle after reconnect").
        Assert.True(apply.Convergence!.IsConverged);
        Assert.Equal(total, apply.Position.Sequence);
        Assert.Equal(total, apply.Convergence.HighWatermark);
        Assert.Empty(apply.Convergence.Gaps);
        Assert.Equal(total, apply.DeliveryAcks.Count);

        // 0 lost / 0 duplicate (product vision §11).
        await AssertExactlyOnceAsync(receiver.Inbox, Enumerable.Range(1, total).Select(s => (long)s));
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private static (SyncEngine Engine, InMemoryOutbox Outbox, InMemoryInbox Inbox, InMemoryIdempotencyStore Idempotency) NewEngine()
    {
        var outbox = new InMemoryOutbox();
        var inbox = new InMemoryInbox();
        var idempotency = new InMemoryIdempotencyStore();
        return (new SyncEngine(outbox, inbox, idempotency), outbox, inbox, idempotency);
    }

    private static string MessageId(long sequence) => $"msg-{sequence:D5}";

    private static SyncItem MessageItem(long sequence)
        => TestData.MessageItem(TestData.NewMessage(MessageId(sequence)), sequence);

    private static SyncBatch MakeBatch(string batchId, SyncItem[] items, string? sinceCursor = null)
    {
        var ordered = items.OrderBy(i => i.Sequence).ToArray();
        return new SyncBatch
        {
            BatchId = batchId,
            GatewayId = GatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = sinceCursor,
            IdempotencyKey = IdempotencyKeys.Derive(batchId, ordered),
            SequenceStart = ordered.Length == 0 ? null : ordered[0].Sequence,
            SequenceEnd = ordered.Length == 0 ? null : ordered[^1].Sequence,
            Items = ordered.ToList(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
    }

    private static Task<ApplyBatchResult> ApplyAsync(SyncEngine engine, SyncBatch batch)
        => engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

    /// <summary>Shreds a distinct sequence set into random-length batches (order-independent, non-overlapping).</summary>
    private static List<SyncBatch> RandomBatches(string batchPrefix, long[] sequences, Random rng)
    {
        var shuffled = sequences.OrderBy(_ => rng.Next()).ToArray();
        var batches = new List<SyncBatch>();

        var i = 0;
        var batchIndex = 0;
        while (i < shuffled.Length)
        {
            var chunk = Math.Min(shuffled.Length - i, rng.Next(1, 8)); // 1..7 items per batch
            var items = shuffled.Skip(i).Take(chunk).Select(MessageItem).ToArray();
            batches.Add(MakeBatch($"{batchPrefix}-{batchIndex++:D3}", items));
            i += chunk;
        }

        return batches;
    }

    private static async Task<ApplyBatchResult> ApplyInOrderAsync(SyncEngine engine, IReadOnlyList<SyncBatch> batches, Random rng)
    {
        ApplyBatchResult? last = null;
        foreach (var batch in batches.OrderBy(_ => rng.Next()))
        {
            last = await ApplyAsync(engine, batch);
        }

        return last!;
    }

    private static async Task AssertExactlyOnceAsync(InMemoryInbox inbox, IEnumerable<long> expectedSequences)
    {
        foreach (var sequence in expectedSequences)
        {
            var id = MessageId(sequence);
            Assert.True(await inbox.ContainsMessageAsync(id), $"message {id} was lost");
            Assert.Single(await inbox.GetByMessageAsync(id));
        }
    }
}
