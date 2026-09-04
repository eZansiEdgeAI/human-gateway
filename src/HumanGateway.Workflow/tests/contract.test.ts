import { describe, expect, it } from 'vitest'
import {
  ContractWorkflowRunner,
  HumanGatewayInteractionProvider,
  pendingHumanTask,
  type HumanInteractionEvent,
  type HumanGatewayTaskMessage,
} from '../src/index.js'

describe('published FlowForge runner contract', () => {
  const cases: ReadonlyArray<['input' | 'approval', { text: string } | { decision: 'approved' }]> = [
    ['input', { text: 'Ada' }],
    ['approval', { decision: 'approved' }],
  ]

  it.each(cases)('resumes a %s PendingHumanTask with the provider response', async (kind, response) => {
    const task = pendingHumanTask({
      id: `${kind}-1`, workflowRef: 'workflow-run-1', nodeId: `node-${kind}`, kind,
      role: 'teacher', prompt: kind === 'input' ? 'Name?' : 'Approve?', subject: 'Contract test',
      correlationToken: 'opaque-correlation-token',
    })
    let envelope: HumanGatewayTaskMessage | undefined
    const events: HumanInteractionEvent[] = []
    const runner = new ContractWorkflowRunner([task])
    const provider = new HumanGatewayInteractionProvider({
      transport: { sendHumanTask: async message => {
        envelope = message
        return { status: 'completed', task: message.task, response }
      } },
      onEvent: event => { events.push(event) },
    })

    const results = await runner.run(provider)

    expect(envelope?.task).toMatchObject(task)
    expect(results).toEqual([{ taskId: task.id, response }])
    expect(runner.resumedTaskIds).toEqual([task.id])
    expect(events.map(event => event.type)).toEqual([
      'HumanInteractionRequested', 'HumanResponseReceived', 'HumanInteractionCompleted',
    ])
  })

  it('keeps workflow and node correlation intact through the round trip', async () => {
    const task = pendingHumanTask({
      id: 'approval-1', workflowRef: 'run-9', nodeId: 'approve-payment', kind: 'approval',
      role: 'finance', prompt: 'Approve payment?', subject: 'Invoice 42',
    })
    const runner = new ContractWorkflowRunner([task])
    const provider = new HumanGatewayInteractionProvider({
      transport: { sendHumanTask: async message => ({
        status: 'completed', task: { ...message.task }, response: { decision: 'rejected', reason: 'Needs review' },
      }) },
    })

    const [result] = await runner.run(provider)

    expect(result.taskId).toBe('approval-1')
    expect(runner.pendingTasks[0]).toMatchObject({
      workflowRef: 'run-9', nodeId: 'approve-payment', role: 'finance',
      prompt: 'Approve payment?', subject: 'Invoice 42',
    })
  })
})
