using System.Text.RegularExpressions;
using HumanGateway.Relay.Security;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Unit tests for the registration-token primitives (AUTH-FR-01, SP-07): token wire shape per
/// gateway.schema.json <c>$defs.registrationToken</c>, the <c>sha256:&lt;hex&gt;</c> fingerprint that is the
/// only thing the Relay persists, and constant-time verification (CLOUD-RELAY-4.3).
/// </summary>
public sealed partial class RegistrationTokensTests
{
    [GeneratedRegex("^hgrt_[A-Za-z0-9_-]{43,251}$")]
    private static partial Regex TokenShapeRegex();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$")]
    private static partial Regex FingerprintShapeRegex();

    [Fact]
    public void Generate_TokenMatchesGatewaySchemaRegistrationTokenShape()
    {
        var token = RegistrationTokens.Generate();

        // gateway.schema.json#/$defs/registrationToken: ^hgrt_[A-Za-z0-9_-]+$, 48..256 chars.
        Assert.Matches(TokenShapeRegex(), token);
        Assert.InRange(token.Length, 48, 256);
        Assert.StartsWith("hgrt_", token, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_TokensAreUniqueAndHighEntropy()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RegistrationTokens.Generate()).ToArray();

        Assert.Equal(200, tokens.Distinct().Count());
        // 32 random bytes per token: 256 bits of entropy.
        Assert.All(tokens, t => Assert.True(t.Length >= 48, "token is at least the schema minimum length"));
    }

    [Fact]
    public void Fingerprint_MatchesSha256FingerprintShape()
    {
        var fingerprint = RegistrationTokens.Fingerprint(RegistrationTokens.Generate());

        Assert.Matches(FingerprintShapeRegex(), fingerprint);
        Assert.StartsWith("sha256:", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_IsDeterministic_AndDistinguishesTokens()
    {
        var token = RegistrationTokens.Generate();

        Assert.Equal(RegistrationTokens.Fingerprint(token), RegistrationTokens.Fingerprint(token));
        Assert.NotEqual(RegistrationTokens.Fingerprint(token), RegistrationTokens.Fingerprint(token + "x"));
    }

    [Fact]
    public void Fingerprint_NeverContainsThePlaintextToken()
    {
        var token = RegistrationTokens.Generate();

        // SP-07: the persisted fingerprint must never leak the token bytes.
        Assert.DoesNotContain(token, RegistrationTokens.Fingerprint(token), StringComparison.Ordinal);
    }

    [Fact]
    public void IsWellFormed_AcceptsGeneratedTokens_AndRejectsShapes()
    {
        var token = RegistrationTokens.Generate();

        Assert.True(RegistrationTokens.IsWellFormed(token));
        Assert.False(RegistrationTokens.IsWellFormed(null));
        Assert.False(RegistrationTokens.IsWellFormed(""));
        Assert.False(RegistrationTokens.IsWellFormed("hgrt_short"));
        Assert.False(RegistrationTokens.IsWellFormed("H" + token[1..]));     // prefix case violation
        Assert.False(RegistrationTokens.IsWellFormed(token + "!"));          // charset violation
        Assert.False(RegistrationTokens.IsWellFormed("hgrr_" + token[5..])); // wrong prefix
    }

    [Fact]
    public void Verify_AcceptsTheSameToken_RejectsOthersAndNulls()
    {
        var token = RegistrationTokens.Generate();
        var fingerprint = RegistrationTokens.Fingerprint(token);

        Assert.True(RegistrationTokens.Verify(token, fingerprint));
        Assert.False(RegistrationTokens.Verify(RegistrationTokens.Generate(), fingerprint));
        Assert.False(RegistrationTokens.Verify(null, fingerprint));
        Assert.False(RegistrationTokens.Verify(token, null));
        Assert.False(RegistrationTokens.Verify(token, "sha256:" + new string('0', 64)));
        Assert.False(RegistrationTokens.Verify(token, ""));
    }
}
