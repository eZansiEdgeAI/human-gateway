/**
 * Application store: the single source of app state (offline-pwa Open Q #1).
 *
 * React Context + hooks + plain TS modules — no external store dependency. The
 * store owns the Inbox/Thread data, connectivity, the outbox flush trigger, and
 * the HTTP polling loop with backoff (offline-pwa Open Q #2), and exposes the
 * write actions the Compose view uses.
 *
 * Reads go through the cache-aware repository (`data/repository.ts`): Edge when
 * online, IndexedDB when offline. Writes go through the offline-first API
 * client, which durably enqueues to the outbox before any network attempt.
 */

import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import type {
  AnswerTaskRequest,
  ConversationView,
  CreateConversationRequest,
  DeliveryState,
  HumanTask,
  MessageView,
  ProtocolError,
  SendMessageRequest,
} from '../types/protocol'
import { edgeApi, flushOutbox, toProtocolError, type EdgeApiClient } from '../api'
import {
  loadConversations,
  loadTasks as loadTasksCached,
  loadThread,
} from '../data/repository'
import { listAllMessages, listMessagesForConversation, putMessage } from '../db/messages'
import { putConversation } from '../db/conversations'
import { getTask, putTask } from '../db/tasks'
import { isOnline, subscribeConnectivity } from '../lib/connectivity'
import { createBackoffPolicy } from '../lib/backoff'
import { messageDeliveryState } from '../lib/delivery'
import { makeOptimisticMessageView } from '../lib/optimistic'
import { markRead } from '../lib/readState'
import { newId } from '../lib/id'
import { AppStoreContext, type AppStoreValue, type SendOutcome, type TaskAnswerOutcome } from './context'

export interface AppStoreProviderProps {
  children: ReactNode
  /** Injectable Edge API client (tests); defaults to the `edgeApi` singleton. */
  api?: EdgeApiClient
}

/**
 * Maps each conversation id to the aggregate delivery state of its newest
 * message, so the Inbox list can show a per-conversation status summary
 * without re-reading every thread (PWA-FR-05).
 */
async function computeLatestStatuses(): Promise<Record<string, DeliveryState>> {
  const all = await listAllMessages()
  const statuses: Record<string, DeliveryState> = {}
  for (const view of all) {
    const state = messageDeliveryState(view)
    if (state) statuses[view.message.conversationId] = state
  }
  return statuses
}

export function AppStoreProvider({ children, api = edgeApi }: AppStoreProviderProps) {
  const [online, setOnline] = useState<boolean>(isOnline)
  const [loading, setLoading] = useState<boolean>(true)
  const [error, setError] = useState<ProtocolError | null>(null)
  const [conversations, setConversations] = useState<ConversationView[]>([])
  const [threads, setThreads] = useState<Record<string, MessageView[]>>({})
  const [tasks, setTasks] = useState<HumanTask[]>([])
  const [latestDeliveryByConversation, setLatestDeliveryByConversation] = useState<
    Record<string, DeliveryState>
  >({})

  // Track connectivity for the sync banner and to gate polling/flush.
  useEffect(() => subscribeConnectivity(setOnline), [])

  /** Re-reads conversations, their delivery statuses, and open tasks. */
  const reload = useCallback(async () => {
    const [nextConversations, statuses, nextTasks] = await Promise.all([
      loadConversations(api),
      computeLatestStatuses(),
      loadTasksCached(api),
    ])
    setConversations(nextConversations)
    setLatestDeliveryByConversation(statuses)
    setTasks(nextTasks)
  }, [api])

  /** Flushes the outbox (when online) and refreshes the conversation list. */
  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      if (isOnline()) {
        await flushOutbox()
      }
      await reload()
    } catch (err) {
      setError(toProtocolError(err))
    } finally {
      setLoading(false)
    }
  }, [reload])

  /** Loads a conversation's thread and records it as read. */
  const openConversation = useCallback(
    async (conversationId: string) => {
      const thread = await loadThread(conversationId, api)
      setThreads((prev) => ({ ...prev, [conversationId]: thread }))
      // Mark read at the newest message so the Inbox unread badge clears.
      const latest = thread.length > 0 ? thread[thread.length - 1].message.createdAt : undefined
      markRead(conversationId, latest)
    },
    [api],
  )

  /** Composes a message: optimistic draft first, then offline-first send. */
  const sendMessage = useCallback(
    async (request: SendMessageRequest): Promise<SendOutcome> => {
      const localId = newId()
      const optimistic = makeOptimisticMessageView(request, localId)
      await putMessage(optimistic)

      const outcome = await api.sendMessage(request, { localEntityId: localId })
      if (outcome.disposition === 'sent') {
        // reconcileSuccess already stored the canonical entity and dropped the
        // draft; re-store defensively so the thread shows the server id.
        await putMessage(outcome.entity)
      }

      const thread = await listMessagesForConversation(request.conversationId)
      setThreads((prev) => ({ ...prev, [request.conversationId]: thread }))
      void reload()

      return {
        disposition: outcome.disposition,
        conversationId: request.conversationId,
        message: optimistic,
      }
    },
    [api, reload],
  )

  /** Creates a conversation (offline-first) and caches the resulting view. */
  const createConversation = useCallback(
    async (request: CreateConversationRequest): Promise<ConversationView> => {
      const outcome = await api.createConversation(request)
      if (outcome.disposition === 'sent') {
        await putConversation(outcome.entity)
        void reload()
        return outcome.entity
      }
      // Queued offline: build a local view so the UI can proceed. Its id is a
      // local placeholder; the Edge assigns the canonical id on flush.
      const optimistic: ConversationView = {
        id: newId(),
        title: request.title,
        participants: request.participants,
        messageCount: 0,
        createdAt: new Date().toISOString(),
      }
      await putConversation(optimistic)
      return optimistic
    },
    [api, reload],
  )

  /** Re-reads the task list (Edge when online, IndexedDB when offline). */
  const loadTasks = useCallback(async () => {
    const nextTasks = await loadTasksCached(api)
    setTasks(nextTasks)
  }, [api])

  /**
   * Answers a task offline-first (PWA-FR-06). The write is durably queued
   * before any network attempt; when the device is offline the local task is
   * optimistically marked answered so the UI reflects the recorded response
   * immediately and won't let the user answer twice.
   */
  const answerTask = useCallback(
    async (taskId: string, request: AnswerTaskRequest): Promise<TaskAnswerOutcome> => {
      const outcome = await api.answerTask(taskId, request)
      if (outcome.disposition === 'sent') {
        // Server issued the canonical task (with its response) — store it.
        await putTask(outcome.entity)
      } else {
        // Queued offline: reflect the response locally so it appears answered.
        const existing = await getTask(taskId)
        if (existing) {
          const now = new Date().toISOString()
          await putTask({
            ...existing,
            status: 'RESPONSE_RECEIVED',
            response: { ...request, respondedAt: now },
            responseReceivedAt: now,
            updatedAt: now,
          })
        }
      }
      await loadTasks()
      return { disposition: outcome.disposition, taskId }
    },
    [api, loadTasks],
  )

  // Initial load + polling with backoff + reconnect flush (offline-pwa Open Q #2).
  // The first tick always runs — online or offline — to serve cached data; after
  // that the loop only polls while online, and the 'online' event restarts it.
  useEffect(() => {
    const policy = createBackoffPolicy()
    let timer: ReturnType<typeof setTimeout> | undefined
    let disposed = false
    let didInitialLoad = false

    const tick = async () => {
      if (didInitialLoad && !isOnline()) {
        // Offline after the initial load: do not hammer the network; wait for
        // the 'online' event.
        return
      }
      didInitialLoad = true
      try {
        await refresh()
        policy.reset()
      } catch {
        policy.backoff()
      }
      if (!disposed) timer = setTimeout(tick, policy.delay())
    }

    const kick = () => {
      if (timer) clearTimeout(timer)
      timer = setTimeout(tick, 0)
    }

    // subscribeConnectivity invokes the listener immediately with the current
    // state, so an already-online device starts polling right away, and an
    // offline device starts as soon as connectivity returns.
    const unsubscribe = subscribeConnectivity((isNowOnline) => {
      if (isNowOnline) kick()
    })

    // Initial load (works offline) + starts polling when online.
    kick()

    return () => {
      disposed = true
      if (timer) clearTimeout(timer)
      unsubscribe()
    }
  }, [refresh])

  const value = useMemo<AppStoreValue>(
    () => ({
      online,
      loading,
      error,
      conversations,
      threads,
      tasks,
      latestDeliveryByConversation,
      refresh,
      openConversation,
      sendMessage,
      createConversation,
      loadTasks,
      answerTask,
    }),
    [
      online,
      loading,
      error,
      conversations,
      threads,
      tasks,
      latestDeliveryByConversation,
      refresh,
      openConversation,
      sendMessage,
      createConversation,
      loadTasks,
      answerTask,
    ],
  )

  return <AppStoreContext.Provider value={value}>{children}</AppStoreContext.Provider>
}
