import type { HumanInteractionProvider, HumanInteractionProviderOptions } from './provider.js'
import { HumanInteractionExpiredError } from './console.js'
import type {
  ArtifactReference,
  HumanInteractionRequest,
  HumanInteractionResponse,
  HumanInteractionResult,
} from './types.js'
import {
  translateArtifactReceived,
  translateHumanInteractionCompleted,
  translateHumanInteractionExpired,
  translateHumanInteractionRequested,
  translateHumanResponseReceived,
} from './translation.js'

/** The transport-independent request sent to a HumanGateway client/adapter. */
export interface HumanGatewayTaskMessage {
  type: 'human-task'
  task: {
    id: string
    kind: 'input' | 'approval'
    workflowRef: string
    nodeId: string
    role?: string
    prompt: string
    subject?: string
    options?: string[]
    correlationToken?: string
    expiresAt?: string
  }
  requestedAt: string
}

/** Result returned by the HumanGateway transport after the task is answered. */
export interface HumanGatewayTaskResponse {
  status: 'completed' | 'expired'
  /**
   * The task snapshot returned by the gateway envelope.  Gateways should echo
   * the consumer-owned correlation fields; the provider verifies them and
   * never lets a response resume a different FlowForge node.
   */
  task?: HumanGatewayTaskMessage['task']
  response?: HumanInteractionResponse
  artifacts?: ArtifactReference[]
  expiredAt?: string
}

/**
 * Small consumer-side seam around the Edge/Relay API. The provider deliberately
 * knows nothing about HTTP, persistence, sync, authentication, or workflow
 * execution; those concerns belong to the supplied transport and FlowForge.
 */
export interface HumanGatewayTransport {
  sendHumanTask(
    message: HumanGatewayTaskMessage,
    options?: { signal?: AbortSignal },
  ): Promise<HumanGatewayTaskResponse>
}

export interface HumanGatewayInteractionProviderOptions extends HumanInteractionProviderOptions {
  transport: HumanGatewayTransport
  now?: () => Date
}

/** Adapter which carries a FlowForge pending task through HumanGateway. */
export class HumanGatewayInteractionProvider implements HumanInteractionProvider {
  private readonly options: HumanGatewayInteractionProviderOptions

  constructor(options: HumanGatewayInteractionProviderOptions) {
    this.options = options
  }

  async requestInteraction(
    request: HumanInteractionRequest,
    options: HumanInteractionProviderOptions = {},
  ): Promise<HumanInteractionResult> {
    const eventSink = options.onEvent ?? this.options.onEvent
    const signal = options.signal ?? this.options.signal
    const now = this.options.now ?? (() => new Date())
    this.throwIfAborted(signal)

    await this.emit(eventSink, translateHumanInteractionRequested(request))
    const task = request.task
    const message: HumanGatewayTaskMessage = {
      type: 'human-task',
      task: {
        id: task.id,
        kind: task.kind,
        workflowRef: task.workflowRef,
        nodeId: task.nodeId,
        role: task.role,
        prompt: task.prompt,
        subject: task.subject,
        options: task.options,
        correlationToken: task.correlationToken,
        expiresAt: task.expiresAt,
      },
      requestedAt: request.requestedAt ?? now().toISOString(),
    }

    const gatewayResult = await this.options.transport.sendHumanTask(message, { signal })
    this.throwIfAborted(signal)
    this.verifyCorrelation(task, gatewayResult.task)
    if (gatewayResult.status === 'expired') {
      const expiredAt = gatewayResult.expiredAt ?? now().toISOString()
      await this.emit(eventSink, translateHumanInteractionExpired(request, expiredAt))
      throw new HumanInteractionExpiredError(task.id, expiredAt)
    }
    if (!gatewayResult.response) throw new Error(`HumanGateway returned no response for task ${task.id}`)

    const response = this.mergeArtifacts(gatewayResult.response, gatewayResult.artifacts)
    await this.emit(eventSink, translateHumanResponseReceived(request, response))
    for (const artifact of response.artifacts ?? []) {
      await this.emit(eventSink, translateArtifactReceived(request, artifact))
    }
    await this.emit(eventSink, translateHumanInteractionCompleted(request, response))
    return { taskId: task.id, response }
  }

  /** Verify the complete PendingHumanTask identity when the response echoes it. */
  private verifyCorrelation(
    expected: HumanGatewayTaskMessage['task'],
    actual?: HumanGatewayTaskMessage['task'],
  ): void {
    if (!actual) return
    const fields: Array<keyof HumanGatewayTaskMessage['task']> = [
      'id', 'kind', 'workflowRef', 'nodeId', 'role', 'prompt', 'subject',
      'correlationToken', 'expiresAt',
    ]
    for (const field of fields) {
      if (actual[field] !== expected[field]) {
        throw new Error(`HumanGateway response correlation mismatch for ${String(field)} on task ${expected.id}`)
      }
    }
    if (JSON.stringify(actual.options) !== JSON.stringify(expected.options)) {
      throw new Error(`HumanGateway response correlation mismatch for options on task ${expected.id}`)
    }
  }

  private mergeArtifacts(response: HumanInteractionResponse, artifacts?: ArtifactReference[]): HumanInteractionResponse {
    if (!artifacts?.length) return response
    const existing = response.artifacts ?? []
    const seen = new Set(existing.map(artifact => artifact.id))
    return { ...response, artifacts: [...existing, ...artifacts.filter(artifact => !seen.has(artifact.id))] }
  }

  private async emit(
    sink: HumanInteractionProviderOptions['onEvent'],
    event: Parameters<NonNullable<HumanInteractionProviderOptions['onEvent']>>[0],
  ): Promise<void> {
    await sink?.(event)
  }

  private throwIfAborted(signal?: AbortSignal): void {
    if (signal?.aborted) throw signal.reason ?? new DOMException('The interaction was aborted', 'AbortError')
  }
}
