/**
 * Offline-first write wrapper (PWA-FR-02).
 *
 * The single entry point for every mutation the PWA makes. It guarantees
 * local-first ordering — the operation is durably enqueued to IndexedDB
 * *before* any network attempt (EDGE-FR-04 mirrors this on the server) — then
 * attempts to flush immediately when the device is online:
 *
 *   - **Online**  → the entry is flushed right away; the caller receives the
 *                   server-issued entity with `disposition: 'sent'`.
 *   - **Offline** → the entry stays PENDING in the outbox; the caller receives
 *                   `disposition: 'queued'` and the flush worker will replay it
 *                   when connectivity returns.
 *
 * Because the write is persisted first, a message can never be lost to a kill
 * between "the user hit send" and "the bytes left the device" (offline-pwa §6,
 * scenario 2).
 */

import type { OutboxEntry, OutboxOperation } from '../db/outbox'
import { createOutboxEntry, enqueueOutbox } from '../db/outbox'
import { isOnline } from '../lib/connectivity'
import { flushEntry } from './flush'
import type { FlushDeps } from './flush'
import type { OperationResult } from './dispatcher'

/** Result of an offline-first write. */
export type WriteOutcome<T> =
  | { disposition: 'sent'; entity: T; outboxId: string; localEntityId?: string }
  | { disposition: 'queued'; outboxId: string; localEntityId?: string }

export interface WriteOptions extends FlushDeps {
  /**
   * Local id of the optimistic draft this write flushes (the id the UI shows
   * before the Edge assigns its own). Reconciled to the server-issued entity on
   * a successful flush.
   */
  localEntityId?: string
}

/**
 * Enqueues a write durably, then flushes it immediately when online. The
 * `pick` mapper narrows the dispatcher's {@link OperationResult} union back to
 * the concrete entity type the caller expects.
 */
export async function enqueueWrite<T>(
  operation: OutboxOperation,
  options: { pick: (result: OperationResult) => T } & WriteOptions,
): Promise<WriteOutcome<T>> {
  const entry: OutboxEntry = createOutboxEntry(operation, {
    localEntityId: options.localEntityId,
  })

  // Durable write BEFORE any network attempt (local-first ordering).
  await enqueueOutbox(entry)

  if (isOnline()) {
    const outcome = await flushEntry(entry, {
      dispatch: options.dispatch,
      reconcile: options.reconcile,
    })
    if (outcome.status === 'succeeded') {
      return {
        disposition: 'sent',
        entity: options.pick(outcome.result),
        outboxId: entry.id,
        localEntityId: options.localEntityId,
      }
    }
  }

  return {
    disposition: 'queued',
    outboxId: entry.id,
    localEntityId: options.localEntityId,
  }
}
