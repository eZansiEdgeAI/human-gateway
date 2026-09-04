import { describe, expect, it } from 'vitest'
import { compareHeadlessProviders, parseAnswersArgument, type PendingHumanTask } from '../src/index.js'

const tasks: PendingHumanTask[] = [
  { id: 'input-1', workflowRef: 'run-1', nodeId: 'ask-name', kind: 'input', prompt: 'Name?' },
  { id: 'approval-1', workflowRef: 'run-1', nodeId: 'approve', kind: 'approval', prompt: 'Approve?' },
]

describe('headless answers harness', () => {
  it('produces the same outcome through Console and HumanGateway providers', async () => {
    const result = await compareHeadlessProviders(tasks, { 'input-1': 'Ada', 'approval-1': true })

    expect(result.console.results).toEqual(result.humanGateway.results)
    expect(result.humanGateway.events.map(event => event.type)).toEqual([
      'HumanInteractionRequested', 'HumanResponseReceived', 'HumanInteractionCompleted',
      'HumanInteractionRequested', 'HumanResponseReceived', 'HumanInteractionCompleted',
    ])
  })

  it('supports node-id answer keys and JSON --answers values', async () => {
    const answers = await parseAnswersArgument('{"ask-name":"Ada","approve":false}')
    const result = await compareHeadlessProviders(tasks, answers)

    expect(result.humanGateway.results.map(item => item.response)).toEqual([
      { text: 'Ada', respondedAt: '2026-01-01T00:00:00.000Z' },
      { decision: 'rejected', respondedAt: '2026-01-01T00:00:00.000Z' },
    ])
  })
})
