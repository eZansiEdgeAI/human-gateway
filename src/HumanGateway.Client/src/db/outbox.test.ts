import { beforeEach, describe, expect, it } from 'vitest'
import {
  countPendingOutbox,
  createOutboxEntry,
  enqueueOutbox,
  getOutboxEntry,
  listOutbox,
  listPendingOutbox,
  markOutboxFailed,
  markOutboxSucceeded,
  markOutboxSyncing,
  type OutboxEntry,
} from './outbox'
import { resetDatabaseForTests } from './database'
import { makeSendMessageRequest } from '../test/fixtures'

describe('outbox', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  it('enqueues an entry as PENDING and reads it back', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    expect(await getOutboxEntry(entry.id)).toEqual(entry)
  })

  it('persists a PENDING entry before any flush (durable local-first write)', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    expect(await countPendingOutbox()).toBe(1)
    expect(entry.state).toBe('PENDING')
  })

  it('lists PENDING entries in FIFO order for flushing', async () => {
    const first = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    const second = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })

    await enqueueOutbox(first)
    await enqueueOutbox(second)

    const pending = await listPendingOutbox()
    expect(pending.map((e) => e.id)).toEqual([first.id, second.id])
  })

  it('advances an entry SYNCING → gone on success', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    await markOutboxSyncing(entry.id)
    expect((await getOutboxEntry(entry.id))?.state).toBe('SYNCING')

    await markOutboxSucceeded(entry.id)
    expect(await getOutboxEntry(entry.id)).toBeUndefined()
    expect(await countPendingOutbox()).toBe(0)
  })

  it('marks an entry FAILED, increments attempts, and records the error', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    await markOutboxFailed(entry.id, {
      code: 'TIMEOUT',
      message: 'Edge unreachable',
      retryable: true,
    })

    const failed = await getOutboxEntry(entry.id)
    expect(failed).toMatchObject({
      state: 'FAILED',
      attempts: 1,
      lastError: { code: 'TIMEOUT', retryable: true },
    })
  })

  it('preserves the localEntityId used to reconcile the draft on flush', async () => {
    const entry = createOutboxEntry(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { localEntityId: 'draft-1' },
    )
    await enqueueOutbox(entry)

    const stored = await getOutboxEntry(entry.id)
    expect(stored?.localEntityId).toBe('draft-1')
  })

  it('lists all entries (including FAILED) for the sync banner', async () => {
    const pending = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    const failed = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(pending)
    await enqueueOutbox(failed)
    await markOutboxFailed(failed.id, { code: 'INTERNAL_ERROR', message: 'boom' })

    const all: OutboxEntry[] = await listOutbox()
    expect(all).toHaveLength(2)
    expect(await countPendingOutbox()).toBe(1)
  })
})
