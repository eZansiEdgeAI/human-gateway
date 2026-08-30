/**
 * Cache-aware reads for the Inbox, Thread, and Task views (PWA-FR-01, PWA-FR-06).
 *
 * Each read tries the Edge first when the device is online, mirrors the result
 * into IndexedDB, and falls back to the cached copy on any network failure —
 * so the Inbox/Thread/Task list render with no network. The write path
 * (messages, conversations, task answers) already goes through the offline-first
 * outbox; this module only handles reads.
 *
 * `api` is injectable for tests; production uses the `edgeApi` singleton.
 */

import type { ConversationView, HumanTask, MessageView } from '../types/protocol'
import { edgeApi, type EdgeApiClient } from '../api'
import { isOnline } from '../lib/connectivity'
import { listConversations as listConversationsCached, putConversation } from '../db/conversations'
import {
  listMessagesForConversation,
  putMessage,
} from '../db/messages'
import { listTasks as listTasksCached, putTask } from '../db/tasks'

/**
 * Lists conversations, refreshing from the Edge when online and otherwise
 * serving the cached copy.
 */
export async function loadConversations(
  api: EdgeApiClient = edgeApi,
): Promise<ConversationView[]> {
  if (isOnline()) {
    try {
      const remote = await api.listConversations()
      await Promise.all(remote.map((conversation) => putConversation(conversation)))
      return remote
    } catch {
      // Network/HTTP failure → serve the cached copy below.
    }
  }
  return listConversationsCached()
}

/**
 * Lists a conversation's messages, refreshing from the Edge when online and
 * otherwise serving the cached copy.
 */
export async function loadThread(
  conversationId: string,
  api: EdgeApiClient = edgeApi,
): Promise<MessageView[]> {
  if (isOnline()) {
    try {
      const remote = await api.listConversationMessages(conversationId)
      await Promise.all(remote.map((view) => putMessage(view)))
      return remote
    } catch {
      // Network/HTTP failure → serve the cached copy below.
    }
  }
  return listMessagesForConversation(conversationId)
}

/**
 * Lists human tasks, refreshing from the Edge when online and otherwise serving
 * the cached copy (PWA-FR-06). Task answering is offline-capable, so the cached
 * list — including any task answered offline — must be visible with no network.
 */
export async function loadTasks(api: EdgeApiClient = edgeApi): Promise<HumanTask[]> {
  if (isOnline()) {
    try {
      const remote = await api.listTasks()
      await Promise.all(remote.map((task) => putTask(task)))
      return remote
    } catch {
      // Network/HTTP failure → serve the cached copy below.
    }
  }
  return listTasksCached()
}
