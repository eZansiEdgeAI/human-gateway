/**
 * Human-task presentation helpers (PWA-FR-06, offline-pwa §4 Task view).
 *
 * Pure, framework-free helpers that decide how a `HumanTask` should be
 * presented and whether it is still answerable. They are the single source of
 * truth for the task list and task answering UI:
 *
 *  - `taskResponseType` maps a task's `kind`/`options` to the input control
 *    the UI renders (free text, single choice, or approve/reject).
 *  - `isOpenTask` / `isTaskExpired` gate whether an answer form should render.
 *  - the status/kind labels + expiry formatting keep the list and detail views
 *    consistent.
 */

import type { HumanTask, HumanTaskKind, HumanTaskStatus } from '../types/protocol'

/** The input control the task answering UI renders for a task. */
export type TaskResponseType = 'freeText' | 'choice' | 'approval'

/**
 * Determines how a task should be answered.
 *
 *  - `approval` → approve / reject with an optional reason.
 *  - `input` with `options` → single-choice selection (radio group). The v1
 *    protocol's `options` array has no multiplicity flag, so choice is rendered
 *    single-select; multi-choice awaits an explicit protocol flag.
 *  - `input` without options → free-text answer.
 */
export function taskResponseType(
  task: Pick<HumanTask, 'kind' | 'options'>,
): TaskResponseType {
  if (task.kind === 'approval') return 'approval'
  if (task.options && task.options.length > 0) return 'choice'
  return 'freeText'
}

/** True when a task is still awaiting a response (answerable). */
export function isOpenTask(task: Pick<HumanTask, 'status'>): boolean {
  return task.status === 'REQUESTED' || task.status === 'DELIVERED_TO_HUMAN'
}

/**
 * True when a task can no longer be answered: already `EXPIRED`, or its
 * `expiresAt` has passed. `now` is injectable for deterministic tests.
 */
export function isTaskExpired(
  task: Pick<HumanTask, 'status' | 'expiresAt'>,
  now: number = Date.now(),
): boolean {
  if (task.status === 'EXPIRED') return true
  if (!task.expiresAt) return false
  const time = Date.parse(task.expiresAt)
  return !Number.isNaN(time) && time <= now
}

/** Human-readable kind label (icon + text renders separately in the UI). */
export function taskKindLabel(kind?: HumanTaskKind): string {
  return kind === 'approval' ? 'Approval' : 'Input'
}

/** Presentation metadata for each task lifecycle state. */
export const TASK_STATUS_META: Record<HumanTaskStatus, { label: string; description: string }> = {
  REQUESTED: { label: 'Requested', description: 'Awaiting your response.' },
  DELIVERED_TO_HUMAN: { label: 'Open', description: 'Awaiting your response.' },
  RESPONSE_RECEIVED: { label: 'Answered', description: 'Your response was received.' },
  COMPLETED: { label: 'Completed', description: 'This task is complete.' },
  EXPIRED: { label: 'Expired', description: 'This task expired before it was answered.' },
}

/** Human-readable status label, with a stable fallback for unknown values. */
export function taskStatusLabel(status?: HumanTaskStatus): string {
  return status ? TASK_STATUS_META[status].label : 'Unknown'
}

/** An absolute, locale-formatted expiry timestamp ('' when absent/invalid). */
export function formatExpiry(iso?: string): string {
  if (!iso) return ''
  const time = new Date(iso).getTime()
  if (Number.isNaN(time)) return ''
  return new Date(time).toLocaleString()
}
