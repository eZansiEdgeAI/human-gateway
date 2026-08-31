using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HumanGateway.Security.Tests;

/// <summary>
/// Unit tests for bearer-token extraction from requests (AUTH-FR-02, SP-03). Both the Edge and the Relay
/// authenticate users via <c>Authorization: Bearer &lt;hgsu_…&gt;</c>; the extraction must be lenient about
/// casing and spacing but strict about shape.
/// </summary>
public sealed class BearerTokensTests
{
    private static DefaultHttpContext ContextWithHeader(string header)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers.Authorization = header;
        }

        return context;
    }

    [Fact]
    public void FromRequest_ExtractsWellFormedBearerToken()
    {
        var token = SessionTokens.Generate();
        var context = ContextWithHeader($"Bearer {token}");

        Assert.Equal(token, BearerTokens.FromRequest(context.Request));
    }

    [Fact]
    public void FromRequest_IsCaseInsensitiveOnScheme()
    {
        var token = SessionTokens.Generate();
        var context = ContextWithHeader($"bearer {token}");

        Assert.Equal(token, BearerTokens.FromRequest(context.Request));
    }

    [Fact]
    public void FromRequest_ReturnsNull_WhenMissingOrMalformed()
    {
        Assert.Null(BearerTokens.FromRequest(ContextWithHeader(null).Request));
        Assert.Null(BearerTokens.FromRequest(ContextWithHeader("").Request));
        Assert.Null(BearerTokens.FromRequest(ContextWithHeader("Basic dXNlcjpwYXNz").Request));
        Assert.Null(BearerTokens.FromRequest(ContextWithHeader("Bearer").Request));
        Assert.Null(BearerTokens.FromRequest(ContextWithHeader("Bearer  ").Request));
    }
}
