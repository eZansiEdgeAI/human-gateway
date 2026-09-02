import { createContext } from 'react'
import type { AuthSession } from './session'

export interface AuthContextValue {
  session: AuthSession | null
  signIn: (username: string, password: string) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
