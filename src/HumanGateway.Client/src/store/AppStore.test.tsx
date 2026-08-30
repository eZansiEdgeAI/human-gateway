import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { ReactNode } from 'react'
import { AppStoreProvider } from './AppStore'
import { useAppStore, type SendOutcome } from './context'
import { resetDatabaseForTests } from '../db/database'
import { getTask, putTask } from '../db/tasks'
import type { EdgeApiClient } from '../api'
import { makeConversationView, makeHumanTask, makeSendMessageRequest } from '../test/fixtures'

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', { value, configurable: true })
}

function fakeApi(overrides: Partial<EdgeApiClient> = {}): EdgeApiClient {
  return {
    listConversations: async () => [],
    getConversation: async () => null,
    listConversationMessages: async () => [],
    getMessage: async () => null,
    listTasks: async () => [],
    getTask: async () => null,
    listArtifacts: async () => [],
    getArtifact: async () => null,
    getSyncStatus: async () => ({
      gatewayId: 'gw',
      queued: 0,
      lastSequence: 0,
      deliveries: {
        queued: 0,
        syncing: 0,
        delivered: 0,
        acknowledged: 0,
        waitingForSync: 0,
        failed: 0,
      },
    }),
    sendMessage: async () => ({ disposition: 'queued', outboxId: 'o1' }),
    answerTask: async () => ({ disposition: 'queued', outboxId: 'o1' }),
    createConversation: async () => ({ disposition: 'queued', outboxId: 'o1' }),
    createTask: async () => ({ disposition: 'queued', outboxId: 'o1' }),
    registerArtifact: async () => ({ disposition: 'queued', outboxId: 'o1' }),
    ...overrides,
  }
}

function renderStore(api: EdgeApiClient) {
  const wrapper = ({ children }: { children: ReactNode }) => (
    <AppStoreProvider api={api}>{children}</AppStoreProvider>
  )
  return renderHook(() => useAppStore(), { wrapper })
}

describe('AppStore', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  afterEach(() => {
    setOnline(true)
  })

  it('loads conversations on mount and exposes them', async () => {
    const conversation = makeConversationView({ title: 'Assessment', participants: [] })
    const api = fakeApi({ listConversations: async () => [conversation] })

    const { result } = renderStore(api)

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.conversations).toEqual([conversation])
  })

  it('sendMessage persists an optimistic draft and queues it', async () => {
    const api = fakeApi()
    const { result } = renderStore(api)
    await waitFor(() => expect(result.current.loading).toBe(false))

    const request = makeSendMessageRequest({ conversationId: 'conv-1' })
    let outcome: SendOutcome | undefined
    await act(async () => {
      outcome = await result.current.sendMessage(request)
    })

    expect(outcome?.disposition).toBe('queued')
    expect(outcome?.conversationId).toBe('conv-1')

    await waitFor(() => expect(result.current.threads['conv-1']).toHaveLength(1))
    expect(result.current.threads['conv-1'][0].message.id).toBe(outcome?.message.message.id)
  })

  it('loads tasks on mount and exposes them', async () => {
    const task = makeHumanTask({ subject: 'Attendance photo' })
    const api = fakeApi({ listTasks: async () => [task] })

    const { result } = renderStore(api)

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.tasks).toEqual([task])
  })

  it('answerTask marks an offline-answered task RESPONSE_RECEIVED locally', async () => {
    setOnline(false)
    const task = makeHumanTask({ kind: 'approval', status: 'DELIVERED_TO_HUMAN' })
    await putTask(task)
    const api = fakeApi()
    const { result } = renderStore(api)
    await waitFor(() => expect(result.current.loading).toBe(false))

    await act(async () => {
      await result.current.answerTask(task.id, {
        respondedBy: { address: 'human:teacher@school.example', displayName: 'Teacher' },
        decision: 'approved',
        reason: 'Looks good',
      })
    })

    const stored = await getTask(task.id)
    expect(stored?.status).toBe('RESPONSE_RECEIVED')
    expect(stored?.response?.decision).toBe('approved')
    expect(stored?.response?.reason).toBe('Looks good')
  })

  it('answerTask stores the server-issued task when sent', async () => {
    const task = makeHumanTask({ kind: 'input', status: 'REQUESTED' })
    const answered = {
      ...task,
      status: 'RESPONSE_RECEIVED' as const,
      response: { text: '42', respondedAt: new Date().toISOString() },
    }
    const api = fakeApi({
      listTasks: async () => [answered],
      answerTask: async () => ({ disposition: 'sent' as const, entity: answered, outboxId: 'o1' }),
    })
    const { result } = renderStore(api)
    await waitFor(() => expect(result.current.loading).toBe(false))

    await act(async () => {
      await result.current.answerTask(task.id, {
        respondedBy: { address: 'human:teacher@school.example', displayName: 'Teacher' },
        text: '42',
      })
    })

    const stored = await getTask(task.id)
    expect(stored?.status).toBe('RESPONSE_RECEIVED')
    expect(stored?.response?.text).toBe('42')
  })
})
