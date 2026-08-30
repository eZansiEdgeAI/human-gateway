/**
 * IndexedDB schema for the offline PWA (PWA-FR-01, PWA-FR-02).
 *
 * Four object stores back the offline-first client:
 *
 *  - `conversations` — conversation views for the Inbox/Outbox list.
 *  - `messages`      — message envelopes + their per-recipient delivery
 *                      records, indexed by conversation for the thread view.
 *  - `tasks`         — human tasks, indexed by lifecycle status.
 *  - `outbox`        — the durable local outbox: operations queued offline and
 *                      replayed against the Edge when reachable.
 *
 * The database name is namespaced `humangateway` to match the service-worker
 * cache namespace (`cacheId` in `vite.config.ts`), so the store is clearly
 * identifiable on the origin. `DB_VERSION` must be bumped whenever the schema
 * changes; the `onupgradeneeded` callback performs the migration.
 */

/** Object-store names (typed as a const so they can be keyed safely). */
export const STORES = {
  conversations: 'conversations',
  messages: 'messages',
  tasks: 'tasks',
  outbox: 'outbox',
} as const

export type StoreName = (typeof STORES)[keyof typeof STORES]

/** Index names. */
export const INDEXES = {
  /** `messages` → message.conversationId (thread view). */
  messagesByConversation: 'by-conversation',
  /** `tasks` → task.status (open/answered/expired lists). */
  tasksByStatus: 'by-status',
  /** `outbox` → entry.state (list PENDING for flush). */
  outboxByState: 'by-state',
} as const

/** The IndexedDB database name (namespaced like the SW cache). */
export const DB_NAME = 'humangateway'

/**
 * Schema version. Bump whenever a store or index is added, removed, or its
 * key path changes; the `onupgradeneeded` handler migrates older databases.
 */
export const DB_VERSION = 1

/**
 * Creates the object stores and indexes for a brand-new or upgrading database.
 * Called from the `onupgradeneeded` handler in `openDatabase()`.
 */
export function upgradeDatabase(db: IDBDatabase): void {
  // Conversations — keyed by durable conversation id.
  if (!db.objectStoreNames.contains(STORES.conversations)) {
    db.createObjectStore(STORES.conversations, { keyPath: 'id' })
  }

  // Messages — keyed by the envelope's durable message id, indexed by
  // conversation id so a thread can be listed without a full scan.
  if (!db.objectStoreNames.contains(STORES.messages)) {
    const store = db.createObjectStore(STORES.messages, {
      keyPath: 'message.id',
    })
    store.createIndex(INDEXES.messagesByConversation, 'message.conversationId')
  }

  // Tasks — keyed by durable task id, indexed by lifecycle status.
  if (!db.objectStoreNames.contains(STORES.tasks)) {
    const store = db.createObjectStore(STORES.tasks, { keyPath: 'id' })
    store.createIndex(INDEXES.tasksByStatus, 'status')
  }

  // Outbox — keyed by durable local outbox-entry id, indexed by state.
  if (!db.objectStoreNames.contains(STORES.outbox)) {
    const store = db.createObjectStore(STORES.outbox, { keyPath: 'id' })
    store.createIndex(INDEXES.outboxByState, 'state')
  }
}
