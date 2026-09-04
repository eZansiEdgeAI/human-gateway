import { useEffect, useState, type FormEvent } from 'react'
import { createUser, listUsers } from '../auth/api'
import type { UserView } from '../auth/types'

export function AdminUsers({ onBack }: { onBack: () => void }) {
  const [users, setUsers] = useState<UserView[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function refresh(options: { initial?: boolean } = {}) {
    if (options.initial) setLoading(true)
    else setRefreshing(true)
    try {
      setUsers(await listUsers())
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Unable to load users.')
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }

  useEffect(() => { void refresh({ initial: true }) }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const formElement = event.currentTarget
    const form = new FormData(formElement)
    setSubmitting(true); setError(null); setMessage(null)
    try {
      await createUser({ username: String(form.get('username')), displayName: String(form.get('displayName')), password: String(form.get('password')) })
      formElement.reset()
      setMessage('User created.')
      await refresh()
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to create user.') } finally { setSubmitting(false) }
  }

  return <section aria-labelledby="admin-users-title" className="admin-users">
    <div className="admin-users__header">
      <div>
        <p className="admin-users__eyebrow">Gateway administration</p>
        <h2 id="admin-users-title">User management</h2>
        <p className="admin-users__intro">Create accounts for people who need access to this gateway.</p>
      </div>
      <button type="button" className="button" onClick={onBack}>Back to inbox</button>
    </div>
    <form className="admin-users__form" onSubmit={submit}>
      <div className="admin-users__form-heading">
        <h3>New account</h3>
        <p>Share the temporary password securely with the account owner.</p>
      </div>
      <div className="admin-users__fields">
        <label htmlFor="new-username">Username<input id="new-username" name="username" minLength={3} required /></label>
        <label htmlFor="new-display-name">Display name<input id="new-display-name" name="displayName" required /></label>
        <label htmlFor="new-password">Temporary password<input id="new-password" name="password" type="password" minLength={8} required /></label>
      </div>
      <button className="button button--primary" type="submit" disabled={submitting}>{submitting ? 'Creating account…' : 'Create account'}</button>
    </form>
    {message && <p className="app-status" role="status">{message}</p>}
    {error && <p className="app-error" role="alert">{error}</p>}
    <div className="admin-users__accounts-heading">
      <div><p className="admin-users__eyebrow">Directory</p><h3>Accounts</h3></div>
      <span className="admin-users__count">{users.length} {users.length === 1 ? 'account' : 'accounts'}</span>
    </div>
    {loading ? <p className="admin-users__loading" role="status">Loading accounts…</p> : <ul className="admin-users__list" aria-busy={refreshing}>{users.map(user => <li key={user.id} className="admin-users__item"><span className="admin-users__avatar" aria-hidden="true">{user.displayName.charAt(0).toUpperCase()}</span><span className="admin-users__identity"><strong>{user.displayName}</strong><span>{user.username}</span></span><span className={`admin-users__badge admin-users__badge--${user.role.toLowerCase()}`}>{user.role}</span><span className={`admin-users__badge admin-users__badge--${user.status.toLowerCase()}`}>{user.status}</span></li>)}</ul>}
  </section>
}
