/**
 * Replays an outbox operation against the Edge local REST API.
 *
 * This is the single translation point between the durable outbox operation
 * (`OutboxOperation`, in `db/outbox.ts`) and the Edge's HTTP endpoints
 * (`HumanGateway.Edge/Endpoints/LocalApiEndpoints.cs`). The flush worker calls
 * {@link dispatchOperation} once per entry; tests inject a fake dispatcher to
 * exercise the flush lifecycle without a real HTTP server.
 */

import type {
  Artifact,
  ConversationView,
  HumanTask,
  MessageView,
} from '../types/protocol'
import type { OutboxOperation } from '../db/outbox'
import { resolveApiUrl } from './config'
import { httpRequest } from './http'

/** The server-issued entity each operation type returns on success. */
export type OperationResult =
  | { type: 'sendMessage'; result: MessageView }
  | { type: 'answerTask'; result: HumanTask }
  | { type: 'createConversation'; result: ConversationView }
  | { type: 'createTask'; result: HumanTask }
  | { type: 'registerArtifact'; result: Artifact }

export type DispatchOperation = (operation: OutboxOperation) => Promise<OperationResult>

/**
 * Replays a single outbox operation against the Edge, returning the
 * server-issued entity. Throws {@link HttpError} or {@link NetworkError} on
 * failure (mapped to a {@link ProtocolError} by the flush worker).
 */
export function dispatchOperation(operation: OutboxOperation): Promise<OperationResult> {
  switch (operation.type) {
    case 'sendMessage':
      return httpRequest<MessageView>({
        url: resolveApiUrl('/messages'),
        method: 'POST',
        body: operation.request,
      }).then((result) => ({ type: 'sendMessage' as const, result }))

    case 'answerTask':
      return httpRequest<HumanTask>({
        url: resolveApiUrl(`/tasks/${encodeURIComponent(operation.taskId)}/response`),
        method: 'POST',
        body: operation.request,
      }).then((result) => ({ type: 'answerTask' as const, result }))

    case 'createConversation':
      return httpRequest<ConversationView>({
        url: resolveApiUrl('/conversations'),
        method: 'POST',
        body: operation.request,
      }).then((result) => ({ type: 'createConversation' as const, result }))

    case 'createTask':
      return httpRequest<HumanTask>({
        url: resolveApiUrl('/tasks'),
        method: 'POST',
        body: operation.request,
      }).then((result) => ({ type: 'createTask' as const, result }))

    case 'registerArtifact':
      return httpRequest<Artifact>({
        url: resolveApiUrl('/artifacts'),
        method: 'POST',
        body: operation.request,
      }).then((result) => ({ type: 'registerArtifact' as const, result }))
  }
}
