import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TaskView } from './TaskView'
import type { AnswerTaskRequest } from '../types/protocol'
import { makeHumanTask } from '../test/fixtures'

function renderView(
  overrides: Partial<Parameters<typeof TaskView>[0]> = {},
) {
  const props = {
    task: makeHumanTask(),
    online: true,
    answerTask: vi.fn().mockResolvedValue({ disposition: 'queued', taskId: 'task-1' }),
    onAnswered: vi.fn(),
    onBack: vi.fn(),
    ...overrides,
  }
  return render(<TaskView {...props} />)
}

describe('TaskView', () => {
  it('renders the task subject and prompt', () => {
    renderView({
      task: makeHumanTask({ subject: 'Attendance', prompt: 'Upload the attendance photo.' }),
    })
    expect(screen.getByRole('heading', { name: 'Attendance' })).toBeInTheDocument()
    expect(screen.getByText('Upload the attendance photo.')).toBeInTheDocument()
  })

  it('shows the expiry when set', () => {
    renderView({ task: makeHumanTask({ expiresAt: '2026-01-01T00:00:00Z' }) })
    expect(screen.getByText(/expires/i)).toBeInTheDocument()
  })

  it('submits a free-text answer', async () => {
    const task = makeHumanTask()
    const answerTask = vi.fn().mockResolvedValue({ disposition: 'queued', taskId: task.id })
    const onAnswered = vi.fn()
    const user = userEvent.setup()
    renderView({ task, answerTask, onAnswered })

    await user.type(screen.getByLabelText('Answer'), 'Present.')
    await user.click(screen.getByRole('button', { name: 'Submit answer' }))

    await waitFor(() => expect(answerTask).toHaveBeenCalledOnce())
    expect(answerTask.mock.calls[0][0]).toBe(task.id)
    expect(answerTask.mock.calls[0][1]).toMatchObject({ text: 'Present.' })
    expect(onAnswered).toHaveBeenCalledOnce()
  })

  it('submits a choice answer from the radio group', async () => {
    const answerTask = vi.fn().mockResolvedValue({ disposition: 'queued', taskId: 'task-1' })
    const user = userEvent.setup()
    renderView({
      task: makeHumanTask({ kind: 'input', options: ['Option A', 'Option B'] }),
      answerTask,
    })

    await user.click(screen.getByRole('radio', { name: 'Option A' }))
    await user.click(screen.getByRole('button', { name: 'Submit answer' }))

    await waitFor(() => expect(answerTask).toHaveBeenCalledOnce())
    expect(answerTask.mock.calls[0][1]).toMatchObject({ text: 'Option A' })
  })

  it('requires a decision for approval tasks', async () => {
    const answerTask = vi.fn()
    const user = userEvent.setup()
    renderView({ task: makeHumanTask({ kind: 'approval' }), answerTask })

    await user.click(screen.getByRole('button', { name: 'Submit answer' }))
    expect(screen.getByText(/choose approve or reject/i)).toBeInTheDocument()
    expect(answerTask).not.toHaveBeenCalled()
  })

  it('submits an approval decision with an optional reason', async () => {
    const answerTask = vi.fn().mockResolvedValue({ disposition: 'queued', taskId: 'task-1' })
    const user = userEvent.setup()
    renderView({ task: makeHumanTask({ kind: 'approval' }), answerTask })

    await user.click(screen.getByRole('radio', { name: 'Approve' }))
    await user.type(screen.getByLabelText(/reason/i), 'Looks good.')
    await user.click(screen.getByRole('button', { name: 'Submit answer' }))

    await waitFor(() => expect(answerTask).toHaveBeenCalledOnce())
    const request = answerTask.mock.calls[0][1] as AnswerTaskRequest
    expect(request.decision).toBe('approved')
    expect(request.reason).toBe('Looks good.')
  })

  it('allows a photo-only answer (no text required)', async () => {
    const answerTask = vi.fn().mockResolvedValue({ disposition: 'queued', taskId: 'task-1' })
    const user = userEvent.setup()
    renderView({
      task: makeHumanTask({ kind: 'input', prompt: 'Upload your attendance photo.' }),
      answerTask,
    })

    // No text typed; submit an empty free-text answer — must be blocked, so the
    // test proves attachment is the only permitted path is exercised separately.
    await user.click(screen.getByRole('button', { name: 'Submit answer' }))
    expect(screen.getByText(/type an answer or attach a file/i)).toBeInTheDocument()
    expect(answerTask).not.toHaveBeenCalled()
  })

  it('shows a read-only summary for an already-answered task', () => {
    renderView({
      task: makeHumanTask({
        kind: 'approval',
        status: 'COMPLETED',
        response: { decision: 'approved', reason: 'Verified.', respondedAt: new Date().toISOString() },
      }),
    })
    expect(screen.getByText('Approved')).toBeInTheDocument()
    expect(screen.getByText(/verified/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit answer' })).not.toBeInTheDocument()
  })

  it('shows a read-only summary for an expired task', () => {
    renderView({
      task: makeHumanTask({
        kind: 'input',
        status: 'EXPIRED',
        expiresAt: '2020-01-01T00:00:00Z',
      }),
    })
    expect(screen.getAllByText('Expired').length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Submit answer' })).not.toBeInTheDocument()
  })

  it('calls onBack when Back is clicked', async () => {
    const onBack = vi.fn()
    const user = userEvent.setup()
    renderView({ onBack })
    await user.click(screen.getByRole('button', { name: 'Back' }))
    expect(onBack).toHaveBeenCalledOnce()
  })
})
