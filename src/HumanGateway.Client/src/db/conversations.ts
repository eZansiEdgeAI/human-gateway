/**
 * Conversation repository (offline-pwa §4 Inbox/Outbox view).
 *
 * Stores the Edge's `ConversationView` records locally so the conversation
 * list renders with no network. Conversations are keyed by durable id; the
 * repository sorts by `createdAt` descending (newest first) to match the Edge.
 */

import type { ConversationView } from '../types/protocol'
import { deleteValue, getAllValues, getValue, putValue } from './database'
import { STORES } from './schema'

/** Upserts a conversation view. */
export function putConversation(conversation: ConversationView): Promise<IDBValidKey> {
  return putValue(STORES.conversations, conversation)
}

/** Gets a conversation by id, or `undefined` when absent. */
export function getConversation(id: string): Promise<ConversationView | undefined> {
  return getValue<ConversationView>(STORES.conversations, id)
}

/** Lists every conversation, newest first. */
export async function listConversations(): Promise<ConversationView[]> {
  const conversations = await getAllValues<ConversationView>(STORES.conversations)
  return conversations.sort((a, b) => b.createdAt.localeCompare(a.createdAt))
}

/** Deletes a conversation (and, callers should note, not its messages). */
export function deleteConversation(id: string): Promise<undefined> {
  return deleteValue(STORES.conversations, id)
}
