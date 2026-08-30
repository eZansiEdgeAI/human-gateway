/**
 * Outbox flush worker (PWA-FR-02).
 *
 * Replays durable outbox entries against the Edge, driving the lifecycle the
 * outbox store describes:
 *
 *   enqueue (PENDING) → markSyncing (SYNCING) → markSucceeded (deleted)
 *                                             ↘ markFailed (FAILED, retried later)
 *
 * On success the server-issued entity is persisted to the local IndexedDB
 * stores (and the optimistic draft, when present, is reconciled away) so the
 * thread/task views render the canonical records with no network. On failure
 * the entry is marked FAILED with the mapped {@link ProtocolError}; retryable
 * failures are requeued at the start of the next flush.
 *
 * The flush runs on demand (after an offline-first write) and is re-triggered
 * when connectivity returns; the HTTP polling loop that schedules it lands with
 * the Inbox/Outbox + sync-banner task (offline-pwa Open Q #2).
 */

import type { OutboxEntry } from '../db/outbox'
import {
  countPendingOutbox,
  listPendingOutbox,
  markOutboxFailed,
  markOutboxSucceeded,
  markOutboxSyncing,
  requeueFailedOutbox,
} from '../db/outbox'
import { deleteMessage, putMessage } from '../db/messages'
import { putConversation } from '../db/conversations'
import { putTask } from '../db/tasks'
import type { ProtocolError } from '../types/protocol'
import { toProtocolError } from './http'
import {
  dispatchOperation,
  type DispatchOperation,
  type OperationResult,
} from './dispatcher'

/** Injected dependencies so tests can drive the flush without a live Edge. */
export interface FlushDeps {
  /** Replays one operation (defaults to the real HTTP dispatcher). */
  dispatch?: DispatchOperation
  /** Persists the server-issued entity on success (defaults to {@link reconcileSuccess}). */
  reconcile?: ReconcileOperation
}

export type ReconcileOperation = (
  entry: OutboxEntry,
  result: OperationResult,
) => Promise<void>

/** Outcome of flushing a single entry. */
export type FlushEntryOutcome =
  | { status: 'succeeded'; result: OperationResult }
  | { status: 'failed'; error: ProtocolError }

/** Summary of a full flush pass. */
export interface FlushResult {
  /** Entries that succeeded and were removed from the outbox. */
  flushed: number
  /** Entries that failed (left FAILED for the next pass). */
  failed: number
  /** PENDING entries remaining after the pass. */
  remainingPending: number
}

/**
 * Flushes a single entry through its full lifecycle and returns the outcome.
 * Callers own the surrounding retry/backoff policy; this just advances state.
 */
export async function flushEntry(
  entry: OutboxEntry,
  deps: FlushDeps = {},
): Promise<FlushEntryOutcome> {
  const dispatch = deps.dispatch ?? dispatchOperation
  const reconcile = deps.reconcile ?? reconcileSuccess

  await markOutboxSyncing(entry.id)
  try {
    const result = await dispatch(entry.operation)
    await reconcile(entry, result)
    await markOutboxSucceeded(entry.id)
    return { status: 'succeeded', result }
  } catch (error) {
    const protocolError = toProtocolError(error)
    await markOutboxFailed(entry.id, protocolError)
    return { status: 'failed', error: protocolError }
  }
}

/**
 * Flushes every PENDING entry, oldest first. Requeues retryable FAILED entries
 * first so a previous transient failure is retried on this pass. Idempotent and
 * safe to call concurrently (each entry is guarded by its own state read).
 */
export async function flushOutbox(deps: FlushDeps = {}): Promise<FlushResult> {
  await requeueFailedOutbox()

  const pending = await listPendingOutbox()
  let flushed = 0
  let failed = 0
  for (const entry of pending) {
    const outcome = await flushEntry(entry, deps)
    if (outcome.status === 'succeeded') {
      flushed += 1
    } else {
      failed += 1
    }
  }

  return { flushed, failed, remainingPending: await countPendingOutbox() }
}

/**
 * Persists the server-issued entity for a successful flush and reconciles the
 * optimistic draft away, so the local views hold the canonical record.
 */
export async function reconcileSuccess(
  entry: OutboxEntry,
  result: OperationResult,
): Promise<void> {
  switch (result.type) {
    case 'sendMessage': {
      await putMessage(result.result)
      // Drop the optimistic draft (localEntityId) once the Edge has assigned
      // its canonical id, so the thread never shows both.
      if (entry.localEntityId && entry.localEntityId !== result.result.message.id) {
        await deleteMessage(entry.localEntityId)
      }
      break
    }
    case 'answerTask':
    case 'createTask':
      await putTask(result.result)
      break
    case 'createConversation':
      await putConversation(result.result)
      break
    case 'registerArtifact':
      // Artifact metadata has no local store yet (artifact-engineer task); the
      // bytes land via the Edge artifact store, not the PWA.
      break
  }
}
