import { useEffect, useState } from 'react'
import { isOnline, subscribeConnectivity } from '../lib/connectivity'

/**
 * Tracks browser online/offline state for the PWA lifecycle
 * (ONLINE → OFFLINE → RECONNECTING → ONLINE, product vision §10).
 *
 * Delegates to `src/lib/connectivity.ts` — the single source of truth the
 * offline-first Edge API client will also consume — so the sync banner and the
 * fetch wrapper never disagree about connectivity.
 */
export function useOnlineStatus(): boolean {
  const [online, setOnline] = useState<boolean>(isOnline)

  useEffect(() => subscribeConnectivity(setOnline), [])

  return online
}
