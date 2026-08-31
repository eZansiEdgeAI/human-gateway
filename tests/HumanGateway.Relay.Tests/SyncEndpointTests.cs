using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HumanGateway.Core.Ids;
using HumanGateway.Protocol.Models;
using HumanGateway.Relay.Api;
using HumanGateway.Relay.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HumanGateway.Relay.Tests;

/// <summary>
/// HTTP-level integration tests for the Relay sync endpoint (CLOUD-RELAY-4.4, RELAY-FR-02, SYNC-FR-01..07):
/// push/pull cursors, idempotent replay, cross-school message routing (RELAY-FR-04), and delivery
/// acknowledgements (SYNC-FR-05). Boots the real Relay <c>Program</c> over a Testcontainers PostgreSQL and
/// exercises the full loop over the wire, proving the acceptance criteria: a registered Edge pushes and pulls
/// cursors and messages converge (cloud-relay §6 #1); two schools exchange messages through the Relay without
/// inbound connectivity at either site (§6 #2); unregistered gateways are rejected (SP-02, §7 #3); and a
/// Relay restart resumes from durable state with no duplication (§6 #3).
/// </summary>
public sealed class SyncEndpointTests : IClassFixture<PostgresRelayFixture>
{
    private static readonly JsonSerializerOptions ApiJson = CreateApiJson();

    private readonly PostgresRelayFixture _fixture;

    public SyncEndpointTests(PostgresRelayFixture fixture)
    {
        _fixture = fixture;
    }

    private static JsonSerializerOptions CreateApiJson()
    {
        var options = new JsonSerializerOptions();
        RelayJson.Configure(options);
        return options;
    }

    // -----------------------------------------------------------------------------------------------
    // Identity gate (SP-02, RELAY-FR-03 §7 #3)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Push_UnregisteredGateway_IsRejectedWithNotFound()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();

        var response = await PushAsync(client, BuildPushBatch("gateway:ghost-sync", new List<SyncItem>()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.NotFound, error?.Code);
        Assert.False(error?.Retryable);
    }

    [Fact]
    public async Task Push_PendingGateway_IsRejectedWithGatewayUnregistered()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var pendingId = UniqueGatewayId("gateway:sync-pending");
        await IssueTokenAsync(client, pendingId); // PENDING, never confirmed

        var response = await PushAsync(client, BuildPushBatch(pendingId, new List<SyncItem>()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.GatewayUnregistered, error?.Code);
    }

    [Fact]
    public async Task Pull_UnregisteredGateway_IsRejected()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/sync/pull",
            new { gatewayId = "gateway:ghost-sync", sinceCursor = (string?)null }, ApiJson);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // PUSH: apply, route, cursor (RELAY-FR-02, RELAY-FR-04, SYNC-FR-01..03)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Push_AppliesMessage_RoutesToRecipientGateway_AndReturnsCursor()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        var message = BuildMessage("msg-00000001", schoolA, schoolB);
        var batch = BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        });

        var response = await PushAsync(client, batch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncBatch>(ApiJson);
        Assert.NotNull(result);
        Assert.Equal(BatchDirection.Push, result.Direction);
        Assert.Equal(schoolA, result.GatewayId);
        Assert.NotNull(result.Cursor);
        Assert.NotNull(result.Items);
        Assert.Empty(result.Items); // keepalive result batch

        await using var db = NewDbContext(factory);
        {
            // RELAY-FR-01: the message envelope is durably stored.
            Assert.True(await db.Messages.AnyAsync(m => m.Id == message.Id));
            // The cross-site delivery ledger records the routed recipient.
            Assert.True(await db.Deliveries.AnyAsync(d => d.MessageId == message.Id
                && d.RecipientAddress == SystemParticipant(schoolB).Address && d.State == "QUEUED"));
            // The push cursor was recorded for observability.
            var cursor = await db.SyncCursors.SingleAsync(c => c.GatewayId == schoolA);
            Assert.Equal(result.Cursor, cursor.PushCursor);
        }
    }

    [Fact]
    public async Task Push_KeepaliveBatch_IsAccepted_WithoutCursorAtStart()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var schoolA = await RegisterGatewayAsync(client, UniqueGatewayId("gateway:sync-a"));

        var response = await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncBatch>(ApiJson);
        Assert.NotNull(result);
        Assert.NotNull(result.Items);
        Assert.Empty(result.Items);
        Assert.Null(result.SequenceStart);
        Assert.Null(result.SequenceEnd);
    }

    [Fact]
    public async Task Push_ReplayedBatch_IsIdempotent_NoDuplicateDelivery()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        var message = BuildMessage("msg-00000002", schoolA, schoolB);
        var batch = BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        }, batchId: "batch-00000001", idempotencyKey: "idem-00000001");

        var first = await PushAsync(client, batch);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<SyncBatch>(ApiJson);

        // The same logical batch (same batchId + idempotencyKey) is replayed — the Relay collapses it.
        var second = await PushAsync(client, batch);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondResult = await second.Content.ReadFromJsonAsync<SyncBatch>(ApiJson);
        Assert.Equal(firstResult?.Cursor, secondResult?.Cursor);

        // Exactly one copy reaches the recipient gateway (SYNC-FR-02, NF-05).
        var pulled = await PullAsync(client, schoolB, null);
        Assert.NotNull(pulled);
        Assert.NotNull(pulled.Items);
        Assert.Single(pulled.Items);
        Assert.Equal(message.Id, pulled.Items[0].Message!.Id);

        await using var db = NewDbContext(factory);
        Assert.Equal(1, await db.Messages.CountAsync(m => m.Id == message.Id));
    }

    [Fact]
    public async Task Push_MultipleRecipientsAtOneGateway_AreRoutedOnce()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        var message = BuildMessage("msg-00000003", schoolA, schoolB)
            with
            {
                Recipients = new List<Participant>
                {
                    SystemParticipant(schoolB),
                    SystemParticipant(schoolB), // second recipient at the same school
                },
            };

        var response = await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pulled = await PullAsync(client, schoolB, null);
        Assert.NotNull(pulled);
        Assert.NotNull(pulled.Items);
        Assert.Single(pulled.Items); // one delivery per gateway, not per recipient
    }

    // -----------------------------------------------------------------------------------------------
    // PULL: cursor-based incremental sync (SYNC-FR-03)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pull_ReturnsRoutedMessage_ThenKeepaliveAfterCursorAdvance()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        var message = BuildMessage("msg-00000004", schoolA, schoolB);
        var response = await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The sender's own pull queue is empty — no self-delivery.
        var senderPull = await PullAsync(client, schoolA, null);
        Assert.NotNull(senderPull);
        Assert.NotNull(senderPull.Items);
        Assert.Empty(senderPull.Items);

        // B pulls from the start position and receives exactly the routed message.
        var first = await PullAsync(client, schoolB, null);
        Assert.NotNull(first);
        Assert.NotNull(first.Items);
        Assert.Equal(BatchDirection.Pull, first.Direction);
        Assert.Equal(schoolB, first.GatewayId);
        Assert.Single(first.Items);
        Assert.Equal(SyncItemKind.Message, first.Items[0].Kind);
        Assert.Equal(message.Id, first.Items[0].Message!.Id);
        Assert.NotNull(first.Cursor);
        Assert.Equal(1, first.Items[0].Sequence);

        // B pulls again with the issued cursor — incremental: nothing new (keepalive).
        var second = await PullAsync(client, schoolB, first.Cursor);
        Assert.NotNull(second);
        Assert.NotNull(second.Items);
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task Pull_WithStaleCursor_ReDeliversUnacknowledgedItems()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        var message = BuildMessage("msg-00000005", schoolA, schoolB);
        await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        }));

        // Pull once — the "Edge" never persists the issued cursor.
        var first = await PullAsync(client, schoolB, null);
        Assert.NotNull(first);
        Assert.NotNull(first.Items);
        Assert.Single(first.Items);

        // A stale/null cursor re-pull re-delivers the same item (at-least-once transport; the Edge's own
        // idempotent apply collapses it) — nothing is ever skipped (NF-05, SYNC-FR-06).
        var retry = await PullAsync(client, schoolB, null);
        Assert.NotNull(retry);
        Assert.NotNull(retry.Items);
        Assert.Single(retry.Items);
        Assert.Equal(message.Id, retry.Items[0].Message!.Id);
    }

    [Fact]
    public async Task Pull_InvalidCursor_IsRejectedWithCursorInvalid()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var schoolB = await RegisterGatewayAsync(client, UniqueGatewayId("gateway:sync-b"));

        var response = await client.PostAsJsonAsync("/sync/pull",
            new { gatewayId = schoolB, sinceCursor = "not-a-relay-cursor" }, ApiJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.CursorInvalid, error?.Code);
        Assert.True(error?.Retryable);
    }

    [Fact]
    public async Task Pull_MalformedGatewayId_IsRejected()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/sync/pull",
            new { gatewayId = "bad gateway!", sinceCursor = (string?)null }, ApiJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.ValidationFailed, error?.Code);
    }

    // -----------------------------------------------------------------------------------------------
    // Delivery acknowledgements (SYNC-FR-05) and two-site exchange (RELAY-FR-04)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ack_RoundTripsToSenderPullQueue_AndTransitionsDeliveryRecord()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        // School A pushes a message for school B.
        var message = BuildMessage("msg-00000006", schoolA, schoolB);
        await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        }));

        // School B pulls and receives the message (its Edge would now enqueue a delivery ack).
        var pulled = await PullAsync(client, schoolB, null);
        Assert.NotNull(pulled);
        Assert.NotNull(pulled.Items);
        Assert.Single(pulled.Items);

        // School B pushes the delivery acknowledgement (SYNC-FR-05).
        var ack = new DeliveryAck
        {
            MessageId = message.Id,
            Recipient = SystemParticipant(schoolB),
            State = DeliveryAckState.Delivered,
            AcknowledgedAt = "2026-08-31T13:00:00.000Z",
        };
        var ackPush = await PushAsync(client, BuildPushBatch(schoolB, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Ack, Sequence = 1, Ack = ack },
        }));
        Assert.Equal(HttpStatusCode.OK, ackPush.StatusCode);

        // School A pulls and learns delivery via the ack routed back to its pull queue.
        var senderPull = await PullAsync(client, schoolA, null);
        Assert.NotNull(senderPull);
        Assert.NotNull(senderPull.Items);
        Assert.Single(senderPull.Items);
        Assert.Equal(SyncItemKind.Ack, senderPull.Items[0].Kind);
        var deliveredAck = senderPull.Items[0].Ack;
        Assert.NotNull(deliveredAck);
        Assert.Equal(message.Id, deliveredAck.MessageId);
        Assert.Equal(DeliveryAckState.Delivered, deliveredAck.State);
        Assert.Equal(SystemParticipant(schoolB).Address, deliveredAck.Recipient.Address);

        // The Relay's cross-site delivery record transitioned QUEUED → DELIVERED.
        await using var db = NewDbContext(factory);
        var record = await db.Deliveries.SingleAsync(d => d.MessageId == message.Id);
        Assert.Equal("DELIVERED", record.State);
    }

    // -----------------------------------------------------------------------------------------------
    // Relay restart durability (cloud-relay §6 #3)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task RelayRestart_PullResumesFromDurableState_NoDuplication()
    {
        var schoolA = UniqueGatewayId("gateway:sync-ra");
        var schoolB = UniqueGatewayId("gateway:sync-rb");
        var message = BuildMessage("msg-00000007", schoolA, schoolB);

        // First "relay instance": register both schools and push the message.
        using (var first = new RelayApiFactory(_fixture))
        {
            var client = first.CreateClient();
            await RegisterGatewayAsync(client, schoolA);
            await RegisterGatewayAsync(client, schoolB);
            var response = await PushAsync(client, BuildPushBatch(schoolA, new List<SyncItem>
            {
                new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // "Relay restart": a fresh host over the same durable PostgreSQL container.
        using var restarted = new RelayApiFactory(_fixture);
        var client2 = restarted.CreateClient();

        // Registered gateways survive the restart and reconnect (SP-02 still enforced).
        var keepalive = await PushAsync(client2, BuildPushBatch(schoolA, new List<SyncItem>()));
        Assert.Equal(HttpStatusCode.OK, keepalive.StatusCode);

        // The queued message survives the restart and is delivered exactly once.
        var pulled = await PullAsync(client2, schoolB, null);
        Assert.NotNull(pulled);
        Assert.NotNull(pulled.Items);
        Assert.Single(pulled.Items);
        Assert.Equal(message.Id, pulled.Items[0].Message!.Id);

        // A follow-up pull from the issued cursor is a keepalive — no duplication.
        var again = await PullAsync(client2, schoolB, pulled.Cursor);
        Assert.NotNull(again);
        Assert.NotNull(again.Items);
        Assert.Empty(again.Items);
    }

    // -----------------------------------------------------------------------------------------------
    // Validation (syncbatch.schema.json shape, SYNC-FR-01..07)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Push_WrongDirection_IsRejected()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var schoolA = await RegisterGatewayAsync(client, UniqueGatewayId("gateway:sync-c"));

        var batch = BuildPushBatch(schoolA, new List<SyncItem>()) with { Direction = BatchDirection.Pull };
        var response = await PushAsync(client, batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.ValidationFailed, error?.Code);
    }

    [Fact]
    public async Task Push_MalformedMessageItem_IsRejected()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        // A message item whose payload violates message.schema.json (missing payload body).
        var message = BuildMessage("msg-00000008", schoolA, schoolB)
            with { Payload = null! };
        var batch = BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Message = message },
        });

        var response = await PushAsync(client, batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ProtocolError>(ApiJson);
        Assert.Equal(ErrorCodes.ValidationFailed, error?.Code);
    }

    [Fact]
    public async Task Push_ItemKindPayloadMismatch_IsRejected()
    {
        using var factory = _factory();
        using var client = factory.CreateClient();
        var (schoolA, schoolB) = await RegisterPairAsync(client);

        // kind=message but the payload is a Delivery — violates the syncItem oneOf.
        var delivery = new Delivery
        {
            Id = "delivery-00000001",
            MessageId = "msg-00000009",
            Recipient = SystemParticipant(schoolB),
            State = DeliveryState.Queued,
            Attempts = 0,
            MaxAttempts = 5,
            CreatedAt = "2026-08-31T12:00:00.000Z",
            UpdatedAt = "2026-08-31T12:00:00.000Z",
        };
        var batch = BuildPushBatch(schoolA, new List<SyncItem>
        {
            new() { Kind = SyncItemKind.Message, Sequence = 1, Delivery = delivery },
        });

        var response = await PushAsync(client, batch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private RelayApiFactory _factory() => new(_fixture);

    private static RelayDbContext NewDbContext(RelayApiFactory factory)
        => factory.Services.GetRequiredService<IDbContextFactory<RelayDbContext>>().CreateDbContext();

    /// <summary>A unique durable gateway ID so tests share one PostgreSQL container without collisions.</summary>
    private static string UniqueGatewayId(string prefix)
        => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<(string SchoolA, string SchoolB)> RegisterPairAsync(HttpClient client)
    {
        var schoolA = await RegisterGatewayAsync(client, UniqueGatewayId("gateway:sync-a"));
        var schoolB = await RegisterGatewayAsync(client, UniqueGatewayId("gateway:sync-b"));
        return (schoolA, schoolB);
    }

    private static async Task<string> IssueTokenAsync(HttpClient client, string gatewayId)
    {
        var response = await client.PostAsJsonAsync("/gateways", new { gatewayId }, ApiJson);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var issued = await response.Content.ReadFromJsonAsync<RegistrationIssued>(ApiJson);
        return issued!.RegistrationToken;
    }

    private static async Task<string> RegisterGatewayAsync(HttpClient client, string gatewayId)
    {
        var token = await IssueTokenAsync(client, gatewayId);
        var confirm = await client.PostAsJsonAsync($"/gateways/{gatewayId}/register",
            new { gatewayId, registrationToken = token }, ApiJson);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        return gatewayId;
    }

    private static async Task<HttpResponseMessage> PushAsync(HttpClient client, SyncBatch batch)
        => await client.PostAsJsonAsync("/sync/push", batch, ApiJson);

    private static async Task<SyncBatch?> PullAsync(HttpClient client, string gatewayId, string? sinceCursor)
    {
        var response = await client.PostAsJsonAsync("/sync/pull",
            new { gatewayId, sinceCursor }, ApiJson);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<SyncBatch>(ApiJson);
    }

    private static SyncBatch BuildPushBatch(
        string gatewayId,
        IReadOnlyList<SyncItem> items,
        string? batchId = null,
        string? idempotencyKey = null)
    {
        return new SyncBatch
        {
            BatchId = batchId ?? IdGenerator.NewId(),
            GatewayId = gatewayId,
            Direction = BatchDirection.Push,
            SinceCursor = null,
            Cursor = null,
            IdempotencyKey = idempotencyKey ?? "idem-" + Guid.NewGuid().ToString("N"),
            SequenceStart = items.Count > 0 ? items.Min(i => i.Sequence) : null,
            SequenceEnd = items.Count > 0 ? items.Max(i => i.Sequence) : null,
            Items = items.ToList(),
            CreatedAt = "2026-08-31T12:00:00.000Z",
        };
    }

    private static Message BuildMessage(string id, string senderGatewayId, string recipientGatewayId, string body = "hello")
        => new()
        {
            Id = id,
            Sender = SystemParticipant(senderGatewayId),
            Recipients = new List<Participant> { SystemParticipant(recipientGatewayId) },
            ConversationId = "conversation:test-000001",
            Payload = new MessagePayload { Body = body },
            CreatedAt = "2026-08-31T12:00:00.000Z",
            ContentHash = "sha256:" + new string('a', 64),
        };

    private static Participant SystemParticipant(string gatewayId)
        => new()
        {
            // The protocol participant-address charset forbids ':' in the suffix, so the address is a
            // colon-free alias; the identity link (gatewayId) is what routes it (participant.schema.json).
            Address = $"system:{gatewayId.Replace(':', '-')}",
            Kind = ParticipantKind.System,
            DisplayName = gatewayId,
            GatewayId = gatewayId,
        };
}
