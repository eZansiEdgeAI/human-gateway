using Xunit;

namespace HumanGateway.Security.Tests;

/// <summary>
/// Unit tests for the Edge's TLS scheme enforcement (IDENTITY-SECURITY-5.4, AUTH-FR-04, SP-01): the Edge may
/// only dial out to the Relay over https, with plain http permitted only for loopback hosts or an explicit
/// insecure-dev opt-in.
/// </summary>
public sealed class RelayTlsPolicyTests
{
    [Fact]
    public void RequireAllowed_AcceptsHttps_ForAnyHost()
    {
        var uri = RelayTlsPolicy.RequireAllowed("https://relay.example.com");

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("relay.example.com", uri.Host);
    }

    [Fact]
    public void RequireAllowed_RejectsHttp_ForNonLoopbackHosts()
        => Assert.Throws<ArgumentException>(() => RelayTlsPolicy.RequireAllowed("http://relay.example.com"));

    [Fact]
    public void RequireAllowed_AcceptsLoopbackHttp_ForLocalDevelopment()
    {
        Assert.Equal("http", RelayTlsPolicy.RequireAllowed("http://localhost:5275").Scheme);
        Assert.Equal("http", RelayTlsPolicy.RequireAllowed("http://127.0.0.1:5275").Scheme);
        Assert.Equal("http", RelayTlsPolicy.RequireAllowed("http://[::1]:5275").Scheme);
    }

    [Fact]
    public void RequireAllowed_AcceptsHttp_WhenInsecureDevOptInIsSet()
        => Assert.Equal("http", RelayTlsPolicy.RequireAllowed("http://relay:8080", allowInsecureHttp: true).Scheme);

    [Fact]
    public void RequireAllowed_RejectsMissingAndMalformedUrls()
    {
        Assert.Throws<ArgumentException>(() => RelayTlsPolicy.RequireAllowed(null));
        Assert.Throws<ArgumentException>(() => RelayTlsPolicy.RequireAllowed(string.Empty));
        Assert.Throws<ArgumentException>(() => RelayTlsPolicy.RequireAllowed("not-a-url"));
        Assert.Throws<ArgumentException>(() => RelayTlsPolicy.RequireAllowed("ftp://relay.example.com"));
    }
}
