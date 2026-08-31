using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Unit tests for the registration request wire models (CLOUD-RELAY-4.3): semantic validation mirrors
/// gateway.schema.json shapes — a durable ID for <c>gatewayId</c>, the <c>hgrt_</c> token shape, and
/// displayName bounds. Failures throw <see cref="GatewayServiceException"/> with the reserved
/// VALIDATION_FAILED code (SP-07).
/// </summary>
public sealed class RequestValidationTests
{
    [Fact]
    public void RegisterGatewayRequest_AcceptsValidIdentity()
    {
        var request = new RegisterGatewayRequest
        {
            GatewayId = "gateway:school-01",
            DisplayName = "Riverside Primary",
        };

        request.Validate(); // must not throw
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]                         // too short
    [InlineData("gateway!")]                  // forbidden char
    [InlineData("has spaces")]                // forbidden chars
    [InlineData(":starts-with-separator")]    // leading separator
    public void RegisterGatewayRequest_RejectsInvalidGatewayId(string? gatewayId)
    {
        var request = new RegisterGatewayRequest { GatewayId = gatewayId! };

        var ex = Assert.Throws<GatewayServiceException>(() => request.Validate());
        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
        Assert.False(ex.Retryable);
    }

    [Fact]
    public void RegisterGatewayRequest_RejectsOversizedDisplayName()
    {
        var request = new RegisterGatewayRequest
        {
            GatewayId = "gateway:school-01",
            DisplayName = new string('x', 256),
        };

        var ex = Assert.Throws<GatewayServiceException>(() => request.Validate());
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }

    [Fact]
    public void ConfirmRegistrationRequest_AcceptsValidToken()
    {
        var request = new ConfirmRegistrationRequest
        {
            GatewayId = "gateway:school-01",
            RegistrationToken = "hgrt_" + new string('A', 43),
        };

        request.Validate(); // must not throw
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hgrt_short")]        // below minimum body length
    [InlineData("bearer_abcdef")]     // wrong prefix
    public void ConfirmRegistrationRequest_RejectsMalformedToken(string? token)
    {
        var request = new ConfirmRegistrationRequest
        {
            GatewayId = "gateway:school-01",
            RegistrationToken = token!,
        };

        var ex = Assert.Throws<GatewayServiceException>(() => request.Validate());
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }
}
