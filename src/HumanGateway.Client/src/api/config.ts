/**
 * Edge base-URL resolution for the offline-first API client (EDGE-FR-03).
 *
 * The Edge REST API runs on a different origin from the PWA (it does not serve
 * static files — see `HumanGateway.Edge/Program.cs`), so the client must know
 * where to reach it. v1 resolves that origin in three layers, in priority
 * order (local-edge Open Q #2: "documented fixed host/IP config (v1)"):
 *
 *   1. **Runtime override** — a teacher/operator can point the PWA at their
 *      school's Edge from a settings screen (`setEdgeBaseUrl` persists to
 *      `localStorage`). Survives a reload and works offline.
 *   2. **Build-time env** — `VITE_EDGE_BASE_URL` baked in at build time.
 *   3. **Relay-hosted build** — same-origin API calls when the PWA is built for
 *      the public Relay.
 *   4. **Default** — the documented localhost dev URL (`dotnet run` from the
 *      Edge README).
 *
 * The value is always normalised to a trailing-slash-free origin, so callers
 * can safely concatenate a leading-slash path.
 */

/** `localStorage` key holding the runtime Edge base URL. */
export const EDGE_BASE_URL_STORAGE_KEY = 'humangateway.edgeBaseUrl'

/** Build-time override via `VITE_EDGE_BASE_URL` (import.meta.env). */
const ENV_EDGE_BASE_URL = import.meta.env.VITE_EDGE_BASE_URL as string | undefined

/** Set to `same-origin` by the Relay container build. */
const RELAY_HOSTED = ENV_EDGE_BASE_URL === 'same-origin'

/** Documented localhost dev URL (see `HumanGateway.Edge/README.md`). */
export const DEFAULT_EDGE_BASE_URL = 'http://localhost:5187'

/** Reads the current Edge base URL (runtime → build-time → default). */
export function getEdgeBaseUrl(): string {
  const runtime = readStorage()
  if (runtime) return normalize(runtime)

  if (RELAY_HOSTED) return ''

  if (ENV_EDGE_BASE_URL) return normalize(ENV_EDGE_BASE_URL)

  return normalize(DEFAULT_EDGE_BASE_URL)
}

/** Persists a runtime Edge base URL override for the current origin. */
export function setEdgeBaseUrl(url: string): void {
  if (typeof localStorage !== 'undefined') {
    localStorage.setItem(EDGE_BASE_URL_STORAGE_KEY, normalize(url))
  }
}

/** Clears the runtime override so resolution falls back to env/default. */
export function clearEdgeBaseUrl(): void {
  if (typeof localStorage !== 'undefined') {
    localStorage.removeItem(EDGE_BASE_URL_STORAGE_KEY)
  }
}

/**
 * Resolves a leading-slash API path against the configured Edge base URL,
 * e.g. `resolveApiUrl('/conversations')` → `http://localhost:5187/conversations`.
 */
export function resolveApiUrl(path: string): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  return `${getEdgeBaseUrl()}${normalizedPath}`
}

function readStorage(): string | null {
  if (typeof localStorage === 'undefined') return null
  try {
    return localStorage.getItem(EDGE_BASE_URL_STORAGE_KEY)
  } catch {
    // Storage can throw in private/blocked contexts; treat as absent.
    return null
  }
}

/** Strips surrounding whitespace and any trailing slashes. */
function normalize(url: string): string {
  return url.trim().replace(/\/+$/, '')
}
