import { useEffect, useState, type FormEvent } from 'react'
import { createUser, listUsers } from '../auth/api'
import type { UserView } from '../auth/types'

export function AdminUsers({ onBack }: { onBack: () => void }) {
  const [users, setUsers] = useState<UserView[]>([])
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function refresh() {
    try { setUsers(await listUsers()); setError(null) } catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to load users.') }
  }

  useEffect(() => { void refresh() }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setSubmitting(true); setError(null); setMessage(null)
    try {
      await createUser({ username: String(form.get('username')), displayName: String(form.get('displayName')), password: String(form.get('password')) })
      event.currentTarget.reset(); setMessage('User created.'); await refresh()
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to create user.') } finally { setSubmitting(false) }
  }

  return <section aria-labelledby="admin-users-title" className="admin-users">
    <div className="admin-users__header"><div><h2 id="admin-users-title">User management</h2><p>Create accounts for people who need access to this gateway.</p></div><button type="button" className="button" onClick={onBack}>Back</button></div>
    <form className="admin-users__form" onSubmit={submit}>
      <label htmlFor="new-username">Username</label><input id="new-username" name="username" minLength={3} required />
      <label htmlFor="new-display-name">Display name</label><input id="new-display-name" name="displayName" required />
      <label htmlFor="new-password">Temporary password</label><input id="new-password" name="password" type="password" minLength={8} required />
      <button className="button button--primary" type="submit" disabled={submitting}>{submitting ? 'Creating…' : 'Create user'}</button>
    </form>
    {message && <p className="app-status" role="status">{message}</p>}
    {error && <p className="app-error" role="alert">{error}</p>}
    <h3>Accounts</h3>
    <ul className="admin-users__list">{users.map(user => <li key={user.id}><strong>{user.displayName}</strong><span>{user.username} · {user.role} · {user.status}</span></li>)}</ul>
  </section>
}
