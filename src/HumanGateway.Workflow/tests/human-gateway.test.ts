import { describe, expect, it } from 'vitest'
import {
  HumanGatewayInteractionProvider,
  HumanInteractionExpiredError,
  type HumanInteractionEvent,
  type HumanInteractionRequest,
  type HumanGatewayTaskMessage,
} from '../src/index.js'

const request = (kind: 'input' | 'approval' = 'input'): HumanInteractionRequest => ({
  requestedAt: '2026-01-01T00:00:00.000Z',
  task: {
    id: 'task-1', workflowRef: 'run-1', nodeId: 'node-1', kind,
    role: 'teacher', prompt: kind === 'approval' ? 'Approve?' : 'What is your answer?',
    subject: 'Test task', correlationToken: 'opaque-token', expiresAt: '2026-02-01T00:00:00.000Z',
  },
})

describe('HumanGatewayInteractionProvider', () => {
  it('translates a pending task and maps the response and artifacts', async () => {
    const events: HumanInteractionEvent[] = []
    let sent: HumanGatewayTaskMessage | undefined
    const result = await new HumanGatewayInteractionProvider({
      now: () => new Date('2026-01-01T00:00:01.000Z'),
      transport: { sendHumanTask: async message => {
        sent = message
        return {
          status: 'completed',
          response: { text: '42', respondedAt: '2026-01-01T00:00:02.000Z' },
          artifacts: [{ id: 'a-1', hash: 'sha256:abc', filename: 'evidence.txt' }],
        }
      } },
    }).requestInteraction(request(), { onEvent: event => { events.push(event) } })

    expect(sent).toEqual({
      type: 'human-task',
      task: {
        id: 'task-1', kind: 'input', workflowRef: 'run-1', nodeId: 'node-1', role: 'teacher',
        prompt: 'What is your answer?', subject: 'Test task', options: undefined,
        correlationToken: 'opaque-token', expiresAt: '2026-02-01T00:00:00.000Z',
      },
      requestedAt: '2026-01-01T00:00:00.000Z',
    })
    expect(result.response.artifacts?.[0].id).toBe('a-1')
    expect(events.map(event => event.type)).toEqual([
      'HumanInteractionRequested', 'HumanResponseReceived', 'ArtifactReceived', 'HumanInteractionCompleted',
    ])
  })

  it('supports approval tasks and does not duplicate transport artifacts', async () => {
    const result = await new HumanGatewayInteractionProvider({
      transport: { sendHumanTask: async () => ({
        status: 'completed',
        response: { decision: 'approved', artifacts: [{ id: 'a-1', hash: 'sha256:abc' }] },
        artifacts: [{ id: 'a-1', hash: 'sha256:abc' }, { id: 'a-2', hash: 'sha256:def' }],
      }) },
    }).requestInteraction(request('approval'))

    expect(result.response.decision).toBe('approved')
    expect(result.response.artifacts?.map(artifact => artifact.id)).toEqual(['a-1', 'a-2'])
  })

  it('surfaces an expired gateway interaction as a provider error and event', async () => {
    const events: HumanInteractionEvent[] = []
    const provider = new HumanGatewayInteractionProvider({
      transport: { sendHumanTask: async () => ({ status: 'expired', expiredAt: '2026-01-02T00:00:00.000Z' }) },
    })

    await expect(provider.requestInteraction(request(), { onEvent: event => { events.push(event) } }))
      .rejects.toBeInstanceOf(HumanInteractionExpiredError)
    expect(events.map(event => event.type)).toEqual(['HumanInteractionRequested', 'HumanInteractionExpired'])
  })
})
