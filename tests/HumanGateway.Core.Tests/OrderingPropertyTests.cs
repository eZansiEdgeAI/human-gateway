using HumanGateway.Core.Ordering;
using HumanGateway.Protocol.Models;
using Xunit;

// The Delivery *type* (Protocol.Models.Delivery) shares its simple name with the HumanGateway.Core.Delivery
// namespace, which is in scope from HumanGateway.Core.Tests. Alias it under a distinct name (as
// HumanGateway.Core.Delivery does) to keep the references unambiguous.
using DeliveryModel = HumanGateway.Protocol.Models.Delivery;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Property tests for deterministic reordering (SYNC-FR-07). These pin the order-independence guarantee:
/// the same multiset of items reorders to the same sequence regardless of arrival order, equal sequences are
/// tie-broken by stable payload identity (never input position), and gaps are preserved — items are never
/// dropped and never duplicated.
/// </summary>
public class OrderingPropertyTests
{
    private static SyncItem MessageItem(long sequence, string id)
        => TestData.MessageItem(TestData.NewMessage(id), sequence);

    // ---- Permutation invariance (the core determinism property) ----

    [Fact]
    public void Reorder_is_invariant_under_every_permutation()
    {
        var items = new[]
        {
            MessageItem(3, "msg-0003"),
            MessageItem(1, "msg-0001"),
            MessageItem(4, "msg-0004"),
            MessageItem(2, "msg-0002"),
        };

        var expected = items.OrderBy(i => i.Sequence).Select(i => i.Message!.Id).ToArray();

        foreach (var permutation in Permutations(items.ToList()))
        {
            var ordered = SequenceOrdering.Reorder(permutation);
            Assert.Equal(expected, ordered.Select(i => i.Message!.Id).ToArray());
        }
    }

    [Fact]
    public void Reorder_preserves_multiset_never_drops_or_duplicates()
    {
        // {3, 1, 5} with a gap: reordered to {1, 3, 5}, the gap is preserved, and every item lands exactly once.
        var items = new[]
        {
            MessageItem(5, "msg-0005"),
            MessageItem(1, "msg-0001"),
            MessageItem(3, "msg-0003"),
        };

        var ordered = SequenceOrdering.Reorder(items);

        Assert.Equal(new long[] { 1, 3, 5 }, ordered.Select(i => i.Sequence).ToArray());
        Assert.Equal(3, ordered.Count);
        Assert.Equal(items.Select(i => i.Message!.Id).OrderBy(id => id, StringComparer.Ordinal),
                     ordered.Select(i => i.Message!.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Reorder_empty_input_is_empty()
    {
        Assert.Empty(SequenceOrdering.Reorder(Array.Empty<SyncItem>()));
    }

    [Fact]
    public void Reorder_single_item_is_that_item()
    {
        var item = MessageItem(7, "msg-0007");
        Assert.Equal(item, Assert.Single(SequenceOrdering.Reorder(new[] { item })));
    }

    // ---- Equal-sequence tie-break determinism (order-independent) ----

    [Fact]
    public void Reorder_equal_sequence_is_tie_broken_by_stable_identity_not_input_order()
    {
        // Two items sharing a sequence number: the result is ordered by stable payload identity, so swapping
        // the input order does not change the output (a plain stable sort would flip these).
        var a = MessageItem(2, "msg:aaa");
        var b = MessageItem(2, "msg:bbb");

        var forward = SequenceOrdering.Reorder(new[] { a, b });
        var reversed = SequenceOrdering.Reorder(new[] { b, a });

        Assert.Equal(new[] { "msg:aaa", "msg:bbb" }, forward.Select(i => i.Message!.Id).ToArray());
        Assert.Equal(forward.Select(i => i.Message!.Id), reversed.Select(i => i.Message!.Id));
    }

    [Fact]
    public void Reorder_equal_sequence_many_way_shuffle_is_stable()
    {
        var items = new[]
        {
            MessageItem(9, "msg:zzz"),
            MessageItem(9, "msg:aaa"),
            MessageItem(9, "msg:mmm"),
        };

        var expected = items.OrderBy(i => i.Message!.Id, StringComparer.Ordinal)
                            .Select(i => i.Message!.Id)
                            .ToArray();

        var rng = new Random(1234);
        for (var i = 0; i < 50; i++)
        {
            var shuffled = items.OrderBy(_ => rng.Next()).ToArray();
            Assert.Equal(expected, SequenceOrdering.Reorder(shuffled).Select(x => x.Message!.Id).ToArray());
        }
    }

    [Fact]
    public void StableIdentity_derives_from_each_items_durable_payload_id()
    {
        Assert.Equal("msg-1", SequenceOrdering.StableIdentity(MessageItem(1, "msg-1")));

        Assert.Equal("delivery-1", SequenceOrdering.StableIdentity(new SyncItem
        {
            Kind = SyncItemKind.Delivery,
            Sequence = 1,
            Delivery = new DeliveryModel { Id = "delivery-1", MessageId = "msg-1" },
        }));

        Assert.Equal("artifact-1", SequenceOrdering.StableIdentity(new SyncItem
        {
            Kind = SyncItemKind.Artifact,
            Sequence = 1,
            Artifact = new Artifact { Id = "artifact-1", Hash = "sha256:00", MimeType = "text/plain", Filename = "a.txt" },
        }));

        Assert.Equal("msg-1", SequenceOrdering.StableIdentity(new SyncItem
        {
            Kind = SyncItemKind.Ack,
            Sequence = 1,
            Ack = new DeliveryAck { MessageId = "msg-1" },
        }));
    }

    [Fact]
    public void StableIdentity_item_without_payload_is_empty()
    {
        // An item whose kind has no payload (or an unknown kind) falls back to the empty string, so it sorts
        // before identified items deterministically rather than throwing.
        Assert.Equal(string.Empty, SequenceOrdering.StableIdentity(new SyncItem { Kind = SyncItemKind.Message, Sequence = 1 }));
    }

    // ---- Composite (gatewayId, sequence) ordering ----

    [Fact]
    public void Reorder_composite_is_invariant_and_orders_gateway_then_sequence()
    {
        var tuples = new[]
        {
            ("gw:b", MessageItem(1, "b-1")),
            ("gw:a", MessageItem(2, "a-2")),
            ("gw:a", MessageItem(1, "a-1")),
            ("gw:b", MessageItem(2, "b-2")),
        };

        var expected = new[] { "a-1", "a-2", "b-1", "b-2" };

        var rng = new Random(99);
        for (var i = 0; i < 50; i++)
        {
            var shuffled = tuples.OrderBy(_ => rng.Next()).ToArray();
            var ordered = SequenceOrdering.Reorder(shuffled);
            Assert.Equal(expected, ordered.Select(x => x.Message!.Id).ToArray());
        }
    }

    // ---- Helpers ----

    private static IEnumerable<List<SyncItem>> Permutations(List<SyncItem> items)
    {
        if (items.Count == 0)
        {
            yield return new List<SyncItem>();
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
