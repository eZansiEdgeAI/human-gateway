import type {
  ArtifactReference,
  HumanInteractionEvent,
  HumanInteractionRequest,
  HumanInteractionResponse,
} from './types.js'

/**
 * The transport-neutral mapping between HumanGateway/FlowForge activity and
 * the five lifecycle concepts exposed by the provider boundary.
 *
 * These functions are intentionally pure. Observers can therefore persist,
 * compare, or replay the resulting events without giving this adapter any
 * ownership of workflow state, authorisation, or audit decisions.
 */
export const translateHumanInteractionRequested = (
  request: HumanInteractionRequest,
): HumanInteractionEvent => ({ type: 'HumanInteractionRequested', request })

export const translateHumanResponseReceived = (
  request: HumanInteractionRequest,
  response: HumanInteractionResponse,
): HumanInteractionEvent => ({ type: 'HumanResponseReceived', request, response })

export const translateHumanInteractionCompleted = (
  request: HumanInteractionRequest,
  response: HumanInteractionResponse,
): HumanInteractionEvent => ({ type: 'HumanInteractionCompleted', request, response })

export const translateArtifactReceived = (
  request: HumanInteractionRequest,
  artifact: ArtifactReference,
): HumanInteractionEvent => ({ type: 'ArtifactReceived', request, artifact })

export const translateHumanInteractionExpired = (
  request: HumanInteractionRequest,
  expiredAt?: string,
): HumanInteractionEvent => ({ type: 'HumanInteractionExpired', request, expiredAt })
