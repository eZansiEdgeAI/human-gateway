/**
 * IndexedDB access layer for the offline PWA (PWA-FR-01, PWA-FR-02).
 *
 * A single lazily-opened database plus small, promise-based helpers over the
 * raw IndexedDB API. Every repository (`outbox.ts`, `conversations.ts`,
 * `messages.ts`, `tasks.ts`) is built on these helpers, so the transaction
 * plumbing lives in exactly one place.
 *
 * The singleton is cached for the life of the app; `resetDatabaseForTests()`
 * closes and forgets it so test suites can start from a clean database.
 */

import { DB_NAME, DB_VERSION, upgradeDatabase, type StoreName } from './schema'

let dbPromise: Promise<IDBDatabase> | null = null

/** Opens (once) and returns the app database, running migrations if needed. */
export function getDatabase(): Promise<IDBDatabase> {
  if (!dbPromise) {
    dbPromise = openDatabase()
  }
  return dbPromise
}

/** Opens a fresh connection, awaiting `upgradeneeded` before use. */
function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)

    request.onupgradeneeded = () => {
      upgradeDatabase(request.result)
    }

    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('Failed to open IndexedDB.'))
    request.onblocked = () =>
      reject(new Error('IndexedDB upgrade blocked by another open connection.'))
  })
}

/**
 * Deletes the database and forgets the cached connection. Intended for tests;
 * never call in production (it would wipe the offline store).
 */
export async function deleteDatabase(): Promise<void> {
  dbPromise = null
  await new Promise<void>((resolve, reject) => {
    const request = indexedDB.deleteDatabase(DB_NAME)
    request.onsuccess = () => resolve()
    request.onerror = () => reject(request.error)
    request.onblocked = () => resolve()
  })
}

/** Closes and forgets the cached connection so the next call reopens. */
export async function resetDatabaseForTests(): Promise<void> {
  if (dbPromise) {
    const db = await dbPromise
    db.close()
  }
  dbPromise = null
  await deleteDatabase()
}

// ---------------------------------------------------------------------------
// Promise helpers
// ---------------------------------------------------------------------------

/** Wraps a single IDBRequest in a promise. */
function requestToPromise<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed.'))
  })
}

/**
 * Runs `fn` inside a transaction over `storeName`. `fn` receives the object
 * store and must issue its IDBRequests synchronously (chained within their
 * `onsuccess` handlers) so the transaction does not auto-commit mid-operation.
 */
function withStore<T>(
  storeName: StoreName,
  mode: IDBTransactionMode,
  fn: (store: IDBObjectStore) => Promise<T> | T,
): Promise<T> {
  return getDatabase().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const tx = db.transaction(storeName, mode)
        const store = tx.objectStore(storeName)

        let result: Promise<T> | T
        try {
          result = fn(store)
        } catch (error) {
          reject(error)
          return
        }

        Promise.resolve(result).then(resolve, reject)
        tx.onerror = () => reject(tx.error ?? new Error('IndexedDB transaction failed.'))
        tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted.'))
      }),
  )
}

/** Gets a single value by primary key, or `undefined` when absent. */
export function getValue<V>(storeName: StoreName, key: IDBValidKey): Promise<V | undefined> {
  return withStore(storeName, 'readonly', (store) =>
    requestToPromise(store.get(key) as IDBRequest<V | undefined>),
  )
}

/** Puts (inserts or overwrites) a value keyed by its keyPath. */
export function putValue<V>(storeName: StoreName, value: V): Promise<IDBValidKey> {
  return withStore(storeName, 'readwrite', (store) => requestToPromise(store.put(value)))
}

/** Deletes a value by primary key. */
export function deleteValue(storeName: StoreName, key: IDBValidKey): Promise<undefined> {
  return withStore(storeName, 'readwrite', (store) => requestToPromise(store.delete(key)))
}

/** Returns every value in the store. */
export function getAllValues<V>(storeName: StoreName): Promise<V[]> {
  return withStore(storeName, 'readonly', (store) => requestToPromise(store.getAll() as IDBRequest<V[]>))
}

/** Returns every value matching `index` on `query`. */
export function getAllByIndex<V>(
  storeName: StoreName,
  indexName: string,
  query: IDBValidKey,
): Promise<V[]> {
  return withStore(storeName, 'readonly', (store) =>
    requestToPromise(store.index(indexName).getAll(query) as IDBRequest<V[]>),
  )
}

/** Counts values in the store, optionally restricted to an index query. */
export function countValues(
  storeName: StoreName,
  indexName?: string,
  query?: IDBValidKey,
): Promise<number> {
  return withStore(storeName, 'readonly', (store) => {
    const source: IDBObjectStore | IDBIndex = indexName ? store.index(indexName) : store
    return requestToPromise(source.count(query))
  })
}

/**
 * Reads a value by primary key, applies `mutator`, and writes the result back —
 * all within one `readwrite` transaction. If no value exists, `mutator` is
 * invoked with `undefined` and its (possibly `undefined`) result decides
 * whether anything is written. This is the read-modify-write primitive the
 * outbox uses to advance an entry's state without a lost update.
 */
export function updateValue<V>(
  storeName: StoreName,
  key: IDBValidKey,
  mutator: (current: V | undefined) => V | undefined,
): Promise<void> {
  return withStore(storeName, 'readwrite', (store) => {
    return new Promise<void>((resolve, reject) => {
      const getRequest = store.get(key)
      getRequest.onsuccess = () => {
        const next = mutator(getRequest.result as V | undefined)
        if (next === undefined) {
          resolve()
          return
        }
        const putRequest = store.put(next)
        putRequest.onsuccess = () => resolve()
        putRequest.onerror = () => reject(putRequest.error)
      }
      getRequest.onerror = () => reject(getRequest.error)
    })
  })
}
