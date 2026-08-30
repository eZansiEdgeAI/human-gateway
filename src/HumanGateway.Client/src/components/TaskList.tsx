/**
 * Task list view (offline-pwa §4, PWA-FR-06).
 *
 * Lists human tasks — open tasks first, then answered/expired history — so a
 * teacher can pick a task to answer. Each row is a real `<button>` (keyboard
 * focusable, ≥ 44×44px touch target) and shows the kind and status as text
 * (never colour alone, ACC-03).
 */

import type { HumanTask } from '../types/protocol'
import {
  formatExpiry,
  isOpenTask,
  taskKindLabel,
  taskStatusLabel,
} from '../lib/tasks'

export interface TaskListProps {
  tasks: HumanTask[]
  onSelectTask: (taskId: string) => void
}

export function TaskList({ tasks, onSelectTask }: TaskListProps) {
  const sorted = [...tasks].sort(byOpenThenNewest)

  return (
    <section aria-label="Tasks" className="task-list">
      <header className="task-list__header">
        <h2 className="task-list__heading">Tasks</h2>
      </header>

      {sorted.length === 0 ? (
        <p className="empty-state">No tasks yet. New tasks appear here as they arrive.</p>
      ) : (
        <ul className="task-list__items">
          {sorted.map((task) => (
            <li key={task.id}>
              <button
                type="button"
                className="task-item"
                onClick={() => onSelectTask(task.id)}
              >
                <span className="task-item__main">
                  <span className="task-item__top">
                    <span className="task-item__kind">{taskKindLabel(task.kind)}</span>
                    <span className="task-item__status">{taskStatusLabel(task.status)}</span>
                  </span>
                  <span className="task-item__subject">{task.subject ?? task.prompt}</span>
                  {task.subject && <span className="task-item__prompt">{task.prompt}</span>}
                  {task.expiresAt && (
                    <span className="task-item__expiry">Expires {formatExpiry(task.expiresAt)}</span>
                  )}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** Open tasks first, then newest-first by request/creation time. */
function byOpenThenNewest(a: HumanTask, b: HumanTask): number {
  const aOpen = isOpenTask(a) ? 0 : 1
  const bOpen = isOpenTask(b) ? 0 : 1
  if (aOpen !== bOpen) return aOpen - bOpen
  const aTime = a.requestedAt ?? a.createdAt
  const bTime = b.requestedAt ?? b.createdAt
  return bTime.localeCompare(aTime) || b.id.localeCompare(a.id)
}
