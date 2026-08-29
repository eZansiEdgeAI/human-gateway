namespace HumanGateway.Core.Retry;

/// <summary>
/// Exponential backoff with full jitter, capped (synchronisation Open Q #2 default, EDGE-FR-06). Plain
/// exponential backoff would thunder on large client counts; the delay is capped and jittered so retries
/// from many concurrent deliveries do not synchronise.
/// </summary>
public sealed record BackoffPolicy
{
    /// <summary>Delay before the first retry (attempt 0).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound on any single backoff delay.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Default maximum attempts before a delivery becomes FAILED (configurable).</summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>The default policy (1s base, 5m cap, 8 attempts).</summary>
    public static BackoffPolicy Default => new();

    /// <summary>
    /// Computes the next backoff delay for <paramref name="attempt"/> (0-based). Full jitter: a uniform
    /// value in <c>[0, cappedDelay]</c>. Pass a seeded <see cref="Random"/> for deterministic tests.
    /// </summary>
    public TimeSpan NextDelay(int attempt, Random? random = null)
    {
        var rnd = random ?? Random.Shared;
        var exponent = Math.Max(0, attempt);
        // Cap the shift so very large attempt counts cannot overflow; the MaxDelay cap bounds the result anyway.
        var shift = Math.Min(exponent, 30);
        var uncappedMs = BaseDelay.TotalMilliseconds * Math.Pow(2.0, shift);
        var cappedMs = Math.Min(uncappedMs, MaxDelay.TotalMilliseconds);
        var jitteredMs = Math.Max(0.0, cappedMs * rnd.NextDouble());
        return TimeSpan.FromMilliseconds(jitteredMs);
    }

    /// <summary>Computes the earliest time the next attempt may run.</summary>
    public DateTimeOffset NextRetryAt(int attempt, DateTimeOffset now, Random? random = null)
        => now + NextDelay(attempt, random);

    /// <summary>True when <paramref name="attempts"/> has not yet reached <see cref="MaxAttempts"/>.</summary>
    public bool ShouldRetry(int attempts) => attempts < MaxAttempts;

    /// <summary>True when <paramref name="attempts"/> has not yet reached an explicit <paramref name="maxAttempts"/>.</summary>
    public static bool ShouldRetry(int attempts, int maxAttempts) => attempts < maxAttempts;
}
