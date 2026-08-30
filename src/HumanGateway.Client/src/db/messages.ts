/**
 * Message repository (PWA-FR-04, PWA-FR-05).
 *
 * Stores `MessageView` records — an envelope plus its per-recipient delivery
 * records — keyed by the envelope's message id and indexed by conversation id
 * so a thread renders with no network. Delivery status for each message comes
 * straight from the embedded `Delivery.state` values, which the UI renders as
 * icon + text (never colour alone, ACC-03).
 */

import type { MessageView } from '../types/protocol'
import { deleteValue, getAllByIndex, getAllValues, getValue, putValue } from './database'
import { INDEXES, STORES } from './schema'

/** Upserts a message view (envelope + deliveries). */
export function putMessage(view: MessageView): Promise<IDBValidKey> {
  return putValue(STORES.messages, view)
}

/** Gets a message by id, or `undefined` when absent. */
export function getMessage(id: string): Promise<MessageView | undefined> {
  return getValue<MessageView>(STORES.messages, id)
}

/** Lists a conversation's messages in chronological order (oldest first). */
export async function listMessagesForConversation(
  conversationId: string,
): Promise<MessageView[]> {
  const messages = await getAllByIndex<MessageView>(
    STORES.messages,
    INDEXES.messagesByConversation,
    conversationId,
  )
  return messages.sort((a, b) => a.message.createdAt.localeCompare(b.message.createdAt))
}

/** Lists every locally-stored message, oldest first. */
export async function listAllMessages(): Promise<MessageView[]> {
  const messages = await getAllValues<MessageView>(STORES.messages)
  return messages.sort((a, b) => a.message.createdAt.localeCompare(b.message.createdAt))
}

/** Deletes a message (and its embedded delivery records). */
export function deleteMessage(id: string): Promise<undefined> {
  return deleteValue(STORES.messages, id)
}
