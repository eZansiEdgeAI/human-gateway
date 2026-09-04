import type {
  HumanInteractionEventSink,
  HumanInteractionRequest,
  HumanInteractionResult,
} from './types.js'

/** Options shared by provider implementations. */
export interface HumanInteractionProviderOptions {
  /** Receives transport-independent lifecycle notifications. */
  onEvent?: HumanInteractionEventSink
  /** Allows a runner to cancel a pending interaction. */
  signal?: AbortSignal
}

/**
 * Pluggable FlowForge human-interaction boundary.
 *
 * Implementations choose how a human is reached (console, HumanGateway, or a
 * future channel). They must return the human response and preserve the task
 * correlation supplied in the request. They do not own workflow execution,
 * workflow state, actor authorisation, or audit decisions.
 */
export interface HumanInteractionProvider {
  requestInteraction(
    request: HumanInteractionRequest,
    options?: HumanInteractionProviderOptions,
  ): Promise<HumanInteractionResult>
}
