import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enqueueWrite } from './offlineFirst'
import { resetDatabaseForTests } from '../db/database'
import { countPendingOutbox, getOutboxEntry, listOutbox } from '../db/outbox'
import { getMessage } from '../db/messages'
import { makeMessageView, makeSendMessageRequest } from '../test/fixtures'
import type { MessageView } from '../types/protocol'
import type { OperationResult } from './dispatcher'

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', { value, configurable: true })
}

function pickMessage(result: OperationResult): MessageView {
  if (result.type !== 'sendMessage') throw new Error(`unexpected result type ${result.type}`)
  return result.result
}

describe('offline-first write wrapper', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  afterEach(() => {
    setOnline(true)
    vi.restoreAllMocks()
  })

  it('queues durably and does not touch the network when offline', async () => {
    setOnline(false)
    const dispatch = vi.fn()

    const outcome = await enqueueWrite<MessageView>(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { dispatch, pick: pickMessage },
    )

    expect(outcome.disposition).toBe('queued')
    expect(dispatch).not.toHaveBeenCalled()
    expect(await countPendingOutbox()).toBe(1)
    expect(await getOutboxEntry(outcome.outboxId)).toMatchObject({ state: 'PENDING' })
  })

  it('flushes immediately when online and returns the server-issued entity', async () => {
    setOnline(true)
    const view = makeMessageView()
    const dispatch = vi.fn(async (): Promise<OperationResult> => ({ type: 'sendMessage', result: view }))

    const outcome = await enqueueWrite<MessageView>(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { dispatch, pick: pickMessage },
    )

    expect(outcome.disposition).toBe('sent')
    if (outcome.disposition !== 'sent') throw new Error('expected sent outcome')
    expect(outcome.entity).toEqual(view)
    // The entry is removed and the canonical server message is persisted.
    expect(await getOutboxEntry(outcome.outboxId)).toBeUndefined()
    expect(await getMessage(view.message.id)).toEqual(view)
  })

  it('persists the entry before any network attempt (local-first ordering)', async () => {
    setOnline(true)
    const dispatch = vi.fn(async () => {
      // At the moment the dispatcher runs, the durable write must already exist.
      expect(await listOutbox()).toHaveLength(1)
      return { type: 'sendMessage', result: makeMessageView() } satisfies OperationResult
    })

    await enqueueWrite<MessageView>(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { dispatch, pick: pickMessage },
    )

    expect(dispatch).toHaveBeenCalledTimes(1)
  })

  it('reports queued and leaves a retryable FAILED entry when an online flush fails', async () => {
    setOnline(true)
    const dispatch = vi.fn(async (): Promise<OperationResult> => {
      throw new Error('Edge unreachable')
    })

    const outcome = await enqueueWrite<MessageView>(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { dispatch, pick: pickMessage },
    )

    expect(outcome.disposition).toBe('queued')
    expect(await getOutboxEntry(outcome.outboxId)).toMatchObject({ state: 'FAILED', attempts: 1 })
  })
})
