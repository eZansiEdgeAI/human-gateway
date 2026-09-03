using HumanGateway.Protocol.Models;

namespace HumanGateway.Edge.Sync;

/// <summary>Projects messages received from the Relay into local consumers (including workflow adapters).</summary>
public interface IInboundMessageHandler
{
    Task HandleAsync(IReadOnlyList<Message> messages, CancellationToken ct = default);
}

/// <summary>No-op default for hosts that only use the sync engine.</summary>
public sealed class NullInboundMessageHandler : IInboundMessageHandler
{
    public Task HandleAsync(IReadOnlyList<Message> messages, CancellationToken ct = default) => Task.CompletedTask;
}
