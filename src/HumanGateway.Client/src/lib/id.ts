/**
 * Durable local ID generation for the offline PWA (PWA-FR-02).
 *
 * The Edge assigns the protocol-level entity IDs (message/task/delivery ids,
 * see `HumanGateway.Core/Ids/IdGenerator.cs`), but the PWA still needs stable
 * local IDs for two things that exist purely client-side:
 *
 *  1. Outbox entries — each queued operation gets a durable local ID so a
 *     flush can be tracked, retried, and reconciled idempotently.
 *  2. Optimistic drafts — a message composed offline is shown immediately
 *     under a local ID and reconciled to the server-assigned ID once the Edge
 *     accepts it.
 *
 * `crypto.randomUUID()` returns a version-4 UUID, matching the protocol's
 * recommendation (UUIDv4 or ULID, `common.schema.json` `$defs/id`). A fallback
 * keeps non-secure contexts (and older webviews) working.
 */

export function newId(): string {
  const cryptoApi = globalThis.crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return cryptoApi.randomUUID()
  }

  // RFC 4122 version-4 fallback for contexts without crypto.randomUUID.
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const r = Math.floor(Math.random() * 16)
    const v = char === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}
