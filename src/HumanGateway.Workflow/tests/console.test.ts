import { describe, expect, it } from 'vitest'
import {
  ConsoleHumanInteractionProvider,
  HumanInteractionExpiredError,
  type HumanInteractionEvent,
  type HumanInteractionRequest,
} from '../src/index.js'

const request = (kind: 'input' | 'approval' = 'input'): HumanInteractionRequest => ({
  requestedAt: '2026-01-01T00:00:00.000Z',
  task: {
    id: 'task-1',
    workflowRef: 'run-1',
    nodeId: 'node-1',
    kind,
    role: 'teacher',
    prompt: kind === 'approval' ? 'Approve?' : 'What is your answer?',
    subject: 'Test task',
  },
})

describe('ConsoleHumanInteractionProvider', () => {
  it('resolves input answers and emits the lifecycle', async () => {
    const events: HumanInteractionEvent[] = []
    const result = await new ConsoleHumanInteractionProvider({
      answers: { 'task-1': '42' },
      now: () => new Date('2026-01-01T00:00:01.000Z'),
    }).requestInteraction(request(), { onEvent: event => { events.push(event) } })

    expect(result).toEqual({
      taskId: 'task-1',
      response: { text: '42', respondedAt: '2026-01-01T00:00:01.000Z' },
    })
    expect(events.map(event => event.type)).toEqual([
      'HumanInteractionRequested',
      'HumanResponseReceived',
      'HumanInteractionCompleted',
    ])
  })

  it('supports approval decisions and artifacts', async () => {
    const events: HumanInteractionEvent[] = []
    const result = await new ConsoleHumanInteractionProvider({
      answers: {
        'node-1': {
          decision: 'approved',
          artifacts: [{ id: 'a-1', hash: 'sha256:abc', filename: 'evidence.txt' }],
        },
      },
      now: () => new Date('2026-01-01T00:00:01.000Z'),
    }).requestInteraction(request('approval'), { onEvent: event => { events.push(event) } })

    expect(result.response.decision).toBe('approved')
    expect(events.map(event => event.type)).toEqual([
      'HumanInteractionRequested',
      'HumanResponseReceived',
      'ArtifactReceived',
      'HumanInteractionCompleted',
    ])
  })

  it('reports expired tasks before returning an error', async () => {
    const events: HumanInteractionEvent[] = []
    const provider = new ConsoleHumanInteractionProvider({
      answers: { 'task-1': 'late' },
      now: () => new Date('2026-01-01T00:00:02.000Z'),
    })
    const expired = request()
    expired.task.expiresAt = '2026-01-01T00:00:01.000Z'

    await expect(provider.requestInteraction(expired, { onEvent: event => { events.push(event) } }))
      .rejects.toBeInstanceOf(HumanInteractionExpiredError)
    expect(events.map(event => event.type)).toEqual([
      'HumanInteractionRequested',
      'HumanInteractionExpired',
    ])
  })
})
