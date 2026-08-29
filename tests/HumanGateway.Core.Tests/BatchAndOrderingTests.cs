using HumanGateway.Core.Batch;
using HumanGateway.Core.Ordering;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

public class BatchSequenceValidatorTests
{
    [Fact]
    public void EmptyKeepaliveWithNullRangeIsValid()
    {
        var batch = new SyncBatch
        {
            BatchId = "batch:1",
            GatewayId = "gateway:1",
            Direction = BatchDirection.Push,
            IdempotencyKey = "key:1",
            Items = new List<SyncItem>(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
        Assert.True(BatchSequenceValidator.Validate(batch).IsValid);
    }

    [Fact]
    public void EmptyBatchWithRangeIsInvalid()
    {
        var batch = new SyncBatch
        {
            BatchId = "batch:1",
            GatewayId = "gateway:1",
            Direction = BatchDirection.Push,
            IdempotencyKey = "key:1",
            Items = new List<SyncItem>(),
            SequenceStart = 1,
            SequenceEnd = 1,
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
        var result = BatchSequenceValidator.Validate(batch);
        Assert.False(result.IsValid);
        Assert.Contains(BatchSequenceValidator.SequenceRangeForbidden, result.Violations);
    }

    [Fact]
    public void NonEmptyBatchWithoutRangeIsInvalid()
    {
        var batch = new SyncBatch
        {
            BatchId = "batch:1",
            GatewayId = "gateway:1",
            Direction = BatchDirection.Push,
            IdempotencyKey = "key:1",
            Items = new List<SyncItem> { TestData.MessageItem(TestData.NewMessage(), 1) },
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
        var result = BatchSequenceValidator.Validate(batch);
        Assert.False(result.IsValid);
        Assert.Contains(BatchSequenceValidator.SequenceRangeRequired, result.Violations);
    }

    [Fact]
    public void InvertedRangeIsInvalid()
    {
        var batch = Batch(5, 2, new[] { 2L, 5L });
        Assert.Contains(BatchSequenceValidator.SequenceRangeInverted, BatchSequenceValidator.Validate(batch).Violations);
    }

    [Fact]
    public void ItemOutsideRangeIsInvalid()
    {
        var batch = Batch(1, 3, new[] { 1L, 4L });
        Assert.Contains(BatchSequenceValidator.ItemSequenceOutOfRange, BatchSequenceValidator.Validate(batch).Violations);
    }

    [Fact]
    public void ValidBatchPasses()
    {
        var batch = Batch(1, 3, new[] { 1L, 2L, 3L });
        Assert.True(BatchSequenceValidator.Validate(batch).IsValid);
    }

    private static SyncBatch Batch(long start, long end, long[] sequences)
        => new()
        {
            BatchId = "batch:1",
            GatewayId = "gateway:1",
            Direction = BatchDirection.Push,
            IdempotencyKey = "key:1",
            SequenceStart = start,
            SequenceEnd = end,
            Items = sequences.Select(s => TestData.MessageItem(TestData.NewMessage(), s)).ToList(),
            CreatedAt = "2026-08-29T00:00:00.000Z",
        };
}

public class SequenceOrderingTests
{
    [Fact]
    public void ReordersDeterministicallyBySequence()
    {
        var items = new[]
        {
            TestData.MessageItem(TestData.NewMessage("m3"), 3),
            TestData.MessageItem(TestData.NewMessage("m1"), 1),
            TestData.MessageItem(TestData.NewMessage("m2"), 2),
        };
        var ordered = SequenceOrdering.Reorder(items);
        Assert.Equal(new long[] { 1, 2, 3 }, ordered.Select(i => i.Sequence).ToArray());
    }

    [Fact]
    public void CompositeOrderingOrdersByGatewayThenSequence()
    {
        var items = new[]
        {
            ("gw:b", TestData.MessageItem(TestData.NewMessage("m1"), 1)),
            ("gw:a", TestData.MessageItem(TestData.NewMessage("m2"), 2)),
            ("gw:a", TestData.MessageItem(TestData.NewMessage("m1"), 1)),
        };
        var ordered = SequenceOrdering.Reorder(items);
        Assert.Equal(new long[] { 1, 2, 1 }, ordered.Select(i => i.Sequence).ToArray());
        Assert.Equal("m2", ordered[1].Message!.Id);
    }
}
