import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { SyncBanner } from './SyncBanner'

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', {
    value,
    configurable: true,
  })
}

describe('SyncBanner', () => {
  afterEach(() => {
    setOnline(true)
  })

  it('renders nothing while online', () => {
    setOnline(true)
    render(<SyncBanner />)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('announces offline state with text and a decorative dot — never colour alone (ACC-03)', () => {
    setOnline(false)
    render(<SyncBanner />)

    const banner = screen.getByRole('status')
    expect(banner).toHaveTextContent(/offline/i)
    expect(banner).toHaveTextContent(/sync when you're connected/i)

    // The dot is decorative; the accessible text carries the meaning.
    expect(banner.querySelector('.sync-banner__dot')).toHaveAttribute(
      'aria-hidden',
      'true',
    )
  })
})
