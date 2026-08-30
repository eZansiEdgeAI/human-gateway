import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { loadConversations, loadTasks, loadThread } from './repository'
import { listConversations } from '../db/conversations'
import { listMessagesForConversation } from '../db/messages'
import { putConversation } from '../db/conversations'
import { putMessage } from '../db/messages'
import { listTasks } from '../db/tasks'
import { putTask } from '../db/tasks'
import { resetDatabaseForTests } from '../db/database'
import type { EdgeApiClient } from '../api'
import { makeConversationView, makeHumanTask, makeMessageView } from '../test/fixtures'

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
    sendMessage: async () => ({ disposition: 'queued', outboxId: 'x' }),
    answerTask: async () => ({ disposition: 'queued', outboxId: 'x' }),
    createConversation: async () => ({ disposition: 'queued', outboxId: 'x' }),
    createTask: async () => ({ disposition: 'queued', outboxId: 'x' }),
    registerArtifact: async () => ({ disposition: 'queued', outboxId: 'x' }),
    ...overrides,
  }
}

describe('repository', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  afterEach(() => {
    setOnline(true)
  })

  describe('loadConversations', () => {
    it('returns remote conversations and caches them when online', async () => {
      setOnline(true)
      const remote = [makeConversationView()]
      const api = fakeApi({ listConversations: async () => remote })

      await expect(loadConversations(api)).resolves.toEqual(remote)
      await expect(listConversations()).resolves.toHaveLength(1)
    })

    it('serves the cached copy when offline', async () => {
      const cached = makeConversationView()
      await putConversation(cached)

      setOnline(false)
      const api = fakeApi({ listConversations: async () => [makeConversationView()] })

      const result = await loadConversations(api)
      expect(result).toEqual([cached])
    })

    it('falls back to the cache when the Edge request fails', async () => {
      const cached = makeConversationView()
      await putConversation(cached)

      setOnline(true)
      const api = fakeApi({
        listConversations: async () => {
          throw new Error('network down')
        },
      })

      await expect(loadConversations(api)).resolves.toEqual([cached])
    })
  })

  describe('loadThread', () => {
    it('returns remote messages and caches them when online', async () => {
      setOnline(true)
      const remote = [makeMessageView()]
      const conversationId = remote[0].message.conversationId
      const api = fakeApi({ listConversationMessages: async () => remote })

      await expect(loadThread(conversationId, api)).resolves.toEqual(remote)
      await expect(listMessagesForConversation(conversationId)).resolves.toHaveLength(1)
    })

    it('serves the cached copy when offline', async () => {
      const cached = makeMessageView()
      await putMessage(cached)
      const conversationId = cached.message.conversationId

      setOnline(false)
      await expect(loadThread(conversationId, fakeApi())).resolves.toEqual([cached])
    })
  })

  describe('loadTasks', () => {
    it('returns remote tasks and mirrors them into the cache when online', async () => {
      setOnline(true)
      const remote = [makeHumanTask()]
      const api = fakeApi({ listTasks: async () => remote })

      await expect(loadTasks(api)).resolves.toEqual(remote)
      await expect(listTasks()).resolves.toEqual(remote)
    })

    it('serves the cached copy when offline', async () => {
      const cached = makeHumanTask()
      await putTask(cached)

      setOnline(false)
      await expect(loadTasks(fakeApi())).resolves.toEqual([cached])
    })

    it('falls back to the cache when the Edge request fails', async () => {
      const cached = makeHumanTask()
      await putTask(cached)

      setOnline(true)
      const api = fakeApi({
        listTasks: async () => {
          throw new Error('network down')
        },
      })

      await expect(loadTasks(api)).resolves.toEqual([cached])
    })
  })
})
