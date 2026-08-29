using HumanGateway.Core.Hashing;
using HumanGateway.Core.Idempotency;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Property tests for idempotency-key derivation and the idempotency store (SYNC-FR-02, NF-05): a replayed
/// batch derives the same key, a changed batch derives a different one, and the store collapses replays.
/// </summary>
public class IdempotencyTests
{
    private static SyncItem MessageItem(long sequence, string id)
        => TestData.MessageItem(TestData.NewMessage(id), sequence);

    [Fact]
    public void Derive_is_deterministic_for_same_batch_id_and_items()
    {
        var items = new[] { MessageItem(1, "msg-0001"), MessageItem(2, "msg-0002") };
        Assert.Equal(
            IdempotencyKeys.Derive("batch-0001", items),
            IdempotencyKeys.Derive("batch-0001", items));
    }

    [Fact]
    public void Derive_differs_when_batch_id_changes()
    {
        var items = new[] { MessageItem(1, "msg-0001") };
        Assert.NotEqual(
            IdempotencyKeys.Derive("batch-0001", items),
            IdempotencyKeys.Derive("batch-0002", items));
    }

    [Fact]
    public void Derive_is_stable_when_payload_changes_but_durable_identity_is_unchanged()
    {
        // The idempotency key identifies a *logical batch* by durable identity (batchId + item IDs), not by
        // payload bytes: a retry of the same message derives the same key so the receiver collapses the replay
        // (SYNC-FR-02, NF-05). Payload integrity is the content hash's job, verified separately (also
        // SYNC-FR-02) — mutating a body leaves the key unchanged but is still detectable via the hash.
        var message = TestData.NewMessage("msg-0001", body: "hello");
        var changed = message with { Payload = message.Payload with { Body = "changed" } };
        changed = changed with { ContentHash = ContentHasher.ComputeMessageHash(changed) };

        var a = IdempotencyKeys.Derive("batch-0001", new[] { TestData.MessageItem(message, 1) });
        var b = IdempotencyKeys.Derive("batch-0001", new[] { TestData.MessageItem(changed, 1) });
        Assert.Equal(a, b); // same durable identity → same logical batch

        // The payload difference is still detected by the content hash.
        Assert.NotEqual(message.ContentHash, changed.ContentHash);
    }

    [Fact]
    public void Derive_differs_when_item_id_changes()
    {
        Assert.NotEqual(
            IdempotencyKeys.Derive("batch-0001", new[] { MessageItem(1, "msg-0001") }),
            IdempotencyKeys.Derive("batch-0001", new[] { MessageItem(1, "msg-0002") }));
    }

    [Fact]
    public void Derive_matches_schema_idempotency_key_shape()
    {
        var key = IdempotencyKeys.Derive("batch-0001", new[] { MessageItem(1, "msg-0001") });
        Assert.Matches("^[A-Za-z0-9._:-]+$", key);
    }

    [Fact]
    public async Task Store_collapses_replayed_batches()
    {
        var store = new InMemoryIdempotencyStore();
        Assert.False(await store.WasAppliedAsync("batch-0001", "key-0001"));

        await store.RecordAsync("batch-0001", "key-0001");
        Assert.True(await store.WasAppliedAsync("batch-0001", "key-0001"));

        // Recording again is a no-op (idempotent), not an error.
        await store.RecordAsync("batch-0001", "key-0001");
        Assert.True(await store.WasAppliedAsync("batch-0001", "key-0001"));
    }

    [Fact]
    public async Task Store_distinguishes_batch_id_and_key()
    {
        var store = new InMemoryIdempotencyStore();
        await store.RecordAsync("batch-0001", "key-0001");

        // Same key under a different batch, or a different key under the same batch, is a different logical batch.
        Assert.False(await store.WasAppliedAsync("batch-0001", "key-0002"));
        Assert.False(await store.WasAppliedAsync("batch-0002", "key-0001"));
    }
}
