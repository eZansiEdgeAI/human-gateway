/**
 * Optimistic draft construction (PWA-FR-02).
 *
 * A message composed offline is shown in the thread immediately, under a local
 * id, before the Edge accepts it. The offline-first flush worker reconciles the
 * draft away once the Edge issues its canonical id (`reconcileSuccess` in
 * `api/flush.ts` deletes the `localEntityId`). Until then the draft carries a
 * placeholder content hash and QUEUED deliveries so the delivery-status UI has
 * something honest to render.
 */

import type { Delivery, Message, MessageView, SendMessageRequest } from '../types/protocol'
import { newId } from './id'

/** Placeholder hash (matches the `sha256:` schema pattern) for a pre-server draft. */
export const PLACEHOLDER_CONTENT_HASH = 'sha256:' + '0'.repeat(64)

/**
 * Builds a local `MessageView` for an unsent message, keyed by `localId`. The
 * flush worker deletes this draft (by `localId`) and stores the server-issued
 * record once the Edge accepts the message.
 */
export function makeOptimisticMessageView(
  request: SendMessageRequest,
  localId: string,
): MessageView {
  const now = new Date().toISOString()

  const message: Message = {
    id: localId,
    sender: request.sender,
    recipients: request.recipients,
    conversationId: request.conversationId,
    replyToMessageId: request.replyToMessageId,
    workflowRef: request.workflowRef,
    humanTaskId: request.humanTaskId,
    payload: request.payload,
    artifactRefs: request.artifactRefs,
    correlationTokens: request.correlationTokens,
    createdAt: now,
    contentHash: PLACEHOLDER_CONTENT_HASH,
  }

  const deliveries: Delivery[] = request.recipients.map((recipient) => ({
    id: newId(),
    messageId: localId,
    recipient,
    state: 'QUEUED',
    attempts: 0,
    maxAttempts: 5,
    queuedAt: now,
    createdAt: now,
    updatedAt: now,
  }))

  return { message, deliveries }
}
