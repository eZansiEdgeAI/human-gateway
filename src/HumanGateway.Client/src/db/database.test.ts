import { beforeEach, describe, expect, it } from 'vitest'
import {
  countValues,
  deleteValue,
  getAllByIndex,
  getAllValues,
  getDatabase,
  getValue,
  putValue,
  resetDatabaseForTests,
  updateValue,
} from './database'
import { DB_NAME, DB_VERSION, INDEXES, STORES } from './schema'

describe('IndexedDB database', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  it('opens and creates the four object stores with the expected indexes', async () => {
    const db = await getDatabase()
    expect(db.name).toBe(DB_NAME)
    expect(db.version).toBe(DB_VERSION)

    expect(Array.from(db.objectStoreNames).sort()).toEqual(
      [STORES.conversations, STORES.messages, STORES.tasks, STORES.outbox].sort(),
    )

    const messages = db.transaction(STORES.messages).objectStore(STORES.messages)
    expect(Array.from(messages.indexNames)).toEqual([INDEXES.messagesByConversation])

    const tasks = db.transaction(STORES.tasks).objectStore(STORES.tasks)
    expect(Array.from(tasks.indexNames)).toEqual([INDEXES.tasksByStatus])

    const outbox = db.transaction(STORES.outbox).objectStore(STORES.outbox)
    expect(Array.from(outbox.indexNames)).toEqual([INDEXES.outboxByState])
  })

  it('round-trips a value through put/get', async () => {
    const record = { id: 'conv-1', title: 'Hello' }
    await putValue(STORES.conversations, record)

    expect(await getValue<typeof record>(STORES.conversations, 'conv-1')).toEqual(record)
  })

  it('returns undefined for a missing key', async () => {
    expect(await getValue(STORES.conversations, 'missing')).toBeUndefined()
  })

  it('deletes a value', async () => {
    await putValue(STORES.conversations, { id: 'conv-1', title: 'Hello' })
    await deleteValue(STORES.conversations, 'conv-1')
    expect(await getValue(STORES.conversations, 'conv-1')).toBeUndefined()
  })

  it('lists all values and filters by index', async () => {
    await putValue(STORES.messages, { message: { id: 'm1', conversationId: 'c1' } })
    await putValue(STORES.messages, { message: { id: 'm2', conversationId: 'c2' } })
    await putValue(STORES.messages, { message: { id: 'm3', conversationId: 'c1' } })

    expect(await getAllValues(STORES.messages)).toHaveLength(3)

    const byConversation = await getAllByIndex<{ message: { id: string } }>(
      STORES.messages,
      INDEXES.messagesByConversation,
      'c1',
    )
    expect(byConversation.map((v) => v.message.id).sort()).toEqual(['m1', 'm3'])
  })

  it('counts values, optionally by index', async () => {
    await putValue(STORES.outbox, { id: 'o1', state: 'PENDING' })
    await putValue(STORES.outbox, { id: 'o2', state: 'FAILED' })

    expect(await countValues(STORES.outbox)).toBe(2)
    expect(await countValues(STORES.outbox, INDEXES.outboxByState, 'PENDING')).toBe(1)
  })

  it('applies a read-modify-write mutation without a lost update', async () => {
    await putValue(STORES.outbox, { id: 'o1', state: 'PENDING', attempts: 0 })

    await updateValue<{ id: string; state: string; attempts: number }>(
      STORES.outbox,
      'o1',
      (current) => (current ? { ...current, state: 'SYNCING' } : undefined),
    )

    expect(await getValue(STORES.outbox, 'o1')).toMatchObject({ state: 'SYNCING' })
  })

  it('skips the write when the mutator returns undefined', async () => {
    await updateValue(STORES.outbox, 'missing', () => undefined)
    expect(await getValue(STORES.outbox, 'missing')).toBeUndefined()
  })
})
