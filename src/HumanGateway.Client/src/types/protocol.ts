/**
 * HumanGateway protocol types (TypeScript mirror of `schemas/*.schema.json`).
 *
 * These types are the wire contract shared by the PWA, the Edge, and the
 * Relay. They mirror the JSON Schema documents one-to-one — field names are
 * camelCase, enums are string-literal unions matching the exact wire tokens,
 * and optional fields are marked optional. The source of truth is
 * `schemas/` (protocol-engineer); keep this file in sync with it.
 *
 * Because `tsconfig.app.json` enables `erasableSyntaxOnly`, enums are declared
 * as string-literal unions (no TS `enum`), which also matches the protocol's
 * "transport- and language-independent JSON" guarantee (PROTO-FR-06).
 *
 * @see schemas/message.schema.json
 * @see schemas/participant.schema.json
 * @see schemas/delivery.schema.json
 * @see schemas/humantask.schema.json
 * @see schemas/artifact.schema.json
 * @see schemas/error.schema.json
 */

// ---------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------

/** Participant kind; must agree with the typed address prefix (PROTO-FR-02). */
export type ParticipantKind = 'human' | 'agent' | 'system'

/**
 * A typed participant: a `human:`, `agent:`, or `system:` address plus display
 * metadata (participant.schema.json, PROTO-FR-02). The only identity carried
 * in message envelopes.
 */
export interface Participant {
  /** Typed address, e.g. `human:teacher@school.example`. Prefix matches `kind`. */
  address: string
  kind?: ParticipantKind
  /** Human-readable display name, cached from the last known metadata. */
  displayName: string
  /** Optional local user identifier for human participants. */
  userId?: string
  /** Optional Edge Gateway identity for system participants. */
  gatewayId?: string
}

// ---------------------------------------------------------------------------
// Errors
// ---------------------------------------------------------------------------

/**
 * The protocol error model (error.schema.json): a stable machine-readable
 * `code`, a human-readable `message`, optional structured `details`, and a
 * retryability hint.
 */
export interface ProtocolError {
  code: string
  message: string
  details?: Record<string, unknown>
  retryable?: boolean
}

// ---------------------------------------------------------------------------
// Artifacts
// ---------------------------------------------------------------------------

/**
 * A first-class content object referenced by messages by ID + hash — never
 * embedded in the envelope (PROTO-FR-04).
 */
export interface Artifact {
  id: string
  hash: string
  sizeBytes: number
  mimeType: string
  filename: string
  description?: string
  createdAt: string
}

/**
 * A reference to an artifact carried inside a message envelope or task
 * response: ID + content hash plus rendering metadata. Never the bytes.
 */
export interface ArtifactReference {
  id: string
  hash: string
  filename?: string
  mimeType?: string
  sizeBytes?: number
}

// ---------------------------------------------------------------------------
// Messages
// ---------------------------------------------------------------------------

/** Message body rendering format. */
export type MessageFormat = 'plaintext' | 'markdown'

/** Message payload: body text, optional rendering format, optional data. */
export interface MessagePayload {
  body: string
  format?: MessageFormat
  data?: Record<string, unknown>
}

/**
 * A durable message envelope (message.schema.json, PROTO-FR-03). Carries ID,
 * sender, recipients, conversation, workflow/task references, payload, and
 * artifact references.
 */
export interface Message {
  id: string
  sender: Participant
  recipients: Participant[]
  conversationId: string
  replyToMessageId?: string
  workflowRef?: string
  humanTaskId?: string
  payload: MessagePayload
  artifactRefs?: ArtifactReference[]
  correlationTokens?: Record<string, string>
  createdAt: string
  updatedAt?: string
  contentHash: string
}

// ---------------------------------------------------------------------------
// Delivery
// ---------------------------------------------------------------------------

/**
 * Delivery lifecycle state (PROTO-FR-05). `WAITING_FOR_SYNC` is a valid state
 * — offline-queued delivery is expected behaviour, never an error.
 */
export type DeliveryState =
  | 'QUEUED'
  | 'SYNCING'
  | 'DELIVERED'
  | 'ACKNOWLEDGED'
  | 'WAITING_FOR_SYNC'
  | 'FAILED'

/** Per-recipient delivery lifecycle record for a message. */
export interface Delivery {
  id: string
  messageId: string
  recipient: Participant
  state: DeliveryState
  attempts: number
  maxAttempts: number
  nextRetryAt?: string
  queuedAt?: string
  syncingAt?: string
  waitingForSyncAt?: string
  deliveredAt?: string
  acknowledgedAt?: string
  failedAt?: string
  error?: ProtocolError
  createdAt: string
  updatedAt: string
}

// ---------------------------------------------------------------------------
// Human tasks
// ---------------------------------------------------------------------------

/** Human task kind: human-input (free text / choice) or human-approval. */
export type HumanTaskKind = 'input' | 'approval'

/** Human task lifecycle state (product vision §10). */
export type HumanTaskStatus =
  | 'REQUESTED'
  | 'DELIVERED_TO_HUMAN'
  | 'RESPONSE_RECEIVED'
  | 'COMPLETED'
  | 'EXPIRED'

/** Approval decision (kind=approval). */
export type ApprovalDecision = 'approved' | 'rejected'

/** The human's response to a task; populated once answered. */
export interface TaskResponse {
  text?: string
  decision?: ApprovalDecision
  reason?: string
  artifactRefs?: ArtifactReference[]
  respondedBy?: Participant
  respondedAt?: string
}

/**
 * The workflow primitive transported by HumanGateway: a request for human
 * input or approval (humantask.schema.json). HumanGateway transports the task;
 * the consumer owns task semantics and authorisation.
 */
export interface HumanTask {
  id: string
  kind?: HumanTaskKind
  status?: HumanTaskStatus
  workflowRef: string
  nodeId: string
  role?: string
  prompt: string
  subject?: string
  options?: string[]
  requestMessageId: string
  responseMessageId?: string
  response?: TaskResponse
  correlationToken?: string
  expiresAt?: string
  requestedAt?: string
  deliveredToHumanAt?: string
  responseReceivedAt?: string
  completedAt?: string
  expiredAt?: string
  createdAt: string
  updatedAt?: string
}

// ---------------------------------------------------------------------------
// Edge local REST API views (local-edge §4 / EDGE-FR-03)
// ---------------------------------------------------------------------------

/** A conversation plus its membership and derived activity metadata. */
export interface ConversationView {
  id: string
  title?: string
  participants: Participant[]
  messageCount: number
  lastMessageAt?: string
  createdAt: string
}

/** A message envelope plus its per-recipient delivery records (PWA-FR-05). */
export interface MessageView {
  message: Message
  deliveries: Delivery[]
}

/**
 * Sync-status snapshot the Edge serves at `GET /sync/status` (EDGE-FR-05,
 * PWA-FR-05). Consumed by the PWA sync banner so teachers see how many
 * messages are still queued and how deliveries are trending — without the
 * banner ever needing to read the whole store.
 */
export interface SyncStatusView {
  gatewayId: string
  queued: number
  lastSequence: number
  deliveries: DeliverySummary
}

/**
 * Counts of delivery records by lifecycle state. The UI renders these as icon
 * + text, never colour alone (ACC-03).
 */
export interface DeliverySummary {
  queued: number
  syncing: number
  delivered: number
  acknowledged: number
  waitingForSync: number
  failed: number
}

// ---------------------------------------------------------------------------
// Edge local REST API request shapes (used by the offline-first client + outbox)
// ---------------------------------------------------------------------------

export interface CreateConversationRequest {
  title?: string
  participants: Participant[]
}

export interface SendMessageRequest {
  sender: Participant
  recipients: Participant[]
  conversationId: string
  replyToMessageId?: string
  workflowRef?: string
  humanTaskId?: string
  payload: MessagePayload
  artifactRefs?: ArtifactReference[]
  correlationTokens?: Record<string, string>
}

export interface CreateTaskRequest {
  kind?: HumanTaskKind
  workflowRef: string
  nodeId: string
  role?: string
  prompt: string
  subject?: string
  options?: string[]
  correlationToken?: string
  expiresAt?: string
  /** The system/agent participant that requested the task. */
  requester: Participant
  /** The human participants the task is delivered to. */
  assignees: Participant[]
  conversationId?: string
}

export interface AnswerTaskRequest {
  respondedBy: Participant
  text?: string
  decision?: ApprovalDecision
  reason?: string
  artifactRefs?: ArtifactReference[]
}

export interface RegisterArtifactRequest {
  id?: string
  hash: string
  sizeBytes: number
  mimeType: string
  filename: string
  description?: string
}
