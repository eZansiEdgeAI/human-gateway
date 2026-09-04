import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AdminUsers } from './AdminUsers'
import type { UserView } from '../auth/types'

vi.mock('../auth/api', () => ({
  listUsers: vi.fn(),
  createUser: vi.fn(),
}))

import { createUser, listUsers } from '../auth/api'

const users: UserView[] = [{
  id: 'user-1', username: 'admin', displayName: 'Admin User', role: 'ADMIN', status: 'ACTIVE', createdAt: '2026-01-01T00:00:00Z',
}]

describe('AdminUsers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(listUsers).mockResolvedValue(users)
  })

  it('shows a stable loading state before accounts arrive', async () => {
    let resolveUsers!: (value: UserView[]) => void
    vi.mocked(listUsers).mockReturnValue(new Promise(resolve => { resolveUsers = resolve }))
    render(<AdminUsers onBack={() => {}} />)
    expect(screen.getByRole('status')).toHaveTextContent('Loading accounts')
    resolveUsers(users)
    expect(await screen.findByText('Admin User')).toBeInTheDocument()
  })

  it('creates an account, resets the form, and refreshes without losing the list', async () => {
    vi.mocked(createUser).mockResolvedValue({ ...users[0], id: 'user-2', username: 'teacher', displayName: 'Teacher' })
    const user = userEvent.setup()
    render(<AdminUsers onBack={() => {}} />)
    await screen.findByText('Admin User')
    await user.type(screen.getByLabelText('Username'), 'teacher')
    await user.type(screen.getByLabelText('Display name'), 'Teacher')
    await user.type(screen.getByLabelText('Temporary password'), 'temporary-password')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() => expect(createUser).toHaveBeenCalledWith({ username: 'teacher', displayName: 'Teacher', password: 'temporary-password' }))
    expect(screen.getByRole('status')).toHaveTextContent('User created.')
    expect(screen.getByLabelText('Username')).toHaveValue('')
    expect(listUsers).toHaveBeenCalledTimes(2)
  })

  it('shows API errors without removing the form', async () => {
    vi.mocked(listUsers).mockRejectedValue(new Error('Unable to load users.'))
    render(<AdminUsers onBack={() => {}} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Unable to load users.')
    expect(screen.getByLabelText('Username')).toBeInTheDocument()
  })
})
