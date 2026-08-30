using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Ordering;

/// <summary>
/// Deterministic reordering by per-gateway sequence number (SYNC-FR-07). Out-of-order arrival is normal;
/// items are reordered by their monotonic sequence and never dropped. Gaps are allowed (retries) and
/// preserved — contiguity is not required.
/// </summary>
/// <remarks>
/// <para>
/// Determinism is <em>order-independent</em>: the same multiset of items always reorders to the same
/// sequence regardless of arrival order, so <c>Reorder(shuffle(x)) == Reorder(x)</c> for any permutation.
/// This is a stronger guarantee than a plain stable sort, which would leak the arrival order into the result
/// whenever two items share a sequence number (which must not happen per gateway, but can arrive when a
/// retry overlaps, or when batches from distinct sources are merged).
/// </para>
/// <para>
/// Equal sequence numbers are therefore tie-broken by a <em>stable payload identity</em> derived from the
/// item's durable ID (the message, delivery, artifact, or ack ID), never by input position. The result is a
/// pure function of the item contents, which is what makes the reorder safe to property-test by shuffling.
/// </para>
/// </remarks>
public static class SequenceOrdering
{
    /// <summary>
    /// Reorders items by ascending <see cref="SyncItem.Sequence"/>, tie-breaking equal sequences by the
    /// item's stable payload identity (ordinal). The result is deterministic regardless of input order.
    /// Gaps are preserved and no item is ever dropped.
    /// </summary>
    public static IReadOnlyList<SyncItem> Reorder(IEnumerable<SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .OrderBy(i => i.Sequence)
            .ThenBy(StableIdentity, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Reorders items across multiple gateways by the composite ordering key
    /// <c>(gatewayId, sequence, identity)</c> (syncbatch.schema.json#/$defs/sequence). Deterministic
    /// regardless of input order; gateway identity compares ordinal.
    /// </summary>
    public static IReadOnlyList<SyncItem> Reorder(IEnumerable<(string GatewayId, SyncItem Item)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .OrderBy(x => x.GatewayId, StringComparer.Ordinal)
            .ThenBy(x => x.Item.Sequence)
            .ThenBy(x => StableIdentity(x.Item), StringComparer.Ordinal)
            .Select(x => x.Item)
            .ToList();
    }

    /// <summary>
    /// A stable, order-independent identity for an item, used only to break ties between equal sequence
    /// numbers. Derived from the item's durable payload ID (message, delivery, artifact, or ack). Items
    /// without a usable ID fall back to the empty string, which sorts before identified items deterministically.
    /// </summary>
    public static string StableIdentity(SyncItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            SyncItemKind.Message => item.Message?.Id ?? string.Empty,
            SyncItemKind.Delivery => item.Delivery?.Id ?? string.Empty,
            SyncItemKind.Artifact => item.Artifact?.Id ?? string.Empty,
            SyncItemKind.Ack => item.Ack?.MessageId ?? string.Empty,
            _ => string.Empty,
        };
    }
}
