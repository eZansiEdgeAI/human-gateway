using HumanGateway.Core.Retry;

namespace HumanGateway.Edge.Sync;

/// <summary>
/// Configuration for the background sync worker (EDGE-FR-05), bound from the <c>Sync</c> configuration
/// section. The worker is outbound-only (SP-01): it periodically pushes the durable outbox to the Relay and
/// pulls inbound batches, retrying transient failures with capped, jittered exponential backoff (SYNC-FR-04,
/// synchronisation Open Q #2).
/// </summary>
public sealed class SyncWorkerOptions
{
    public const string SectionName = "Sync";

    /// <summary>Maximum items per push batch (clamped to the engine's batch-item cap).</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Delay between successful sync cycles (seconds).</summary>
    public int PollIntervalSeconds { get; init; } = 30;

    /// <summary>Backoff shape for transient sync failures.</summary>
    public SyncBackoffOptions Backoff { get; init; } = new();

    /// <summary>The delay between successful cycles.</summary>
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds));
}

/// <summary>Backoff settings (seconds/attempts) mapped onto the core <see cref="BackoffPolicy"/>.</summary>
public sealed class SyncBackoffOptions
{
    /// <summary>Delay before the first retry (seconds).</summary>
    public int BaseDelaySeconds { get; init; } = 1;

    /// <summary>Upper bound on any single backoff delay (seconds).</summary>
    public int MaxDelaySeconds { get; init; } = 300;

    /// <summary>Maximum attempts before a delivery becomes FAILED (configurable).</summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>Builds the core <see cref="BackoffPolicy"/> that drives retry-delay computation.</summary>
    public BackoffPolicy ToPolicy() => new()
    {
        BaseDelay = TimeSpan.FromSeconds(Math.Max(1, BaseDelaySeconds)),
        MaxDelay = TimeSpan.FromSeconds(Math.Max(1, MaxDelaySeconds)),
        MaxAttempts = Math.Max(1, MaxAttempts),
    };
}
