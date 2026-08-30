import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { TaskList } from './TaskList'
import { makeHumanTask } from '../test/fixtures'

function renderList(overrides: Partial<Parameters<typeof TaskList>[0]> = {}) {
  const props = { tasks: [], onSelectTask: () => {}, ...overrides }
  return render(<TaskList {...props} />)
}

describe('TaskList', () => {
  it('shows the empty state when there are no tasks', () => {
    renderList()
    expect(screen.getByText(/no tasks yet/i)).toBeInTheDocument()
  })

  it('renders a task subject, kind, and status as text', () => {
    renderList({
      tasks: [
        makeHumanTask({ subject: 'Attendance photo', kind: 'input', status: 'DELIVERED_TO_HUMAN' }),
      ],
    })
    expect(screen.getByText('Attendance photo')).toBeInTheDocument()
    expect(screen.getByText('Input')).toBeInTheDocument()
    expect(screen.getByText('Open')).toBeInTheDocument()
  })

  it('falls back to the prompt when no subject is set', () => {
    renderList({
      tasks: [makeHumanTask({ subject: undefined, prompt: 'Upload the roster PDF.' })],
    })
    expect(screen.getByText('Upload the roster PDF.')).toBeInTheDocument()
  })

  it('shows an expiry timestamp when set', () => {
    renderList({
      tasks: [makeHumanTask({ expiresAt: '2026-01-01T00:00:00Z' })],
    })
    expect(screen.getByText(/expires/i)).toBeInTheDocument()
  })

  it('sorts open tasks before answered ones', () => {
    const answered = makeHumanTask({ subject: 'Old', status: 'COMPLETED' })
    const open = makeHumanTask({ subject: 'Now', status: 'REQUESTED' })
    renderList({ tasks: [answered, open] })

    const headings = screen.getAllByRole('button')
    expect(headings[0]).toHaveTextContent('Now')
    expect(headings[1]).toHaveTextContent('Old')
  })

  it('calls onSelectTask with the task id when a row is clicked', async () => {
    const onSelectTask = vi.fn()
    const task = makeHumanTask({ subject: 'Attendance photo' })
    const user = userEvent.setup()
    renderList({ tasks: [task], onSelectTask })

    await user.click(screen.getByRole('button', { name: /Attendance photo/ }))
    expect(onSelectTask).toHaveBeenCalledWith(task.id)
  })
})
