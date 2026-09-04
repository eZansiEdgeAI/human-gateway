/**
 * The published FlowForge integration surface used by this consumer adapter.
 *
 * These types deliberately describe an interaction, not workflow state. The
 * workflow runner remains responsible for validation, authorisation, retries,
 * and deciding how the workflow continues after the provider resolves.
 */

/** The two human-node kinds supported by the HumanGateway transport. */
export type HumanInteractionKind = 'input' | 'approval'

/** A durable artifact reference; artifact bytes never travel in an envelope. */
export interface ArtifactReference {
  id: string
  hash: string
  filename?: string
  mimeType?: string
  sizeBytes?: number
}

/**
 * FlowForge's pending human task shape. Keeping the correlation fields
 * explicit prevents an adapter from accidentally losing node identity or
 * workflow context during a transport round-trip.
 */
export interface PendingHumanTask {
  id: string
  workflowRef: string
  nodeId: string
  kind: HumanInteractionKind
  role?: string
  prompt: string
  subject?: string
  options?: string[]
  correlationToken?: string
  expiresAt?: string
}

/** A request presented to a provider by the workflow runner. */
export interface HumanInteractionRequest {
  task: PendingHumanTask
  requestedAt?: string
}

/** The response produced by a human. Input tasks use text; approvals use decision. */
export interface HumanInteractionResponse {
  text?: string
  decision?: 'approved' | 'rejected'
  reason?: string
  artifacts?: ArtifactReference[]
  respondedAt?: string
  respondedBy?: string
}

/** Stable provider lifecycle concepts exposed to workflow integrations. */
export type HumanInteractionEvent =
  | { type: 'HumanInteractionRequested'; request: HumanInteractionRequest }
  | { type: 'HumanResponseReceived'; request: HumanInteractionRequest; response: HumanInteractionResponse }
  | { type: 'HumanInteractionCompleted'; request: HumanInteractionRequest; response: HumanInteractionResponse }
  | { type: 'ArtifactReceived'; request: HumanInteractionRequest; artifact: ArtifactReference }
  | { type: 'HumanInteractionExpired'; request: HumanInteractionRequest; expiredAt?: string }

/** Optional observer for lifecycle events. Observers must not mutate workflow state. */
export type HumanInteractionEventSink = (event: HumanInteractionEvent) => void | Promise<void>

/** Provider result passed back to FlowForge so it can resume its own runner. */
export interface HumanInteractionResult {
  taskId: string
  response: HumanInteractionResponse
}
