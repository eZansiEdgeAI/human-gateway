using HumanGateway.Core.Hashing;
using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Idempotency;

/// <summary>
/// Deterministic idempotency-key derivation (SYNC-FR-02). The key identifies a <em>logical batch</em> by
/// durable identity — the batch ID plus each item's kind, sequence, and durable ID — not by payload bytes.
/// A retry that re-sends the same items therefore derives the same key (so the receiver collapses it as a
/// replay), while a change to the batch ID or to an item's durable identity yields a new logical batch.
/// Payload integrity (tamper/change detection) is the content hash's job, verified separately (also
/// SYNC-FR-02). The result is a <c>sha256:&lt;hex&gt;</c> hash, matching the schema's
/// <c>^[A-Za-z0-9._:-]+$</c> shape.
/// </summary>
public static class IdempotencyKeys
{
    /// <summary>
    /// Derives an idempotency key from the batch identity and the ordered items' durable identity
    /// (kind + sequence + durable ID). A replayed batch derives the same key; changing the batch ID or an
    /// item's durable identity (e.g. a message ID) yields a new key. Mutating an item's payload leaves the
    /// key unchanged.
    /// </summary>
    public static string Derive(string batchId, IEnumerable<SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(batchId);
        ArgumentNullException.ThrowIfNull(items);

        var identities = string.Join("\u0001", items.Select(ItemDurableIdentity));
        var canonical = batchId + "\u0000" + identities;
        return ContentHasher.ComputeUtf8(canonical);
    }

    /// <summary>
    /// The durable identity of one sync item: its kind discriminator, sequence number, and durable ID. The
    /// payload (message body, delivery state/timestamps, artifact bytes, ack timestamp) is deliberately
    /// excluded so a retry of the same logical item derives the same key.
    /// </summary>
    private static string ItemDurableIdentity(SyncItem item)
    {
        var durableId = item.Kind switch
        {
            SyncItemKind.Message => item.Message?.Id,
            SyncItemKind.Delivery => item.Delivery?.Id,
            SyncItemKind.Artifact => item.Artifact?.Id,
            SyncItemKind.Ack => item.Ack is null
                ? null
                : item.Ack.MessageId + "\u0000" + item.Ack.Recipient?.Address,
            _ => null,
        };

        return item.Kind + "\u0000" + item.Sequence + "\u0000" + durableId;
    }
}
