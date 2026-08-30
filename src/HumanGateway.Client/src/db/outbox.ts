/**
 * The durable local outbox (PWA-FR-02).
 *
 * Messages composed offline — and task responses recorded offline — are queued
 * here and replayed against the Edge when connectivity returns (offline-pwa
 * §4: "queued, will sync when connected"). Every entry is an *operation* plus
 * the request payload needed to replay it; the offline-first fetch wrapper
 * (`src/api/`, a later task) drives the lifecycle:
 *
 *   enqueue (PENDING) → markSyncing (SYNCING) → markSucceeded (deleted)
 *                                             ↘ markFailed (FAILED, retried later)
 *
 * Persistence happens *before* any network attempt (EDGE-FR-04 mirrors this on
 * the server), so a message is never lost to a kill between "user hit send"
 * and "bytes left the device".
 */

import type {
  AnswerTaskRequest,
  CreateConversationRequest,
  CreateTaskRequest,
  ProtocolError,
  RegisterArtifactRequest,
  SendMessageRequest,
} from '../types/protocol'
import {
  countValues,
  deleteValue,
  getAllByIndex,
  getAllValues,
  getValue,
  putValue,
  updateValue,
} from './database'
import { INDEXES, STORES } from './schema'
import { newId } from '../lib/id'

/** Outbox entry lifecycle state. */
export type OutboxState = 'PENDING' | 'SYNCING' | 'FAILED'

/**
 * An operation queued for replay against the Edge. The discriminated union
 * mirrors the Edge local REST API endpoints (`LocalApiEndpoints.cs`), so the
 * flush worker can dispatch each entry to the matching HTTP call.
 */
export type OutboxOperation =
  | { type: 'sendMessage'; request: SendMessageRequest }
  | { type: 'answerTask'; taskId: string; request: AnswerTaskRequest }
  | { type: 'createConversation'; request: CreateConversationRequest }
  | { type: 'createTask'; request: CreateTaskRequest }
  | { type: 'registerArtifact'; request: RegisterArtifactRequest }

/** A single durable outbox entry. */
export interface OutboxEntry {
  /** Durable local outbox-entry id (UUIDv4). */
  id: string
  /** The operation to replay against the Edge. */
  operation: OutboxOperation
  /**
   * Local id of the optimistic draft this entry flushes (the locally-generated
   * message/task id shown in the UI before the Edge assigns its own). Set for
   * `sendMessage` and `answerTask` so the flush worker can reconcile the draft
   * to the server-issued entity id on success.
   */
  localEntityId?: string
  state: OutboxState
  /** Completed flush attempts so far. */
  attempts: number
  /** Failure details from the most recent attempt (present when FAILED). */
  lastError?: ProtocolError | null
  createdAt: string
  updatedAt: string
}

export interface NewOutboxEntryOptions {
  /** Local draft id to reconcile on success (see `OutboxEntry.localEntityId`). */
  localEntityId?: string
}

/**
 * Builds a new PENDING outbox entry with a fresh id and timestamps. Purely
 * synchronous — callers then persist it with {@link enqueue}.
 */
export function createOutboxEntry(
  operation: OutboxOperation,
  options: NewOutboxEntryOptions = {},
): OutboxEntry {
  const now = monotonicTimestamp()
  return {
    id: newId(),
    operation,
    localEntityId: options.localEntityId,
    state: 'PENDING',
    attempts: 0,
    lastError: null,
    createdAt: now,
    updatedAt: now,
  }
}

/**
 * Issues an ISO-8601 timestamp that is strictly greater than the last one
 * issued in this session. Wall-clock `Date` only has millisecond precision, so
 * two entries created back-to-back would share a `createdAt` and break the
 * FIFO flush order (whose tiebreaker — the random UUID id — is not insertion
 * ordered). Bumping forward keeps `createdAt` monotonic and therefore a stable
 * sort key for the outbox.
 */
let lastIssuedTimestamp = ''
function monotonicTimestamp(): string {
  const wall = new Date()
  let candidate = wall.toISOString()
  if (lastIssuedTimestamp && candidate <= lastIssuedTimestamp) {
    candidate = new Date(new Date(lastIssuedTimestamp).getTime() + 1).toISOString()
  }
  lastIssuedTimestamp = candidate
  return candidate
}

/** Persists a new outbox entry (durable write before any network attempt). */
export function enqueueOutbox(entry: OutboxEntry): Promise<IDBValidKey> {
  return putValue(STORES.outbox, entry)
}

/** Gets an outbox entry by id, or `undefined` when absent. */
export function getOutboxEntry(id: string): Promise<OutboxEntry | undefined> {
  return getValue<OutboxEntry>(STORES.outbox, id)
}

/** Returns every outbox entry, oldest first (flush order). */
export async function listOutbox(): Promise<OutboxEntry[]> {
  const entries = await getAllValues<OutboxEntry>(STORES.outbox)
  return entries.sort(byCreatedAtAsc)
}

/** Returns every PENDING outbox entry, oldest first (flush order). */
export async function listPendingOutbox(): Promise<OutboxEntry[]> {
  const entries = await getAllByIndex<OutboxEntry>(
    STORES.outbox,
    INDEXES.outboxByState,
    'PENDING',
  )
  return entries.sort(byCreatedAtAsc)
}

/** Counts PENDING outbox entries (for the sync banner's "queued" count). */
export function countPendingOutbox(): Promise<number> {
  return countValues(STORES.outbox, INDEXES.outboxByState, 'PENDING')
}

/** Marks an entry SYNCING (a flush attempt is in flight). */
export function markOutboxSyncing(id: string): Promise<void> {
  return updateValue<OutboxEntry>(STORES.outbox, id, (entry) => {
    if (!entry) return undefined
    return { ...entry, state: 'SYNCING', updatedAt: new Date().toISOString() }
  })
}

/** Removes an entry after a successful flush. */
export function markOutboxSucceeded(id: string): Promise<undefined> {
  return deleteValue(STORES.outbox, id)
}

/** Marks an entry FAILED and records the error, incrementing the attempt count. */
export function markOutboxFailed(id: string, error: ProtocolError): Promise<void> {
  return updateValue<OutboxEntry>(STORES.outbox, id, (entry) => {
    if (!entry) return undefined
    return {
      ...entry,
      state: 'FAILED',
      attempts: entry.attempts + 1,
      lastError: error,
      updatedAt: new Date().toISOString(),
    }
  })
}

/**
 * Requeues FAILED entries whose last error is retryable (or unmarked) back to
 * PENDING, so the next flush retries them. Entries rejected permanently
 * (`retryable === false`) stay FAILED as a dead letter the UI can surface.
 * Returns the number of entries requeued.
 */
export async function requeueFailedOutbox(): Promise<number> {
  const failed = await getAllByIndex<OutboxEntry>(STORES.outbox, INDEXES.outboxByState, 'FAILED')
  let requeued = 0
  for (const entry of failed) {
    if (entry.lastError?.retryable === false) continue
    await updateValue<OutboxEntry>(STORES.outbox, entry.id, (current) =>
      current
        ? { ...current, state: 'PENDING', updatedAt: new Date().toISOString() }
        : undefined,
    )
    requeued += 1
  }
  return requeued
}

function byCreatedAtAsc(a: OutboxEntry, b: OutboxEntry): number {
  return a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id)
}
