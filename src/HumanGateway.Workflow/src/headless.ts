import { ConsoleHumanInteractionProvider, type ConsoleAnswer } from './console.js'
import { HumanGatewayInteractionProvider, type HumanGatewayTaskMessage, type HumanGatewayTransport } from './human-gateway.js'
import type {
  HumanInteractionEvent,
  HumanInteractionRequest,
  HumanInteractionResult,
  PendingHumanTask,
} from './types.js'

/** Answers accepted by the reproducible `--answers` harness. */
export type HeadlessAnswers = Readonly<Record<string, ConsoleAnswer>>

/** The published FlowForge runner seam used by the harness and contract tests. */
export interface HeadlessWorkflowRunner {
  run(provider: { requestInteraction(request: HumanInteractionRequest): Promise<HumanInteractionResult> }): Promise<HumanInteractionResult[]>
}

/** Minimal runner implementation: FlowForge owns this sequencing in production. */
export class StubWorkflowRunner implements HeadlessWorkflowRunner {
  constructor(private readonly tasks: readonly PendingHumanTask[]) {}

  async run(provider: { requestInteraction(request: HumanInteractionRequest): Promise<HumanInteractionResult> }): Promise<HumanInteractionResult[]> {
    const results: HumanInteractionResult[] = []
    for (const task of this.tasks) results.push(await provider.requestInteraction({ task }))
    return results
  }
}

export interface HeadlessRun {
  results: HumanInteractionResult[]
  events: HumanInteractionEvent[]
}

export interface HeadlessComparison {
  console: HeadlessRun
  humanGateway: HeadlessRun
}

function recordEvents(): { events: HumanInteractionEvent[]; onEvent: (event: HumanInteractionEvent) => void } {
  const events: HumanInteractionEvent[] = []
  return { events, onEvent: event => { events.push(event) } }
}

function gatewayTransport(answers: HeadlessAnswers, now: () => Date): HumanGatewayTransport {
  return {
    async sendHumanTask(message: HumanGatewayTaskMessage) {
      const answer = answers[message.task.id] ?? answers[message.task.nodeId]
      if (answer === undefined) throw new Error(`No headless answer supplied for human task ${message.task.id}`)
      const response = typeof answer === 'string'
        ? { text: answer, respondedAt: now().toISOString() }
        : typeof answer === 'boolean'
          ? { decision: answer ? 'approved' as const : 'rejected' as const, respondedAt: now().toISOString() }
          : { ...answer, respondedAt: answer.respondedAt ?? now().toISOString() }
      return { status: 'completed' as const, task: message.task, response }
    },
  }
}

/** Run the same task script through both providers for a deterministic comparison. */
export async function compareHeadlessProviders(
  tasks: readonly PendingHumanTask[],
  answers: HeadlessAnswers,
): Promise<HeadlessComparison> {
  const runner = new StubWorkflowRunner(tasks)
  const consoleEvents = recordEvents()
  const gatewayEvents = recordEvents()
  const consoleRun = {
    results: await runner.run(new ConsoleHumanInteractionProvider({ answers, now: () => new Date('2026-01-01T00:00:00.000Z'), onEvent: consoleEvents.onEvent })),
    events: consoleEvents.events,
  }
  const gatewayRun = {
    results: await runner.run(new HumanGatewayInteractionProvider({ transport: gatewayTransport(answers, () => new Date('2026-01-01T00:00:00.000Z')), now: () => new Date('2026-01-01T00:00:00.000Z'), onEvent: gatewayEvents.onEvent })),
    events: gatewayEvents.events,
  }
  return { console: consoleRun, humanGateway: gatewayRun }
}

/** Parse `--answers JSON` or `--answers @path` without making the harness interactive. */
export async function parseAnswersArgument(value: string): Promise<HeadlessAnswers> {
  // File loading is intentionally kept out of the provider; the CLI may pass
  // JSON directly (and can resolve @files in its own environment).
  const json = value
  const parsed: unknown = JSON.parse(json)
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('--answers must be a JSON object')
  return parsed as HeadlessAnswers
}
