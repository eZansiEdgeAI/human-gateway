/**
 * Typed Edge API client (EDGE-FR-03, offline-pwa §4).
 *
 * The facade every view talks to. Reads hit the Edge HTTP API directly (the
 * data layer above reads from IndexedDB when offline). Writes go through the
 * offline-first wrapper (`offlineFirst.ts`), so a message composed offline is
 * durably queued and flushed when reachable instead of failing (PWA-FR-02).
 *
 * A default singleton (`edgeApi`) is exported for simple consumers; construct
 * an isolated client with `createEdgeApiClient` when tests or a different Edge
 * origin need their own dependencies.
 */

import type {
  Artifact,
  ConversationView,
  HumanTask,
  HumanTaskStatus,
  MessageView,
  SendMessageRequest,
  CreateConversationRequest,
  CreateTaskRequest,
  AnswerTaskRequest,
  RegisterArtifactRequest,
  SyncStatusView,
} from '../types/protocol'
import { resolveApiUrl } from './config'
import { HttpError, httpRequest } from './http'
import { enqueueWrite, type WriteOutcome, type WriteOptions } from './offlineFirst'
import type { FlushDeps } from './flush'
import type { OperationResult } from './dispatcher'

/** Optional configuration for constructing an {@link EdgeApiClient}. */
export type EdgeApiClientOptions = FlushDeps

/** The PWA's view of the Edge local REST API. */
export interface EdgeApiClient {
  // ---- Reads (HTTP; offline reads are served from IndexedDB by the data layer) ----
  listConversations(): Promise<ConversationView[]>
  getConversation(id: string): Promise<ConversationView | null>
  listConversationMessages(id: string): Promise<MessageView[]>
  getMessage(id: string): Promise<MessageView | null>
  listTasks(status?: HumanTaskStatus): Promise<HumanTask[]>
  getTask(id: string): Promise<HumanTask | null>
  listArtifacts(): Promise<Artifact[]>
  getArtifact(id: string): Promise<Artifact | null>
  getSyncStatus(): Promise<SyncStatusView>

  // ---- Writes (offline-first: queued when offline, flushed when reachable) ----
  sendMessage(
    request: SendMessageRequest,
    options?: WriteOptions,
  ): Promise<WriteOutcome<MessageView>>
  answerTask(
    taskId: string,
    request: AnswerTaskRequest,
    options?: WriteOptions,
  ): Promise<WriteOutcome<HumanTask>>
  createConversation(
    request: CreateConversationRequest,
    options?: WriteOptions,
  ): Promise<WriteOutcome<ConversationView>>
  createTask(
    request: CreateTaskRequest,
    options?: WriteOptions,
  ): Promise<WriteOutcome<HumanTask>>
  registerArtifact(
    request: RegisterArtifactRequest,
    options?: WriteOptions,
  ): Promise<WriteOutcome<Artifact>>
}

/** Builds an Edge API client; `options` inject dependencies for tests. */
export function createEdgeApiClient(options: EdgeApiClientOptions = {}): EdgeApiClient {
  const deps: FlushDeps = options

  return {
    listConversations: () => getJson('/conversations'),

    getConversation: (id) => getOrNull('/conversations', id),

    listConversationMessages: (id) =>
      getJson(`/conversations/${encodeURIComponent(id)}/messages`),

    getMessage: (id) => getOrNull('/messages', id),

    listTasks: (status) => {
      const query = status ? `?status=${encodeURIComponent(status)}` : ''
      return getJson(`/tasks${query}`)
    },

    getTask: (id) => getOrNull('/tasks', id),

    listArtifacts: () => getJson('/artifacts'),

    getArtifact: (id) => getOrNull('/artifacts', id),

    getSyncStatus: () => getJson('/sync/status'),

    sendMessage: (request, writeOptions = {}) =>
      enqueueWrite<MessageView>(
        { type: 'sendMessage', request },
        { ...deps, ...writeOptions, pick: (result) => pickResult(result, 'sendMessage') },
      ),

    answerTask: (taskId, request, writeOptions = {}) =>
      enqueueWrite<HumanTask>(
        { type: 'answerTask', taskId, request },
        { ...deps, ...writeOptions, pick: (result) => pickResult(result, 'answerTask') },
      ),

    createConversation: (request, writeOptions = {}) =>
      enqueueWrite<ConversationView>(
        { type: 'createConversation', request },
        { ...deps, ...writeOptions, pick: (result) => pickResult(result, 'createConversation') },
      ),

    createTask: (request, writeOptions = {}) =>
      enqueueWrite<HumanTask>(
        { type: 'createTask', request },
        { ...deps, ...writeOptions, pick: (result) => pickResult(result, 'createTask') },
      ),

    registerArtifact: (request, writeOptions = {}) =>
      enqueueWrite<Artifact>(
        { type: 'registerArtifact', request },
        { ...deps, ...writeOptions, pick: (result) => pickResult(result, 'registerArtifact') },
      ),
  }
}

/** The default client singleton used by the app. */
export const edgeApi: EdgeApiClient = createEdgeApiClient()

function getJson<T>(path: string): Promise<T> {
  return httpRequest<T>({ url: resolveApiUrl(path), method: 'GET' })
}

/** GETs a single entity by id, mapping a 404 to `null`. */
async function getOrNull<T>(pathPrefix: string, id: string): Promise<T | null> {
  try {
    return await getJson<T>(`${pathPrefix}/${encodeURIComponent(id)}`)
  } catch (error) {
    if (error instanceof HttpError && error.status === 404) return null
    throw error
  }
}

/**
 * Narrows the dispatcher's {@link OperationResult} union to the concrete
 * entity type the write caller expects. Overloads (rather than a generic
 * `Extract`) give each call site a precise return type: a generic
 * `Extract<OperationResult, { type: T }>` defers when `T` is unbound, so
 * TypeScript folds it into an intersection of every `result` type instead of
 * the one matching `expected`.
 */
function pickResult(result: OperationResult, expected: 'sendMessage'): MessageView
function pickResult(result: OperationResult, expected: 'answerTask'): HumanTask
function pickResult(result: OperationResult, expected: 'createConversation'): ConversationView
function pickResult(result: OperationResult, expected: 'createTask'): HumanTask
function pickResult(result: OperationResult, expected: 'registerArtifact'): Artifact
function pickResult(
  result: OperationResult,
  expected: OperationResult['type'],
): MessageView | HumanTask | ConversationView | Artifact {
  if (result.type !== expected) {
    throw new Error(`Unexpected dispatch result type: expected ${expected}, got ${result.type}`)
  }
  return result.result
}
