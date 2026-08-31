using Xunit;

namespace HumanGateway.Security.Tests;

/// <summary>
/// Unit tests for the signed opaque session-token primitives (identity-security Open Q #1 default: opaque
/// session tokens v1, JWT only if a consumer needs it). The token is high-entropy bearer material; only its
/// SHA-256 fingerprint is stored, and verification is a constant-time comparison (AUTH-FR-02, SP-03, SP-07).
/// </summary>
public sealed class SessionTokensTests
{
    [Fact]
    public void Generate_ProducesWellFormedHgsuToken()
    {
        var token = SessionTokens.Generate();

        Assert.True(SessionTokens.IsWellFormed(token));
        Assert.StartsWith(SessionTokens.TokenPrefix, token);
        // 5-char prefix + 43 chars of base64url for 32 random bytes.
        Assert.Equal(5 + 43, token.Length);
    }

    [Fact]
    public void Generate_ProducesHighEntropyUniqueTokens()
    {
        var first = SessionTokens.Generate();
        var second = SessionTokens.Generate();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Fingerprint_IsStableSha256Hex()
    {
        var token = SessionTokens.Generate();
        var fingerprint = SessionTokens.Fingerprint(token);

        Assert.StartsWith("sha256:", fingerprint);
        // "sha256:" + 64 lowercase hex chars.
        Assert.Equal(7 + 64, fingerprint.Length);
        Assert.Equal(fingerprint, SessionTokens.Fingerprint(token));
    }

    [Fact]
    public void Verify_MatchesOnlyTheExactToken()
    {
        var token = SessionTokens.Generate();
        var fingerprint = SessionTokens.Fingerprint(token);

        Assert.True(SessionTokens.Verify(token, fingerprint));
        Assert.False(SessionTokens.Verify(token + "x", fingerprint));
        Assert.False(SessionTokens.Verify(SessionTokens.Generate(), fingerprint));
    }

    [Fact]
    public void Verify_RejectsNullAndMalformedInputs()
    {
        Assert.False(SessionTokens.Verify(null, "sha256:" + new string('0', 64)));
        Assert.False(SessionTokens.Verify("hgsu_short", "sha256:" + new string('0', 64)));
        Assert.False(SessionTokens.Verify("not-a-token", "sha256:" + new string('0', 64)));
    }

    [Fact]
    public void IsWellFormed_RejectsWrongPrefixAndBadAlphabet()
    {
        Assert.False(SessionTokens.IsWellFormed(null));
        Assert.False(SessionTokens.IsWellFormed(""));
        Assert.False(SessionTokens.IsWellFormed("hgrt_" + new string('a', 43)));   // gateway token prefix, not session
        Assert.False(SessionTokens.IsWellFormed(SessionTokens.TokenPrefix + "!!not-base64url!!"));
        Assert.True(SessionTokens.IsWellFormed(SessionTokens.Generate()));
    }

    [Fact]
    public void TokenBody_IsBase64Url_WithNoPaddingOrReservedChars()
    {
        var body = SessionTokens.Generate()[SessionTokens.TokenPrefix.Length..];

        Assert.DoesNotContain('+', body);
        Assert.DoesNotContain('/', body);
        Assert.DoesNotContain('=', body);
    }
}
