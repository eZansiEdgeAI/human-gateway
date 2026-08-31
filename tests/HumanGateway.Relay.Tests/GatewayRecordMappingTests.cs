using HumanGateway.Core.Time;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Services;
using HumanGateway.Relay.Storage.Entities;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// Unit tests for the durable-record → protocol-Gateway projection (gateway.schema.json): every stored field
/// maps onto the wire record, the status string converts back to the protocol enum token, and the round-trip
/// serialises with the exact UPPER_SNAKE status (CLOUD-RELAY-4.3, SP-02).
/// </summary>
public sealed class GatewayRecordMappingTests
{
    [Fact]
    public void ToProtocol_MapsAllFieldsAndParsesWireStatus()
    {
        var now = ProtocolTime.Now();
        var record = new GatewayRecord
        {
            GatewayId = "gateway:school-01",
            DisplayName = "Riverside Primary",
            Status = "REGISTERED",
            RegistrationTokenFingerprint = "sha256:" + new string('a', 64),
            TokenIssuedAt = now,
            TokenExpiresAt = now,
            RegisteredAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var protocol = record.ToProtocol();

        Assert.Equal(record.GatewayId, protocol.GatewayId);
        Assert.Equal(record.DisplayName, protocol.DisplayName);
        Assert.Equal(GatewayStatus.Registered, protocol.Status);
        Assert.Equal(record.RegistrationTokenFingerprint, protocol.RegistrationTokenFingerprint);
        Assert.Equal(now, protocol.TokenIssuedAt);
        Assert.Equal(now, protocol.RegisteredAt);
        Assert.Equal(now, protocol.LastSeenAt);
    }

    [Fact]
    public void ToProtocol_UnknownStatusYieldsNull()
    {
        var record = new GatewayRecord
        {
            GatewayId = "gateway:school-01",
            Status = "WEIRD",
            CreatedAt = ProtocolTime.Now(),
        };

        Assert.Null(record.ToProtocol().Status);
    }

    [Fact]
    public void ToProtocol_SerialisesWithExactWireStatusToken()
    {
        var record = new GatewayRecord
        {
            GatewayId = "gateway:school-01",
            Status = "REGISTERED",
            CreatedAt = ProtocolTime.Now(),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(record.ToProtocol(), ProtocolJson.Options);

        Assert.Contains("\"status\":\"REGISTERED\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gatewayId\":\"gateway:school-01\"", json, StringComparison.Ordinal);
    }
}
