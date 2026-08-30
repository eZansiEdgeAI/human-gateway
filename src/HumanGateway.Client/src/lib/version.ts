/**
 * Human-readable application version, surfaced in the UI and used as the
 * human-facing anchor for cache versioning (product vision §12 cache-busting
 * risk).
 *
 * The Service Worker precache is *content-hash* revisioned by Workbox (see
 * `vite.config.ts`): every asset filename embeds a content hash, and
 * `cleanupOutdatedCaches` deletes superseded precache entries on activation,
 * so a stale cache can never strand the app offline. Bump this constant in
 * lockstep with `package.json` `version` whenever a release ships, so
 * operators and support can tell exactly which build a teacher is running
 * offline.
 */
export const APP_VERSION = '0.1.0'
