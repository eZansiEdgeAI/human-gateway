using HumanGateway.Core.Cursor;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>
/// Property tests for cursor arithmetic and the opaque cursor codec (SYNC-FR-03). These pin the "advance
/// contiguously, never rewind, gaps never lost" contract that out-of-order and long-disconnect convergence
/// depends on.
/// </summary>
public class CursorTests
{
    [Fact]
    public void AdvanceContiguous_contiguous_run_advances_to_high_watermark()
    {
        var position = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 2, 3 });
        Assert.Equal(3, position.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_stops_at_first_gap()
    {
        // {1, 3} can only advance the cursor to 1; the later {2} will fill the gap.
        var position = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 3 });
        Assert.Equal(1, position.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_fills_gap_in_later_batch()
    {
        // {1, 3} advances to 1 (gap at 2). The gap is filled once 2 arrives: the receiver re-derives its
        // position over the accumulated applied set (which is what the sync engine does via the inbox).
        var afterFirst = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 3 });
        Assert.Equal(1, afterFirst.Sequence);

        var afterSecond = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 2, 3 });
        Assert.Equal(3, afterSecond.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_advances_only_over_present_sequences()
    {
        // {1, 3} advances to 1 (gap at 2). Supplying {2} next advances to 2 — but not to 3, because 3 is not
        // part of the *current* call's present set. The engine supplies the full applied history, so in
        // practice the cursor reaches the true contiguous high-watermark (see SyncEngineTests).
        var afterFirst = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 3 });
        var afterSecond = CursorMath.AdvanceContiguous(afterFirst, new long[] { 2 });
        Assert.Equal(1, afterFirst.Sequence);
        Assert.Equal(2, afterSecond.Sequence);

        // The full applied history {1, 2, 3} advances contiguously to 3 in one call.
        var converged = CursorMath.AdvanceContiguous(CursorPosition.Start, new long[] { 1, 3, 2 });
        Assert.Equal(3, converged.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_never_rewinds()
    {
        var prior = new CursorPosition(10);
        var position = CursorMath.AdvanceContiguous(prior, new long[] { 1, 2, 11 });
        Assert.Equal(11, position.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_ignores_sequences_at_or_below_prior()
    {
        var prior = new CursorPosition(5);
        // 1..5 are already covered; only 6 is contiguous → cursor advances to 6.
        var position = CursorMath.AdvanceContiguous(prior, new long[] { 1, 2, 3, 4, 5, 6 });
        Assert.Equal(6, position.Sequence);
    }

    [Fact]
    public void AdvanceContiguous_empty_present_keeps_position()
    {
        var prior = new CursorPosition(7);
        var position = CursorMath.AdvanceContiguous(prior, Array.Empty<long>());
        Assert.Equal(7, position.Sequence);
    }

    [Fact]
    public void Encode_start_is_null()
    {
        Assert.Null(CursorCodec.Encode(CursorPosition.Start));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void Encode_round_trips_through_decode(long sequence)
    {
        var token = CursorCodec.Encode(new CursorPosition(sequence));
        Assert.NotNull(token);
        Assert.Equal(sequence, CursorCodec.TryDecode(token)!.Value.Sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v1:not-a-number")]
    [InlineData("v2:123")]
    public void Decode_unrecognised_token_returns_null(string? token)
    {
        Assert.Null(CursorCodec.TryDecode(token));
    }

    [Fact]
    public void Encoded_token_is_url_safe()
    {
        var token = CursorCodec.Encode(new CursorPosition(123456789))!;
        Assert.Matches("^[A-Za-z0-9._:/-]*$", token);
    }
}
