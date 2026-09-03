using System.Text.Json;
using HumanGateway.Edge.Storage;
using HumanGateway.Edge.Storage.Entities;
using HumanGateway.Protocol;
using HumanGateway.Protocol.Models;
using Microsoft.EntityFrameworkCore;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// Persists Relay-delivered messages and reconstructs workflow task envelopes from the structured message
/// payload. The Relay remains transport-only; workflow state and task semantics stay with the consumer.
/// </summary>
public sealed class InboundMessageProjector : IInboundMessageHandler
{
    private readonly IDbContextFactory<EdgeDbContext> _factory;

    public InboundMessageProjector(IDbContextFactory<EdgeDbContext> factory) => _factory = factory;

    public async Task HandleAsync(IReadOnlyList<Message> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        foreach (var message in messages)
        {
            if (await db.Messages.AnyAsync(m => m.Id == message.Id, ct).ConfigureAwait(false)) continue;
            db.Messages.Add(MessageRecord.FromEnvelope(message));
            foreach (var recipient in message.Recipients ?? new List<Participant>())
            {
                db.Deliveries.Add(DeliveryRecord.FromEnvelope(new Delivery
                {
                    Id = $"delivery:{message.Id}:{recipient.Address}", MessageId = message.Id,
                    Recipient = recipient, State = DeliveryState.Delivered, Attempts = 0, MaxAttempts = 5,
                    DeliveredAt = message.CreatedAt, CreatedAt = message.CreatedAt, UpdatedAt = message.CreatedAt,
                }));
            }

            var task = ReadTask(message.Payload.Data);
            if (task is null) continue;
            var existing = await db.Tasks.SingleOrDefaultAsync(t => t.Id == task.Id, ct).ConfigureAwait(false);
            if (existing is null) db.Tasks.Add(HumanTaskRecord.FromEnvelope(task));
            else
            {
                existing.Envelope = task;
                existing.Status = ProtocolJsonConversions.WireToken(task.Status) ?? string.Empty;
                existing.Kind = ProtocolJsonConversions.WireToken(task.Kind);
                existing.ResponseMessageId = task.ResponseMessageId;
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static HumanTask? ReadTask(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty("humanTask", out var task)) return null;
        try { return JsonSerializer.Deserialize<HumanTask>(task.GetRawText(), ProtocolJson.Options); }
        catch (JsonException) { return null; }
    }
}
