using System.Net;
using System.Text;
using Xunit;

namespace HumanGateway.Security.Tests;

/// <summary>
/// Unit tests for the gateway request-signing primitives (IDENTITY-SECURITY-5.4, AUTH-FR-04, SP-01): HMAC-SHA256
/// signatures over the canonical request form, keyed with the purpose-separated key derived from the gateway's
/// registration token. Covers key derivation, canonicalization stability, sign/verify round-trips, tamper and
/// cross-gateway rejection, timestamp freshness bounds, and outbound request signing.
/// </summary>
public sealed class GatewayRequestSigningTests
{
    private static readonly string SigningKey = GatewayRequestSigning.DeriveKey(RegistrationToken());

    private static string RegistrationToken() => "hgrt_" + new string('A', 43);

    // -----------------------------------------------------------------------------------------------
    // Key derivation
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void DeriveKey_IsDeterministic_AndDiffersFromTheToken()
    {
        var token = RegistrationToken();
        var first = GatewayRequestSigning.DeriveKey(token);
        var second = GatewayRequestSigning.DeriveKey(token);

        Assert.Equal(first, second);
        Assert.NotEqual(token, first);
        Assert.DoesNotContain("hgrt_", first);
    }

    [Fact]
    public void DeriveKey_DiffersAcrossTokens()
        => Assert.NotEqual(
            GatewayRequestSigning.DeriveKey(RegistrationToken()),
            GatewayRequestSigning.DeriveKey("hgrt_" + new string('B', 43)));

    // -----------------------------------------------------------------------------------------------
    // Canonical form
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Canonicalize_IsStable_AndCoversEveryIdentifyingElement()
    {
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?sinceCursor=x", "2026-09-01T00:00:00.000Z", "nonce-1", "gateway:school-a");

        Assert.Equal("POST\n/sync/push\n?sinceCursor=x\n2026-09-01T00:00:00.000Z\nnonce-1\ngateway:school-a", canonical);
        // The same inputs always produce the same canonical form.
        Assert.Equal(canonical, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?sinceCursor=x", "2026-09-01T00:00:00.000Z", "nonce-1", "gateway:school-a"));
    }

    [Fact]
    public void Canonicalize_AnyElementDifference_ChangesTheString()
    {
        var baseline = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?cursor=c1", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a");

        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "GET", "/sync/push", "?cursor=c1", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a"));
        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/pull", "?cursor=c1", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a"));
        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?cursor=c2", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a"));
        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?cursor=c1", "2026-09-02T00:00:00.000Z", "n1", "gateway:school-a"));
        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?cursor=c1", "2026-09-01T00:00:00.000Z", "n2", "gateway:school-a"));
        Assert.NotEqual(baseline, GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "?cursor=c1", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-b"));
    }

    // -----------------------------------------------------------------------------------------------
    // Sign / verify
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Sign_ThenVerify_RoundTrips()
    {
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a");
        var signature = GatewayRequestSigning.Sign(SigningKey, canonical);

        Assert.StartsWith(GatewayRequestSigning.SchemePrefix, signature);
        Assert.True(GatewayRequestSigning.Verify(SigningKey, canonical, signature));
    }

    [Fact]
    public void Verify_RejectsTamperedCanonical()
    {
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a");
        var signature = GatewayRequestSigning.Sign(SigningKey, canonical);

        // Any byte difference in the canonical form invalidates the signature.
        var tampered = canonical.Replace("/sync/push", "/sync/pull", StringComparison.Ordinal);
        Assert.False(GatewayRequestSigning.Verify(SigningKey, tampered, signature));
    }

    [Fact]
    public void Verify_RejectsAnotherGatewaysKey()
    {
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a");
        var signature = GatewayRequestSigning.Sign(SigningKey, canonical);

        var otherKey = GatewayRequestSigning.DeriveKey("hgrt_" + new string('C', 43));
        Assert.False(GatewayRequestSigning.Verify(otherKey, canonical, signature));
    }

    [Fact]
    public void Verify_RejectsMalformedAndEmptySignatures()
    {
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "", "2026-09-01T00:00:00.000Z", "n1", "gateway:school-a");

        Assert.False(GatewayRequestSigning.Verify(SigningKey, canonical, null));
        Assert.False(GatewayRequestSigning.Verify(SigningKey, canonical, string.Empty));
        Assert.False(GatewayRequestSigning.Verify(SigningKey, canonical, "not-a-signature"));
        Assert.False(GatewayRequestSigning.Verify(SigningKey, canonical, "v2=deadbeef"));
        // A different valid signature (different nonce) is still rejected for this canonical.
        var otherNonce = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/push", "", "2026-09-01T00:00:00.000Z", "n2", "gateway:school-a");
        Assert.False(GatewayRequestSigning.Verify(SigningKey, canonical, GatewayRequestSigning.Sign(SigningKey, otherNonce)));
    }

    // -----------------------------------------------------------------------------------------------
    // Freshness / replay window
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void IsFresh_AcceptsTimestampsWithinSkew_RejectsOutside()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var skew = TimeSpan.FromMinutes(5);

        Assert.True(GatewayRequestSigning.IsFresh(GatewayRequestSigning.FormatTimestamp(now), now, skew));
        Assert.True(GatewayRequestSigning.IsFresh(
            GatewayRequestSigning.FormatTimestamp(now.AddMinutes(4)), now, skew));
        Assert.True(GatewayRequestSigning.IsFresh(
            GatewayRequestSigning.FormatTimestamp(now.AddMinutes(-4)), now, skew));

        Assert.False(GatewayRequestSigning.IsFresh(
            GatewayRequestSigning.FormatTimestamp(now.AddMinutes(6)), now, skew));
        Assert.False(GatewayRequestSigning.IsFresh(
            GatewayRequestSigning.FormatTimestamp(now.AddMinutes(-6)), now, skew));
        Assert.False(GatewayRequestSigning.IsFresh("not-a-timestamp", now, skew));
        Assert.False(GatewayRequestSigning.IsFresh(string.Empty, now, skew));
    }

    // -----------------------------------------------------------------------------------------------
    // Outbound request signing
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void SignRequest_SetsAllHeaders_AndVerifiesAtTheRelay()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://relay.example.invalid/sync/artifacts/state?gatewayId=gateway%3Aschool-a");
        request.Content = new StringContent("{\"gatewayId\":\"gateway:school-a\"}", Encoding.UTF8, "application/json");

        GatewayRequestSigning.SignRequest(request, "gateway:school-a", SigningKey, TimeProvider.System);

        Assert.Equal("gateway:school-a", request.Headers.GetValues(GatewayRequestSigning.GatewayIdHeader).Single());
        Assert.NotNull(request.Headers.GetValues(GatewayRequestSigning.TimestampHeader).SingleOrDefault());
        Assert.NotNull(request.Headers.GetValues(GatewayRequestSigning.NonceHeader).SingleOrDefault());
        var signature = request.Headers.GetValues(GatewayRequestSigning.SignatureHeader).Single();
        Assert.StartsWith(GatewayRequestSigning.SchemePrefix, signature);

        // The Relay recomputes the canonical from the exact wire values (decoded path, raw query) and verifies.
        var timestamp = request.Headers.GetValues(GatewayRequestSigning.TimestampHeader).Single();
        var nonce = request.Headers.GetValues(GatewayRequestSigning.NonceHeader).Single();
        var canonical = GatewayRequestSigning.Canonicalize(
            "POST", "/sync/artifacts/state", "?gatewayId=gateway%3Aschool-a", timestamp, nonce, "gateway:school-a");
        Assert.True(GatewayRequestSigning.Verify(SigningKey, canonical, signature));
    }
}
