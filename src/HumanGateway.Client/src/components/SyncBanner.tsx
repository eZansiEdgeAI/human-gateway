import { useOnlineStatus } from '../hooks/useOnlineStatus'

/**
 * Sync banner: surfaces offline/online state to the user (offline-pwa §4).
 * "queued, will sync when connected" is intentionally a calm, non-error message
 * (product vision §10 — offline deferral is expected, never an error).
 */
export function SyncBanner() {
  const online = useOnlineStatus()

  if (online) {
    return null
  }

  return (
    <div
      role="status"
      aria-live="polite"
      className="sync-banner"
    >
      <span className="sync-banner__dot" aria-hidden="true" />
      <span>You're offline — messages will sync when you're connected.</span>
    </div>
  )
}
