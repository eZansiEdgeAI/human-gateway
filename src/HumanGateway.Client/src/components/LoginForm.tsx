import { useState, type FormEvent } from 'react'
import { useAuth } from '../auth/useAuth'

export function LoginForm() {
  const { signIn } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await signIn(username, password)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Unable to sign in.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section aria-labelledby="login-title" className="login-card">
      <h2 id="login-title">Sign in</h2>
      <p>Sign in to your HumanGateway account to view your conversations and tasks.</p>
      <form onSubmit={submit}>
        <label htmlFor="login-username">Username</label>
        <input id="login-username" name="username" autoComplete="username" required value={username} onChange={(e) => setUsername(e.target.value)} />
        <label htmlFor="login-password">Password</label>
        <input id="login-password" name="password" type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        {error && <p className="app-error" role="alert">{error}</p>}
        <button className="button button--primary" type="submit" disabled={submitting}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </section>
  )
}
