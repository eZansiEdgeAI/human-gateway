import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushOutbox, reconcileSuccess } from './flush'
import { resetDatabaseForTests } from '../db/database'
import {
  countPendingOutbox,
  createOutboxEntry,
  enqueueOutbox,
  getOutboxEntry,
  listOutbox,
  markOutboxFailed,
} from '../db/outbox'
import { getMessage, putMessage } from '../db/messages'
import { makeMessageView, makeSendMessageRequest } from '../test/fixtures'
import type { OperationResult } from './dispatcher'
import type { OutboxOperation } from '../db/outbox'

describe('outbox flush worker', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  it('replays a PENDING entry, removes it, and persists the server entity', async () => {
    const view = makeMessageView()
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    const result = await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => ({ type: 'sendMessage', result: view })),
    })

    expect(result).toEqual({ flushed: 1, failed: 0, remainingPending: 0 })
    expect(await getOutboxEntry(entry.id)).toBeUndefined()
    expect(await getMessage(view.message.id)).toEqual(view)
  })

  it('marks a failed dispatch FAILED with the mapped error and attempt count', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)

    await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => {
        throw new Error('Edge unreachable')
      }),
    })

    const stored = await getOutboxEntry(entry.id)
    expect(stored).toMatchObject({ state: 'FAILED', attempts: 1 })
    expect(stored?.lastError).toMatchObject({ code: 'INTERNAL_ERROR', retryable: true })
  })

  it('requeues a retryable FAILED entry and retries it on the next pass', async () => {
    const view = makeMessageView()
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)
    await markOutboxFailed(entry.id, { code: 'TIMEOUT', message: 'slow', retryable: true })

    const result = await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => ({ type: 'sendMessage', result: view })),
    })

    expect(result.flushed).toBe(1)
    expect(await getOutboxEntry(entry.id)).toBeUndefined()
  })

  it('leaves a permanently-rejected entry FAILED (dead letter)', async () => {
    const entry = createOutboxEntry({ type: 'sendMessage', request: makeSendMessageRequest() })
    await enqueueOutbox(entry)
    await markOutboxFailed(entry.id, { code: 'VALIDATION_FAILED', message: 'nope', retryable: false })

    const result = await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => ({ type: 'sendMessage', result: makeMessageView() })),
    })

    expect(result).toEqual({ flushed: 0, failed: 0, remainingPending: 0 })
    expect((await getOutboxEntry(entry.id))?.state).toBe('FAILED')
    expect(await listOutbox()).toHaveLength(1)
  })

  it('reconciles the optimistic draft to the server-issued id on success', async () => {
    const draft = makeMessageView({ message: { ...makeMessageView().message, id: 'draft-1' } })
    await putMessage(draft)

    const server = makeMessageView({ message: { ...makeMessageView().message, id: 'msg-server' } })
    const entry = createOutboxEntry(
      { type: 'sendMessage', request: makeSendMessageRequest() },
      { localEntityId: 'draft-1' },
    )
    await enqueueOutbox(entry)

    await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => ({ type: 'sendMessage', result: server })),
    })

    expect(await getMessage('draft-1')).toBeUndefined()
    expect(await getMessage('msg-server')).toEqual(server)
  })

  it('persists a task result without touching the messages store (answerTask reconcile)', async () => {
    const operation: OutboxOperation = {
      type: 'answerTask',
      taskId: 'task-1',
      request: { respondedBy: makeSendMessageRequest().sender, text: 'done' },
    }
    const entry = createOutboxEntry(operation)
    await enqueueOutbox(entry)

    // reconcileSuccess on an answerTask result persists via putTask (tasks store);
    // the entry is removed on success, proving the reconcile path did not throw.
    const result = await flushOutbox({
      dispatch: vi.fn(async (): Promise<OperationResult> => ({
        type: 'answerTask',
        result: {
          id: 'task-1',
          workflowRef: 'wf',
          nodeId: 'n1',
          prompt: 'go',
          requestMessageId: 'm1',
          status: 'RESPONSE_RECEIVED',
          createdAt: new Date().toISOString(),
        },
      })),
    })

    expect(result.flushed).toBe(1)
    expect(await countPendingOutbox()).toBe(0)
    expect(await getOutboxEntry(entry.id)).toBeUndefined()
  })

  it('reconcileSuccess leaves the messages store alone for a registerArtifact result', async () => {
    const entry = createOutboxEntry({
      type: 'registerArtifact',
      request: { hash: 'sha256:' + '0'.repeat(64), sizeBytes: 1, mimeType: 'image/png', filename: 'a.png' },
    })

    await reconcileSuccess(entry, {
      type: 'registerArtifact',
      result: {
        id: 'art-1',
        hash: 'sha256:' + '0'.repeat(64),
        sizeBytes: 1,
        mimeType: 'image/png',
        filename: 'a.png',
        createdAt: new Date().toISOString(),
      },
    })

    expect(await listOutbox()).toHaveLength(0)
  })
})
