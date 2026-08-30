/**
 * Task answering view (offline-pwa §4, PWA-FR-06).
 *
 * Presents a human task and, when still answerable, the input control matching
 * its response type:
 *
 *  - **approval** → Approve / Reject with an optional reason.
 *  - **input + options** → single-choice radio group.
 *  - **input (no options)** → free-text answer.
 *
 * Every answer supports an optional artifact upload (reusing `ArtifactPicker`)
 * and shows the expiry when set. Answers are submitted offline-first via the
 * store's `answerTask` (durably queued to the outbox when offline — the sync
 * banner's "queued, will sync when connected" philosophy applies here too).
 * Already-answered or expired tasks render a read-only summary instead.
 */

import { useState, type FormEvent } from 'react'
import type {
  AnswerTaskRequest,
  ApprovalDecision,
  HumanTask,
} from '../types/protocol'
import { getLocalParticipant } from '../lib/identity'
import type { TaskAnswerOutcome } from '../store/context'
import { ArtifactPicker, type PendingAttachment } from './ArtifactPicker'
import {
  formatExpiry,
  isOpenTask,
  isTaskExpired,
  taskKindLabel,
  taskResponseType,
  taskStatusLabel,
} from '../lib/tasks'

export interface TaskViewProps {
  task: HumanTask
  online: boolean
  answerTask: (taskId: string, request: AnswerTaskRequest) => Promise<TaskAnswerOutcome>
  onAnswered: (outcome: TaskAnswerOutcome) => void
  onBack: () => void
}

export function TaskView({ task, online, answerTask, onAnswered, onBack }: TaskViewProps) {
  const responseType = taskResponseType(task)
  const readOnly = Boolean(task.response) || !isOpenTask(task) || isTaskExpired(task)

  const [text, setText] = useState('')
  const [selected, setSelected] = useState('')
  const [decision, setDecision] = useState<ApprovalDecision | null>(null)
  const [reason, setReason] = useState('')
  const [attachments, setAttachments] = useState<PendingAttachment[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    const refs = attachments.map((attachment) => attachment.ref)
    const respondedBy = getLocalParticipant()

    let request: AnswerTaskRequest
    if (responseType === 'approval') {
      if (!decision) {
        setError('Choose Approve or Reject.')
        return
      }
      request = {
        respondedBy,
        decision,
        reason: reason.trim() || undefined,
        artifactRefs: refs.length > 0 ? refs : undefined,
      }
    } else if (responseType === 'choice') {
      if (!selected) {
        setError('Choose an option.')
        return
      }
      request = {
        respondedBy,
        text: selected,
        artifactRefs: refs.length > 0 ? refs : undefined,
      }
    } else {
      const trimmed = text.trim()
      if (!trimmed && refs.length === 0) {
        setError('Type an answer or attach a file.')
        return
      }
      request = {
        respondedBy,
        text: trimmed || undefined,
        artifactRefs: refs.length > 0 ? refs : undefined,
      }
    }

    setSubmitting(true)
    try {
      const outcome = await answerTask(task.id, request)
      onAnswered(outcome)
    } catch {
      setError('Could not submit your answer. Please try again.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section aria-label="Task" className="task-view">
      <header className="task-view__header">
        <button type="button" className="button button--secondary" onClick={onBack}>
          Back
        </button>
        <span className="task-view__meta">
          <span className="task-view__kind">{taskKindLabel(task.kind)}</span>
          <span className="task-view__status">{taskStatusLabel(task.status)}</span>
        </span>
      </header>

      <h2 className="task-view__subject">{task.subject ?? task.prompt}</h2>
      {task.subject && <p className="task-view__prompt">{task.prompt}</p>}
      {task.expiresAt && (
        <p className="task-view__expiry">Expires {formatExpiry(task.expiresAt)}</p>
      )}

      {readOnly ? (
        <TaskSummary task={task} />
      ) : (
        <form onSubmit={handleSubmit} noValidate className="task-answer">
          {responseType === 'approval' && (
            <fieldset className="task-answer__field">
              <legend className="task-answer__legend">Decision</legend>
              <div className="task-answer__options">
                <label className="task-answer__option">
                  <input
                    type="radio"
                    name="decision"
                    value="approved"
                    checked={decision === 'approved'}
                    onChange={() => setDecision('approved')}
                  />
                  Approve
                </label>
                <label className="task-answer__option">
                  <input
                    type="radio"
                    name="decision"
                    value="rejected"
                    checked={decision === 'rejected'}
                    onChange={() => setDecision('rejected')}
                  />
                  Reject
                </label>
              </div>
              <label className="task-answer__label" htmlFor="task-reason">
                Reason <span className="task-answer__optional">(optional)</span>
              </label>
              <textarea
                id="task-reason"
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                rows={3}
              />
            </fieldset>
          )}

          {responseType === 'choice' && (
            <fieldset className="task-answer__field">
              <legend className="task-answer__legend">Choose an option</legend>
              <div className="task-answer__options">
                {task.options?.map((option) => (
                  <label key={option} className="task-answer__option">
                    <input
                      type="radio"
                      name="choice"
                      value={option}
                      checked={selected === option}
                      onChange={() => setSelected(option)}
                    />
                    {option}
                  </label>
                ))}
              </div>
            </fieldset>
          )}

          {responseType === 'freeText' && (
            <div className="task-answer__field">
              <label className="task-answer__label" htmlFor="task-answer-text">
                Answer
              </label>
              <textarea
                id="task-answer-text"
                value={text}
                onChange={(event) => setText(event.target.value)}
                rows={4}
              />
            </div>
          )}

          <div className="task-answer__field">
            <ArtifactPicker attachments={attachments} onAttachmentsChange={setAttachments} />
          </div>

          {error && (
            <p className="task-answer__error" role="alert">
              {error}
            </p>
          )}

          <div className="task-answer__actions">
            <button type="submit" className="button button--primary" disabled={submitting}>
              {submitting
                ? 'Submitting…'
                : online
                  ? 'Submit answer'
                  : 'Queue answer'}
            </button>
          </div>
        </form>
      )}
    </section>
  )
}

/**
 * Read-only summary of a task that is already answered, completed, or expired.
 * Surfaces the recorded response (decision/text + reason + artifact names) so
 * the teacher can review what was submitted.
 */
function TaskSummary({ task }: { task: HumanTask }) {
  const response = task.response

  return (
    <div className="task-summary" aria-live="polite">
      <p className="task-summary__status">{taskStatusLabel(task.status)}</p>

      {response ? (
        <>
          {response.decision && (
            <p className="task-summary__decision">
              {response.decision === 'approved' ? 'Approved' : 'Rejected'}
            </p>
          )}
          {response.text && <p className="task-summary__text">{response.text}</p>}
          {response.reason && (
            <p className="task-summary__reason">
              <span className="task-summary__reason-label">Reason:</span> {response.reason}
            </p>
          )}
          {response.artifactRefs && response.artifactRefs.length > 0 && (
            <ul className="task-summary__artifacts" aria-label="Attached evidence">
              {response.artifactRefs.map((ref) => (
                <li key={ref.id}>{ref.filename ?? ref.id}</li>
              ))}
            </ul>
          )}
        </>
      ) : (
        <p className="task-summary__empty">No response recorded.</p>
      )}
    </div>
  )
}
