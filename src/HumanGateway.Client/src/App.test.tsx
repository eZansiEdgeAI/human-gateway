import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import App from './App'

describe('App', () => {
  it('renders the HumanGateway app shell heading', () => {
    render(<App />)
    expect(
      screen.getByRole('heading', { level: 1, name: /humangateway/i }),
    ).toBeInTheDocument()
  })

  it('renders the skip link for keyboard users', () => {
    render(<App />)
    expect(
      screen.getByRole('link', { name: /skip to main content/i }),
    ).toBeInTheDocument()
  })

  it('does not show the sync banner while online', () => {
    render(<App />)
    expect(screen.queryByText(/you're offline/i)).not.toBeInTheDocument()
  })
})
