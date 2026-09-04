import type { HumanInteractionProvider, HumanInteractionProviderOptions } from './provider.js'
import type {
  HumanInteractionRequest,
  HumanInteractionResponse,
  HumanInteractionResult,
} from './types.js'

/** Values accepted by the headless console provider. */
export type ConsoleAnswer = string | boolean | HumanInteractionResponse

export interface ConsoleHumanInteractionProviderOptions extends HumanInteractionProviderOptions {
  /** Answers keyed by task id (or, as a convenience, node id). */
  answers?: Readonly<Record<string, ConsoleAnswer>>
  /** Supplies an answer when it is not present in `answers`. */
  answer?: (request: HumanInteractionRequest) => ConsoleAnswer | Promise<ConsoleAnswer>
  /** Clock used for deterministic expiry tests. */
  now?: () => Date
}

/** Error returned to a workflow runner when a console interaction expires. */
export class HumanInteractionExpiredError extends Error {
  readonly taskId: string
  readonly expiredAt: string

  constructor(taskId: string, expiredAt: string) {
    super(`Human interaction ${taskId} expired at ${expiredAt}`)
    this.name = 'HumanInteractionExpiredError'
    this.taskId = taskId
    this.expiredAt = expiredAt
  }
}

/**
 * Synchronous/headless provider used as the FlowForge comparison baseline.
 *
 * It deliberately does not read stdin or own workflow state. Callers provide
 * answers explicitly, making the same interaction script reproducible in CI
 * and suitable for comparison with a transport-backed provider.
 */
export class ConsoleHumanInteractionProvider implements HumanInteractionProvider {
  private readonly options: ConsoleHumanInteractionProviderOptions

  constructor(options: ConsoleHumanInteractionProviderOptions = {}) {
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
    await this.emit(eventSink, { type: 'HumanInteractionRequested', request })
    this.throwIfAborted(signal)
    await this.ensureNotExpired(request, now, eventSink)

    const answer = await this.resolveAnswer(request)
    this.throwIfAborted(signal)
    await this.ensureNotExpired(request, now, eventSink)
    const response = this.toResponse(request, answer, now)

    await this.emit(eventSink, { type: 'HumanResponseReceived', request, response })
    for (const artifact of response.artifacts ?? []) {
      await this.emit(eventSink, { type: 'ArtifactReceived', request, artifact })
    }
    await this.emit(eventSink, { type: 'HumanInteractionCompleted', request, response })
    return { taskId: request.task.id, response }
  }

  private async resolveAnswer(request: HumanInteractionRequest): Promise<ConsoleAnswer> {
    const configured = this.options.answers?.[request.task.id] ?? this.options.answers?.[request.task.nodeId]
    if (configured !== undefined) return configured
    if (this.options.answer) return this.options.answer(request)
    throw new Error(`No console answer supplied for human task ${request.task.id}`)
  }

  private toResponse(
    request: HumanInteractionRequest,
    answer: ConsoleAnswer,
    now: () => Date,
  ): HumanInteractionResponse {
    const respondedAt = now().toISOString()
    if (typeof answer === 'string') return { text: answer, respondedAt }
    if (typeof answer === 'boolean') {
      if (request.task.kind !== 'approval') {
        throw new Error(`Boolean console answers are only valid for approval task ${request.task.id}`)
      }
      return { decision: answer ? 'approved' : 'rejected', respondedAt }
    }
    if (request.task.kind === 'approval' && answer.decision === undefined) {
      throw new Error(`Approval task ${request.task.id} requires an approved or rejected decision`)
    }
    if (request.task.kind === 'input' && answer.text === undefined) {
      throw new Error(`Input task ${request.task.id} requires a text answer`)
    }
    return { ...answer, respondedAt: answer.respondedAt ?? respondedAt }
  }

  private async ensureNotExpired(
    request: HumanInteractionRequest,
    now: () => Date,
    eventSink: HumanInteractionProviderOptions['onEvent'],
  ): Promise<void> {
    if (!request.task.expiresAt) return
    const expiresAt = new Date(request.task.expiresAt)
    if (Number.isNaN(expiresAt.getTime()) || now().getTime() < expiresAt.getTime()) return
    const expiredAt = now().toISOString()
    await this.emit(eventSink, { type: 'HumanInteractionExpired', request, expiredAt })
    throw new HumanInteractionExpiredError(request.task.id, expiredAt)
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
