using HumanGateway.Core.Retry;
using HumanGateway.Edge.Sync;
using Microsoft.Extensions.Options;

namespace HumanGateway.Edge.Security;

/// <summary>
/// Configuration for the gateway registration worker (AUTH-FR-01), bound from the <c>Sync</c> configuration
/// section (alongside the sync worker options). The registration worker runs the two-step handshake before
/// sync begins, retrying transient Relay failures with the same capped, jittered exponential backoff the sync
/// worker uses (SYNC-FR-04), and then keeps the token fresh by rotating before it expires.
/// </summary>
public sealed class GatewayRegistrationWorkerOptions
{
    /// <summary>The configuration section this options type binds to.</summary>
    public const string SectionName = "Sync";

    /// <summary>Re-check interval after a successful registration (token-expiry watch, seconds).</summary>
    public int PollIntervalSeconds { get; init; } = 6 * 60 * 60; // 6 h

    /// <summary>
    /// Rotation is triggered when the token expires within this horizon (seconds). Default 48 h — comfortably
    /// inside the Relay's 30-day token TTL so a long weekend of darkness cannot strand a school.
    /// </summary>
    public int RotateWithinSeconds { get; init; } = 48 * 60 * 60;

    /// <summary>Backoff shape for transient registration failures.</summary>
    public SyncBackoffOptions Backoff { get; init; } = new();

    /// <summary>The delay between successful registration checks.</summary>
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(60, PollIntervalSeconds));

    /// <summary>The rotation-now horizon.</summary>
    public TimeSpan RotateWithin => TimeSpan.FromSeconds(Math.Max(60, RotateWithinSeconds));
}
