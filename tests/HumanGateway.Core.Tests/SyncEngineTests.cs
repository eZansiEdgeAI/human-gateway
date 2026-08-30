using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// End-to-end tests of the <see cref="SyncEngine"/> over in-memory ports, covering the SYNC-FR-01..07
/// contract: push/apply round-trip, idempotent replay, deterministic reordering, delivery acknowledgements,
/// and cursor-based convergence (contiguous advancement from the full applied history, and out-of-order gaps).
/// </summary>
public class SyncEngineTests
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-000000000001";

    private static readonly DateTimeOffset Now = TestData.FixedNow;
    private static readonly Participant Receiver = TestData.Human("human:bob@school.example");

    private static SyncItem MessageItem(long sequence, string id)
        => TestData.MessageItem(TestData.NewMessage(id), sequence);

    private static (SyncEngine Engine, InMemoryOutbox Outbox, InMemoryInbox Inbox, InMemoryIdempotencyStore Idempotency) NewEngine()
    {
        var outbox = new InMemoryOutbox();
        var inbox = new InMemoryInbox();
        var idempotency = new InMemoryIdempotencyStore();
        return (new SyncEngine(outbox, inbox, idempotency), outbox, inbox, idempotency);
    }

    private static SyncBatch MakeBatch(string batchId, SyncItem[] items, string? sinceCursor = null)
    {
        var ordered = items.OrderBy(i => i.Sequence).ToArray();
        return new SyncBatch
        {
            BatchId = batchId,
            GatewayId = GatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = sinceCursor,
            IdempotencyKey = IdempotencyKeys.Derive(batchId, items),
            SequenceStart = ordered.Length == 0 ? null : ordered[0].Sequence,
            SequenceEnd = ordered.Length == 0 ? null : ordered[^1].Sequence,
            Items = ordered.ToList(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
    }

    [Fact]
    public async Task BuildPushBatch_returns_empty_keepalive_when_outbox_empty()
    {
        var (engine, _, _, _) = NewEngine();

        var result = await engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = GatewayId,
            Now = Now,
        });

        Assert.Empty(result.EntryIds);
        Assert.Empty(result.Batch.Items!);
        Assert.Null(result.Batch.SequenceStart);
        Assert.Null(result.Batch.SequenceEnd);
        Assert.Equal(0, result.AfterSequence);
    }

    [Fact]
    public async Task BuildPushBatch_returns_pending_items_in_sequence_order()
    {
        var (engine, outbox, _, _) = NewEngine();
        await outbox.EnqueueAsync(GatewayId, MessageItem(2, "msg-0002"));
        await outbox.EnqueueAsync(GatewayId, MessageItem(1, "msg-0001"));
        await outbox.EnqueueAsync(GatewayId, MessageItem(3, "msg-0003"));

        var result = await engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = GatewayId,
            Now = Now,
        });

        Assert.Equal(new long[] { 1, 2, 3 }, result.Batch.Items!.Select(i => i.Sequence).ToArray());
        Assert.Equal(1, result.Batch.SequenceStart);
        Assert.Equal(3, result.Batch.SequenceEnd);
        Assert.Equal(3, result.EntryIds.Count);
        Assert.Equal(3, result.AfterSequence);
        Assert.NotNull(result.Batch.IdempotencyKey);
    }

    [Fact]
    public async Task BuildPushBatch_only_pushes_entries_after_the_receiver_cursor()
    {
        var (engine, outbox, _, _) = NewEngine();
        for (var s = 1; s <= 5; s++)
        {
            await outbox.EnqueueAsync(GatewayId, MessageItem(s, $"msg-{s:D4}"));
        }

        // Receiver has already consumed through sequence 3 → the next push starts at 4.
        var result = await engine.BuildPushBatchAsync(new BuildPushBatchRequest
        {
            GatewayId = GatewayId,
            SinceCursor = HumanGateway.Core.Cursor.CursorCodec.Encode(new HumanGateway.Core.Cursor.CursorPosition(3)),
            Now = Now,
        });

        Assert.Equal(new long[] { 4, 5 }, result.Batch.Items!.Select(i => i.Sequence).ToArray());
        Assert.Equal(4, result.Batch.SequenceStart);
        Assert.Equal(5, result.Batch.SequenceEnd);
    }

    [Fact]
    public async Task ApplyBatch_applies_items_and_builds_delivery_acks()
    {
        var (engine, _, _, _) = NewEngine();
        var batch = MakeBatch("batch-0001", new[]
        {
            MessageItem(1, "msg-0001"),
            MessageItem(2, "msg-0002"),
        });

        var result = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        Assert.True(result.IsValid);
        Assert.False(result.IsDuplicate);
        Assert.Equal(new long[] { 1, 2 }, result.AppliedItems.Select(i => i.Sequence).ToArray());
        Assert.Equal(2, result.DeliveryAcks.Count);
        Assert.All(result.DeliveryAcks, ack =>
        {
            Assert.Equal(DeliveryAckState.Delivered, ack.State);
            Assert.Equal(Receiver.Address, ack.Recipient.Address);
        });
        Assert.Equal(2, result.Position.Sequence);
    }

    [Fact]
    public async Task ApplyBatch_surfaces_inbound_delivery_acks()
    {
        var (engine, _, _, _) = NewEngine();
        var ack = new DeliveryAck
        {
            MessageId = "msg-0001",
            Recipient = Receiver,
            State = DeliveryAckState.Delivered,
            AcknowledgedAt = "2026-08-29T00:00:00.000Z",
        };
        var batch = MakeBatch("batch-0001", new[]
        {
            new SyncItem { Kind = SyncItemKind.Ack, Sequence = 1, Ack = ack },
        });

        var result = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        // The acknowledgement returned to *this* gateway is surfaced for delivery-record wiring (SYNC-FR-05).
        var received = Assert.Single(result.ReceivedAcks);
        Assert.Equal("msg-0001", received.MessageId);
        Assert.Equal(DeliveryAckState.Delivered, received.State);

        // It is also durably recorded in the inbox (a non-message item is not subject to message dedup).
        Assert.Equal(1, result.Position.Sequence);
    }

    [Fact]
    public async Task ApplyBatch_replayed_batch_is_idempotent()
    {
        var (engine, _, inbox, _) = NewEngine();
        var batch = MakeBatch("batch-0001", new[] { MessageItem(1, "msg-0001") });

        var first = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        var second = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        Assert.True(first.IsValid);
        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Empty(second.AppliedItems);

        // The message was recorded exactly once despite the replay (exactly-once effect, NF-05).
        Assert.True(await inbox.ContainsMessageAsync("msg-0001"));
        Assert.Single(await inbox.GetByMessageAsync("msg-0001"));
    }

    [Fact]
    public async Task ApplyBatch_deduplicates_message_seen_in_a_different_batch()
    {
        var (engine, _, inbox, _) = NewEngine();
        var batchA = MakeBatch("batch-0001", new[] { MessageItem(1, "msg-0001") });
        var batchB = MakeBatch("batch-0002", new[] { MessageItem(2, "msg-0001") });

        await engine.ApplyBatchAsync(batchA, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        var second = await engine.ApplyBatchAsync(batchB, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        // The replayed message (under a different batch id) has no second effect.
        Assert.DoesNotContain(second.AppliedItems, i => i.Message?.Id == "msg-0001");
        Assert.Single(await inbox.GetByMessageAsync("msg-0001"));
    }

    [Fact]
    public async Task ApplyBatch_reorders_out_of_order_items()
    {
        var (engine, _, _, _) = NewEngine();
        var batch = MakeBatch("batch-0001", new[]
        {
            MessageItem(3, "msg-0003"),
            MessageItem(1, "msg-0001"),
            MessageItem(2, "msg-0002"),
        });

        var result = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        Assert.Equal(new long[] { 1, 2, 3 }, result.AppliedItems.Select(i => i.Sequence).ToArray());
    }

    [Fact]
    public async Task ApplyBatch_rejects_invalid_batch_shape()
    {
        var (engine, _, _, _) = NewEngine();
        var batch = MakeBatch("batch-0001", new[] { MessageItem(1, "msg-0001") })
            with { SequenceStart = null, SequenceEnd = null }; // non-empty batch without a range

        var result = await engine.ApplyBatchAsync(batch, new ApplyBatchRequest { Receiver = Receiver, Now = Now });

        Assert.False(result.IsValid);
        Assert.Contains(Batch.BatchSequenceValidator.SequenceRangeRequired, result.Violations);
    }

    [Fact]
    public async Task ApplyBatch_advances_cursor_contiguously_across_batches()
    {
        var (engine, _, _, _) = NewEngine();

        var batch1 = MakeBatch("batch-0001", new[]
        {
            MessageItem(1, "msg-0001"),
            MessageItem(2, "msg-0002"),
        });

        var first = await engine.ApplyBatchAsync(batch1, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        Assert.Equal(2, first.Position.Sequence);

        // Second batch continues the stream; the cursor is the contiguous high-watermark of the full applied
        // history, so it advances past the first batch without re-reading anything.
        var batch2 = MakeBatch("batch-0002", new[]
        {
            MessageItem(3, "msg-0003"),
            MessageItem(4, "msg-0004"),
        }, sinceCursor: first.Cursor);

        var second = await engine.ApplyBatchAsync(batch2, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        Assert.Equal(4, second.Position.Sequence);
    }

    [Fact]
    public async Task OutOfOrder_converges_without_loss_or_duplication()
    {
        var (engine, _, inbox, _) = NewEngine();

        // Batch arriving with a gap ({1, 3}): both items applied; the cursor only advances contiguously to 1.
        var batch1 = MakeBatch("batch-0001", new[]
        {
            MessageItem(3, "msg-0003"),
            MessageItem(1, "msg-0001"),
        });
        var r1 = await engine.ApplyBatchAsync(batch1, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        Assert.Equal(new long[] { 1, 3 }, r1.AppliedItems.Select(i => i.Sequence).ToArray());
        Assert.Equal(1, r1.Position.Sequence);

        // The gap (2) is filled in a later batch. Because the engine computes the cursor from the full applied
        // history, the cursor converges to the true contiguous high-watermark (3) in this single follow-up batch.
        var batch2 = MakeBatch("batch-0002", new[] { MessageItem(2, "msg-0002") }, sinceCursor: r1.Cursor);
        var r2 = await engine.ApplyBatchAsync(batch2, new ApplyBatchRequest { Receiver = Receiver, Now = Now });
        Assert.Equal(new long[] { 2 }, r2.AppliedItems.Select(i => i.Sequence).ToArray());
        Assert.Equal(3, r2.Position.Sequence);

        // Every message landed exactly once — no loss, no duplication.
        Assert.True(await inbox.ContainsMessageAsync("msg-0001"));
        Assert.True(await inbox.ContainsMessageAsync("msg-0002"));
        Assert.True(await inbox.ContainsMessageAsync("msg-0003"));
        Assert.Single(await inbox.GetByMessageAsync("msg-0003"));
    }

    [Fact]
    public async Task BackoffPolicy_computes_capped_jittered_delays()
    {
        var policy = Retry.BackoffPolicy.Default;

        // Deterministic seed → same delay.
        var a = policy.NextDelay(0, new Random(42));
        var b = policy.NextDelay(0, new Random(42));
        Assert.Equal(a, b);

        // Capped: no delay exceeds MaxDelay.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            Assert.InRange(policy.NextDelay(attempt, new Random(attempt)), TimeSpan.Zero, policy.MaxDelay);
        }

        // Retry budget honoured.
        Assert.True(policy.ShouldRetry(7));
        Assert.False(policy.ShouldRetry(8));
    }
}
