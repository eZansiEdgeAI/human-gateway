using HumanGateway.Core.Cursor;
using HumanGateway.Core.Idempotency;
using HumanGateway.Core.Inbox;
using HumanGateway.Core.Outbox;
using HumanGateway.Core.Sync;
using HumanGateway.Edge.Api;
using HumanGateway.Edge.Sync;
using HumanGateway.Protocol.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HumanGateway.Edge.Tests;

/// <summary>
/// Background sync worker tests (LOCAL-EDGE-1.6, SYNCHRONISATION-3.2, EDGE-FR-05, product vision §10): drives
/// the <see cref="SyncWorker"/> through a fake outbound transport over the in-memory stores and the real
/// <see cref="SyncEngine"/>, verifying the durable-outbox flush (write-then-ack), the pull-and-apply path with
/// delivery-ack enqueue, durable push/pull cursor persistence across a restart, idempotency-key reuse on retry,
/// offline no-op behaviour, and the STARTING → RECOVERING → STARTED → SYNCING → STOPPING lifecycle.
/// </summary>
public sealed class SyncWorkerTests
{
    private const string GatewayId = "edge:00000000-0000-0000-0000-00000000sync";

    private static readonly CancellationToken Ct = CancellationToken.None;

    // -----------------------------------------------------------------------------------------------
    // Push
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncOnce_PushesPendingOutbox_AndMarksEntriesSent()
    {
        var relay = new FakeRelaySyncClient();
        var (worker, outbox, _, _) = NewHarness(relay);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));
        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0002"));

        await worker.SyncOnceAsync(Ct);

        // The Relay received exactly one push batch carrying both entries.
        var pushed = Assert.Single(relay.PushedBatches);
        Assert.Equal(2, pushed.Items!.Count);

        // The durable outbox is drained: every covered entry was marked sent only after the Relay acked.
        Assert.Empty(await outbox.GetPendingAsync(GatewayId, 0, 100));
    }

    [Fact]
    public async Task SyncOnce_OnTransportFailure_RetainsOutboxEntries()
    {
        var relay = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromException<PushResult>(new InvalidOperationException("network down")),
        };
        var (worker, outbox, _, _) = NewHarness(relay);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.SyncOnceAsync(Ct));

        // Nothing was marked sent — the entry is retained for the next retry (EDGE-FR-04).
        var pending = await outbox.GetPendingAsync(GatewayId, 0, 100);
        Assert.Single(pending);
        Assert.Equal("msg-0001", pending[0].Item.Message!.Id);
    }

    [Fact]
    public async Task SyncOnce_EchoesRelayCursor_OnNextPush()
    {
        var relay = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromResult(new PushResult { Cursor = "v1:relay-cursor" }),
        };
        var (worker, outbox, _, _) = NewHarness(relay);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));
        await worker.SyncOnceAsync(Ct);

        // Second cycle: the outbox is empty, so the worker pushes a keepalive echoing the Relay's cursor.
        await worker.SyncOnceAsync(Ct);

        Assert.Equal(2, relay.PushedBatches.Count);
        Assert.Equal("v1:relay-cursor", relay.PushedBatches[1].SinceCursor);
    }

    // -----------------------------------------------------------------------------------------------
    // Durable push/pull cursors (SYNC-FR-03)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncOnce_PersistsPushCursor_AcrossWorkerRestart()
    {
        var cursors = new InMemorySyncCursorStore();
        var relay1 = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromResult(new PushResult { Cursor = "v1:relay-cursor" }),
        };
        var (worker1, outbox, _, _) = NewHarness(relay1, cursors: cursors);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));
        await worker1.SyncOnceAsync(Ct);

        // A brand-new worker (simulating a process restart) over the same durable cursor store resumes from the
        // persisted push cursor rather than restarting from the start position (NF-02, SYNC-FR-03).
        var relay2 = new FakeRelaySyncClient();
        var (worker2, _, _, _) = NewHarness(relay2, cursors: cursors);
        await worker2.SyncOnceAsync(Ct);

        var keepalive = Assert.Single(relay2.PushedBatches);
        Assert.Equal("v1:relay-cursor", keepalive.SinceCursor);
    }

    [Fact]
    public async Task SyncOnce_PersistsPullCursor_AcrossWorkerRestart()
    {
        var remote = NewMessage("msg-remote");
        var item = new SyncItem { Kind = SyncItemKind.Message, Sequence = 1, Message = remote };
        var inbound = new SyncBatch
        {
            BatchId = "batch-pull-1",
            GatewayId = "edge:relay-gateway",
            Direction = BatchDirection.Pull,
            IdempotencyKey = IdempotencyKeys.Derive("batch-pull-1", new[] { item }),
            SequenceStart = 1,
            SequenceEnd = 1,
            Items = new List<SyncItem> { item },
            CreatedAt = "2026-08-30T00:00:00.000Z",
        };

        var cursors = new InMemorySyncCursorStore();
        var relay1 = new FakeRelaySyncClient
        {
            OnPull = (_, _) => Task.FromResult<SyncBatch?>(inbound),
        };
        var (worker1, _, inbox, _) = NewHarness(relay1, cursors: cursors);

        await worker1.SyncOnceAsync(Ct);
        Assert.True(await inbox.ContainsMessageAsync("msg-remote"));

        // A new worker resumes from the persisted pull cursor: its first pull echoes that cursor (incremental
        // sync — never re-pull from the start position, NF-02).
        var relay2 = new FakeRelaySyncClient();
        var (worker2, _, _, _) = NewHarness(relay2, cursors: cursors);
        await worker2.SyncOnceAsync(Ct);

        var echoedCursor = Assert.Single(relay2.PullCursors);
        Assert.Equal(1, CursorCodec.TryDecode(echoedCursor)!.Value.Sequence);
    }

    // -----------------------------------------------------------------------------------------------
    // Idempotency keys on retry (SYNC-FR-02)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncOnce_OnTransportFailure_ThenRetry_ReusesBatchIdentity()
    {
        var relay = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromException<PushResult>(new InvalidOperationException("network down")),
        };
        var (worker, outbox, _, _) = NewHarness(relay);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.SyncOnceAsync(Ct));

        // The network recovers: the retry reuses the in-flight batch's durable identity (SYNC-FR-02, NF-05),
        // so the Relay can collapse it as a replay rather than a brand-new logical batch.
        relay.OnPush = (_, _) => Task.FromResult(new PushResult { Cursor = "v1:relay-cursor" });
        await worker.SyncOnceAsync(Ct);

        Assert.Equal(2, relay.PushedBatches.Count);
        Assert.Equal(relay.PushedBatches[0].BatchId, relay.PushedBatches[1].BatchId);
        Assert.Equal(relay.PushedBatches[0].IdempotencyKey, relay.PushedBatches[1].IdempotencyKey);
    }

    [Fact]
    public async Task SyncOnce_OnTransportFailure_WithNewEntry_DoesNotReuseBatchIdentity()
    {
        var relay = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromException<PushResult>(new InvalidOperationException("network down")),
        };
        var (worker, outbox, _, _) = NewHarness(relay);

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.SyncOnceAsync(Ct));

        // A new message is created locally while the Relay is still unreachable. The pending set grew, so the
        // next push must be a *new* logical batch (a fresh identity), never fold the new message into the
        // already-(possibly-)sent batch's idempotency key (which would drop it as a replay).
        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0002"));

        relay.OnPush = (_, _) => Task.FromResult(new PushResult { Cursor = "v1:relay-cursor" });
        await worker.SyncOnceAsync(Ct);

        Assert.Equal(2, relay.PushedBatches.Count);
        Assert.NotEqual(relay.PushedBatches[0].BatchId, relay.PushedBatches[1].BatchId);
        Assert.NotEqual(relay.PushedBatches[0].IdempotencyKey, relay.PushedBatches[1].IdempotencyKey);
        // The retry carries both messages — the new one is not silently dropped.
        Assert.Equal(2, relay.PushedBatches[1].Items!.Count);
    }

    // -----------------------------------------------------------------------------------------------
    // Pull
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncOnce_AppliesPulledBatch_AndEnqueuesDeliveryAcks()
    {
        var remote = NewMessage("msg-remote");
        var item = new SyncItem { Kind = SyncItemKind.Message, Sequence = 1, Message = remote };
        var inbound = new SyncBatch
        {
            BatchId = "batch-pull-1",
            GatewayId = "edge:relay-gateway",
            Direction = BatchDirection.Pull,
            IdempotencyKey = IdempotencyKeys.Derive("batch-pull-1", new[] { item }),
            SequenceStart = 1,
            SequenceEnd = 1,
            Items = new List<SyncItem> { item },
            CreatedAt = "2026-08-30T00:00:00.000Z",
        };

        var relay = new FakeRelaySyncClient
        {
            OnPull = (_, _) => Task.FromResult<SyncBatch?>(inbound),
        };
        var (worker, outbox, inbox, _) = NewHarness(relay);

        await worker.SyncOnceAsync(Ct);

        // The inbound message was applied durably.
        Assert.True(await inbox.ContainsMessageAsync("msg-remote"));

        // A delivery acknowledgement was enqueued to flow back to the Relay on the next push (SYNC-FR-05).
        var pending = await outbox.GetPendingAsync(GatewayId, 0, 100);
        var ack = Assert.Single(pending);
        Assert.Equal(SyncItemKind.Ack, ack.Item.Kind);
        Assert.Equal("msg-remote", ack.Item.Ack!.MessageId);
    }

    [Fact]
    public async Task SyncOnce_SkipsInvalidPulledBatch_WithoutApplying()
    {
        // A non-empty batch with no declared sequence range violates the batch-shape invariants.
        var invalid = new SyncBatch
        {
            BatchId = "batch-bad",
            GatewayId = "edge:relay-gateway",
            Direction = BatchDirection.Pull,
            IdempotencyKey = "key-bad",
            Items = new List<SyncItem> { new() { Kind = SyncItemKind.Message, Sequence = 1, Message = NewMessage("msg-bad") } },
            CreatedAt = "2026-08-30T00:00:00.000Z",
        };

        var relay = new FakeRelaySyncClient
        {
            OnPull = (_, _) => Task.FromResult<SyncBatch?>(invalid),
        };
        var (worker, _, inbox, _) = NewHarness(relay);

        await worker.SyncOnceAsync(Ct);

        Assert.False(await inbox.ContainsMessageAsync("msg-bad"));
    }

    // -----------------------------------------------------------------------------------------------
    // Offline / no Relay
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SyncOnce_WhenRelayNotConfigured_IsANoOp()
    {
        var (worker, outbox, _, _) = NewHarness(new DisabledRelaySyncClient());

        await outbox.EnqueueAsync(GatewayId, MessageItem("msg-0001"));

        await worker.SyncOnceAsync(Ct);

        // Outbound sync is disabled: nothing is sent and nothing is dropped (offline-first, NF-01).
        Assert.Single(await outbox.GetPendingAsync(GatewayId, 0, 100));
        Assert.Null(worker.LastSyncAtUtc);
    }

    // -----------------------------------------------------------------------------------------------
    // Lifecycle (product vision §10)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TransitionsThroughLifecycle_AndStops()
    {
        var relay = new FakeRelaySyncClient
        {
            OnPush = (_, _) => Task.FromResult(new PushResult { Cursor = "v1:relay-cursor" }),
        };
        var (worker, _, _, _) = NewHarness(relay, pollSeconds: 1);

        await worker.StartAsync(CancellationToken.None);

        // The first cycle runs immediately (before the poll delay) and pushes a keepalive for the empty outbox.
        await WaitUntilAsync(() => worker.LastSyncAtUtc is not null, TimeSpan.FromSeconds(10));
        Assert.NotEmpty(relay.PushedBatches);

        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(SyncWorkerState.Stopped, worker.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static SyncItem MessageItem(string id) => new()
    {
        Kind = SyncItemKind.Message,
        Sequence = 0,
        Message = NewMessage(id),
    };

    private static Message NewMessage(string id) => TestData.NewMessage(id: id);

    private static (SyncWorker Worker, InMemoryOutbox Outbox, InMemoryInbox Inbox, InMemorySyncCursorStore Cursors) NewHarness(
        IRelaySyncClient relay,
        int pollSeconds = 30,
        string gatewayId = GatewayId,
        InMemorySyncCursorStore? cursors = null)
    {
        var outbox = new InMemoryOutbox();
        var inbox = new InMemoryInbox();
        var idempotency = new InMemoryIdempotencyStore();
        cursors ??= new InMemorySyncCursorStore();
        var engine = new SyncEngine(outbox, inbox, idempotency);

        var gateway = Options.Create(new GatewayOptions { GatewayId = gatewayId });
        var options = Options.Create(new SyncWorkerOptions { BatchSize = 100, PollIntervalSeconds = pollSeconds });

        var worker = new SyncWorker(
            engine,
            outbox,
            cursors,
            relay,
            gateway,
            options,
            NullLogger<SyncWorker>.Instance,
            TimeProvider.System);

        return (worker, outbox, inbox, cursors);
    }

    /// <summary>Configurable in-memory <see cref="IRelaySyncClient"/> recording every push/pull call.</summary>
    private sealed class FakeRelaySyncClient : IRelaySyncClient
    {
        public bool IsConfigured { get; init; } = true;

        public List<SyncBatch> PushedBatches { get; } = new();

        public List<string?> PullCursors { get; } = new();

        public Func<SyncBatch, CancellationToken, Task<PushResult>>? OnPush { get; set; }

        public Func<string?, CancellationToken, Task<SyncBatch?>>? OnPull { get; set; }

        public async Task<PushResult> PushAsync(SyncBatch batch, CancellationToken ct = default)
        {
            PushedBatches.Add(batch);
            return await (OnPush?.Invoke(batch, ct) ?? Task.FromResult(new PushResult())).ConfigureAwait(false);
        }

        public async Task<SyncBatch?> PullAsync(string? sinceCursor, CancellationToken ct = default)
        {
            PullCursors.Add(sinceCursor);
            return await (OnPull?.Invoke(sinceCursor, ct) ?? Task.FromResult<SyncBatch?>(null)).ConfigureAwait(false);
        }
    }
}
