import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getAuthSession } from './session'
import { login, logout } from './api'

describe('remote authentication API', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('logs in against the configured Relay/Edge auth endpoint and persists the bearer session', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      token: 'opaque-session-token',
      expiresAt: '2099-01-01T00:00:00Z',
      user: { id: 'user-1', username: 'reviewer', displayName: 'Reviewer', status: 'ACTIVE', createdAt: '2026-01-01T00:00:00Z' },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))

    const session = await login('reviewer', 'secret')
    expect(session.token).toBe('opaque-session-token')
    expect(getAuthSession()?.user.username).toBe('reviewer')
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/auth/login'), expect.objectContaining({ method: 'POST' }))
  })

  it('sends the bearer token when logging out and clears the local session', async () => {
    localStorage.setItem('humangateway.authSession', JSON.stringify({
      token: 'token', expiresAt: '2099-01-01T00:00:00Z', user: { id: 'u', username: 'u', displayName: 'U', status: 'ACTIVE', createdAt: '2026-01-01T00:00:00Z' },
    }))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await logout()
    expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/auth/logout'), expect.objectContaining({ method: 'POST' }))
    expect(getAuthSession()).toBeNull()
  })
})
