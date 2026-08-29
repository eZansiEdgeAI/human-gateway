using HumanGateway.Core.Conflict;
using HumanGateway.Core.Hashing;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

public class FieldMergeTests
{
    [Fact]
    public void IdenticalEnvelopesReportSameContent()
    {
        var local = TestData.NewMessage();
        var outcome = FieldMerge.MergeMessages(local, local);
        Assert.True(outcome.Succeeded);
        Assert.Equal(MergeDisposition.SameContent, outcome.Disposition);
    }

    [Fact]
    public void NewerRemoteWinsPerField()
    {
        var local = TestData.NewMessage(body: "old body", updatedAt: "2026-08-29T01:00:00.000Z");
        var remote = TestData.NewMessage(id: local.Id, body: "new body", updatedAt: "2026-08-29T02:00:00.000Z");

        var outcome = FieldMerge.MergeMessages(local, remote);
        Assert.True(outcome.Succeeded);
        Assert.Equal(MergeDisposition.Merged, outcome.Disposition);
        Assert.Equal("new body", outcome.Merged!.Payload!.Body);
        Assert.True(ContentHasher.VerifyMessageHash(outcome.Merged));
    }

    [Fact]
    public void TiesKeepLocal()
    {
        var local = TestData.NewMessage(body: "local", updatedAt: "2026-08-29T01:00:00.000Z");
        var remote = TestData.NewMessage(id: local.Id, body: "remote", updatedAt: "2026-08-29T01:00:00.000Z");

        var outcome = FieldMerge.MergeMessages(local, remote);
        Assert.Equal("local", outcome.Merged!.Payload!.Body);
    }

    [Fact]
    public void CorruptLocalYieldsVerifiedRemote()
    {
        var local = TestData.NewMessage(body: "tampered") with { ContentHash = "sha256:deadbeef" };
        var remote = TestData.NewMessage(id: local.Id, body: "intact");

        var outcome = FieldMerge.MergeMessages(local, remote);
        Assert.Equal(MergeDisposition.LocalCorrupt, outcome.Disposition);
        Assert.Equal("intact", outcome.Merged!.Payload!.Body);
    }

    [Fact]
    public void DifferentIdsAreNotMergeable()
    {
        var local = TestData.NewMessage();
        var remote = TestData.NewMessage();
        Assert.Equal(MergeDisposition.Conflict, FieldMerge.MergeMessages(local, remote).Disposition);
    }
}
