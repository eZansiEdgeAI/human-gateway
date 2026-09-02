/** Client-side session persistence for Edge and Relay user sessions (AUTH-FR-02). */

import type { UserView } from './types'

export interface AuthSession {
  token: string
  expiresAt: string
  user: UserView
}

export const AUTH_SESSION_STORAGE_KEY = 'humangateway.authSession'

export function getAuthSession(): AuthSession | null {
  if (typeof localStorage === 'undefined') return null
  try {
    const raw = localStorage.getItem(AUTH_SESSION_STORAGE_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as AuthSession
    if (!session.token || !session.expiresAt || !session.user?.id) return null
    if (Date.parse(session.expiresAt) <= Date.now()) {
      clearAuthSession()
      return null
    }
    return session
  } catch {
    return null
  }
}

export function setAuthSession(session: AuthSession): void {
  if (typeof localStorage === 'undefined') return
  localStorage.setItem(AUTH_SESSION_STORAGE_KEY, JSON.stringify(session))
}

export function clearAuthSession(): void {
  if (typeof localStorage !== 'undefined') localStorage.removeItem(AUTH_SESSION_STORAGE_KEY)
}
