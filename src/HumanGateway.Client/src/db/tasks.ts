/**
 * Human-task repository (PWA-FR-06).
 *
 * Stores the protocol `HumanTask` records locally so a teacher can read a task
 * and answer it with no network. Tasks are keyed by durable id and indexed by
 * lifecycle status, enabling the task list to filter to open tasks
 * (REQUESTED / DELIVERED_TO_HUMAN) without a full scan.
 */

import type { HumanTask, HumanTaskStatus } from '../types/protocol'
import { deleteValue, getAllByIndex, getAllValues, getValue, putValue } from './database'
import { INDEXES, STORES } from './schema'

/** Upserts a human task. */
export function putTask(task: HumanTask): Promise<IDBValidKey> {
  return putValue(STORES.tasks, task)
}

/** Gets a task by id, or `undefined` when absent. */
export function getTask(id: string): Promise<HumanTask | undefined> {
  return getValue<HumanTask>(STORES.tasks, id)
}

/** Lists every task, newest first (by request/creation time). */
export async function listTasks(): Promise<HumanTask[]> {
  const tasks = await getAllValues<HumanTask>(STORES.tasks)
  return tasks.sort(byNewestFirst)
}

/** Lists tasks in a single lifecycle state, newest first. */
export async function listTasksByStatus(status: HumanTaskStatus): Promise<HumanTask[]> {
  const tasks = await getAllByIndex<HumanTask>(STORES.tasks, INDEXES.tasksByStatus, status)
  return tasks.sort(byNewestFirst)
}

/** Deletes a task. */
export function deleteTask(id: string): Promise<undefined> {
  return deleteValue(STORES.tasks, id)
}

function byNewestFirst(a: HumanTask, b: HumanTask): number {
  const aTime = a.requestedAt ?? a.createdAt
  const bTime = b.requestedAt ?? b.createdAt
  return bTime.localeCompare(aTime) || b.id.localeCompare(a.id)
}
