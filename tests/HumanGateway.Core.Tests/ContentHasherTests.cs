using HumanGateway.Core.Hashing;
using HumanGateway.Protocol.Models;
using Xunit;

namespace HumanGateway.Core.Tests;

/// <summary>Content-hash verification (SYNC-FR-02): the envelope hash excludes <see cref="Message.ContentHash"/>
/// itself and detects any tamper/corruption.</summary>
public class ContentHasherTests
{
    [Fact]
    public void Computed_message_hash_verifies()
    {
        var message = TestData.NewMessage();
        Assert.True(ContentHasher.VerifyMessageHash(message));
    }

    [Fact]
    public void Tampered_payload_fails_verification()
    {
        var message = TestData.NewMessage();
        var tampered = message with { Payload = message.Payload with { Body = "tampered" } };
        Assert.False(ContentHasher.VerifyMessageHash(tampered));
    }

    [Fact]
    public void Hash_prefix_and_shape()
    {
        var hash = ContentHasher.ComputeUtf8("hello");
        Assert.StartsWith("sha256:", hash);
        Assert.Matches("^sha256:[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Verify_detects_byte_mismatch()
    {
        var content = "hello"u8.ToArray();
        var declared = ContentHasher.Compute(content);
        Assert.True(ContentHasher.Verify(declared, content));
        Assert.False(ContentHasher.Verify(declared, "HELLO"u8.ToArray()));
    }
}
