import { resolveApiUrl } from '../api/config'
import { httpRequest } from '../api/http'
import { clearAuthSession, setAuthSession, type AuthSession } from './session'
import type { LoginResponse, UserView } from './types'

export async function login(username: string, password: string): Promise<AuthSession> {
  const response = await httpRequest<LoginResponse>({
    url: resolveApiUrl('/auth/login'),
    method: 'POST',
    body: { username, password },
  })
  const session: AuthSession = { token: response.token, expiresAt: response.expiresAt, user: response.user }
  setAuthSession(session)
  return session
}

export async function getCurrentUser(): Promise<UserView> {
  return httpRequest<UserView>({ url: resolveApiUrl('/auth/me'), method: 'GET' })
}

export async function logout(): Promise<void> {
  try {
    await httpRequest<void>({ url: resolveApiUrl('/auth/logout'), method: 'POST' })
  } finally {
    clearAuthSession()
  }
}
