using HumanGateway.Protocol.Models;

namespace HumanGateway.Core.Ordering;

/// <summary>
/// Deterministic reordering by per-gateway sequence number (SYNC-FR-07). Out-of-order arrival is normal;
/// items are reordered by their monotonic sequence and never dropped. Gaps are allowed (retries) and
/// preserved — contiguity is not required.
/// </summary>
public static class SequenceOrdering
{
    /// <summary>
    /// Stable-sorts items by ascending <see cref="SyncItem.Sequence"/>. Items with equal sequence retain
    /// their input order (LINQ-to-objects <c>OrderBy</c> is a stable sort), so the result is deterministic.
    /// </summary>
    public static IReadOnlyList<SyncItem> Reorder(IEnumerable<SyncItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.OrderBy(i => i.Sequence).ToList();
    }

    /// <summary>
    /// Reorders items across multiple gateways by the composite ordering key <c>(gatewayId, sequence)</c>
    /// (syncbatch.schema.json#/$defs/sequence).
    /// </summary>
    public static IReadOnlyList<SyncItem> Reorder(IEnumerable<(string GatewayId, SyncItem Item)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .OrderBy(x => x.GatewayId, StringComparer.Ordinal)
            .ThenBy(x => x.Item.Sequence)
            .Select(x => x.Item)
            .ToList();
    }
}
