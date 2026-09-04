import type { HumanInteractionProvider } from './provider.js'
import type { HumanInteractionRequest, HumanInteractionResult, PendingHumanTask } from './types.js'

/** Consumer-side FlowForge runner seam used by contract tests. */
export interface WorkflowRunner {
  run(provider: HumanInteractionProvider): Promise<HumanInteractionResult[]>
}

/** Creates a task-shaped fixture while keeping correlation fields explicit. */
export function pendingHumanTask(
  task: Pick<PendingHumanTask, 'id' | 'workflowRef' | 'nodeId' | 'kind' | 'prompt'> &
    Partial<Omit<PendingHumanTask, 'id' | 'workflowRef' | 'nodeId' | 'kind' | 'prompt'>>,
): PendingHumanTask {
  return { ...task }
}

/** Deterministic runner fixture; graph execution remains FlowForge's responsibility. */
export class ContractWorkflowRunner implements WorkflowRunner {
  readonly resumedTaskIds: string[] = []

  constructor(readonly pendingTasks: readonly PendingHumanTask[]) {}

  async run(provider: HumanInteractionProvider): Promise<HumanInteractionResult[]> {
    const results: HumanInteractionResult[] = []
    for (const task of this.pendingTasks) {
      const request: HumanInteractionRequest = { task }
      const result = await provider.requestInteraction(request)
      this.resumedTaskIds.push(result.taskId)
      results.push(result)
    }
    return results
  }
}
