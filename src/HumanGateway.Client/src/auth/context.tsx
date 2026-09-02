import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { login, logout } from './api'
import { getAuthSession, type AuthSession } from './session'
import { AuthContext } from './contextBase'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(() => getAuthSession())
  const signIn = useCallback(async (username: string, password: string) => setSession(await login(username, password)), [])
  const signOut = useCallback(async () => {
    await logout()
    setSession(null)
  }, [])
  const value = useMemo(() => ({ session, signIn, signOut }), [session, signIn, signOut])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
