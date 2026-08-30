import type { ReactNode } from 'react'
import { APP_VERSION } from '../lib/version'
import { SyncBanner } from './SyncBanner'

/**
 * Installable, offline-friendly app shell cached by the service worker
 * (PWA-FR-01, PWA-FR-07). Provides the persistent header, the sync banner, and
 * the main content region the Inbox/Outbox, Compose, and Task views will mount
 * into in later tasks.
 */
export function AppShell({ children }: { children?: ReactNode }) {
  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <header className="app-header">
        <span className="app-header__brand" aria-hidden="true">
          H
        </span>
        <h1 className="app-header__title">HumanGateway</h1>
      </header>
      <SyncBanner />
      <main id="main-content" className="app-main" tabIndex={-1}>
        {children ?? (
          <div className="placeholder">
            <p>
              Your conversations will appear here. The Inbox, Compose, and Task
              views arrive in the next build step.
            </p>
          </div>
        )}
      </main>
      <footer className="app-footer">
        <p>
          HumanGateway — offline-first messaging for low-connectivity sites.
          <span className="app-footer__version"> v{APP_VERSION}</span>
        </p>
      </footer>
    </div>
  )
}
