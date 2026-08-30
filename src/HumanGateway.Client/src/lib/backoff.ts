/**
 * Exponential backoff for the Inbox polling loop (offline-pwa Open Q #2).
 *
 * v1 fetches new messages with HTTP polling — not WebSockets — so the loop must
 * back off on failure and reset on success to stay polite on a flaky LAN.
 * Delays are plain millisecond counts; the store owns the `setTimeout` loop.
 */

export interface BackoffPolicy {
  /** Milliseconds to wait before the next attempt. */
  delay(): number
  /** Resets the backoff to the base interval (call after a successful poll). */
  reset(): void
  /** Grows the backoff toward the cap (call after a failed poll). */
  backoff(): void
}

export interface BackoffOptions {
  /** Initial interval in ms (default 15s). */
  baseMs?: number
  /** Upper bound in ms (default 5min). */
  maxMs?: number
  /** Growth factor (default 2x). */
  factor?: number
}

/** Creates a new exponential-backoff policy with the given bounds. */
export function createBackoffPolicy(options: BackoffOptions = {}): BackoffPolicy {
  const baseMs = options.baseMs ?? 15_000
  const maxMs = options.maxMs ?? 300_000
  const factor = options.factor ?? 2
  let current = baseMs

  return {
    delay: () => current,
    reset: () => {
      current = baseMs
    },
    backoff: () => {
      current = Math.min(maxMs, Math.max(baseMs, Math.round(current * factor)))
    },
  }
}
