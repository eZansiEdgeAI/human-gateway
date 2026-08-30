/**
 * Offline detection (offline-pwa §4, PWA-FR-01, product vision §10).
 *
 * A framework-agnostic source of truth for the browser's connectivity signal.
 * The React hook (`src/hooks/useOnlineStatus.ts`) and — later — the
 * offline-first Edge API client both consume this module, so "are we online?"
 * is answered in exactly one place.
 *
 * v1 uses the browser's own `navigator.onLine` signal plus the `online` /
 * `offline` window events (product vision §10). A reachability probe against
 * the Edge health endpoint will layer on top in a later task: `navigator.onLine`
 * is a necessary-but-not-sufficient signal (it can read `true` behind a
 * captive portal or on a dead LAN).
 */

/** A single online/offline transition listener. */
export type ConnectivityListener = (online: boolean) => void

/**
 * Reads the current online status. Safe in non-browser (SSR/test) contexts,
 * and treats an unreported `navigator.onLine` (undefined) as "online" so we
 * never falsely alarm the user into thinking their device is offline.
 */
export function isOnline(): boolean {
  return typeof navigator !== 'undefined' && navigator.onLine !== false
}

/**
 * Subscribes to online/offline transitions. Invokes the listener immediately
 * with the current state (so callers render correctly before the first event),
 * then again on every `online`/`offline` window event. Returns an unsubscribe
 * function for use as a React effect cleanup.
 */
export function subscribeConnectivity(
  listener: ConnectivityListener,
): () => void {
  const handleOnline = () => listener(true)
  const handleOffline = () => listener(false)

  window.addEventListener('online', handleOnline)
  window.addEventListener('offline', handleOffline)
  // Push the current state so callers render correctly before any event fires.
  listener(isOnline())

  return () => {
    window.removeEventListener('online', handleOnline)
    window.removeEventListener('offline', handleOffline)
  }
}
