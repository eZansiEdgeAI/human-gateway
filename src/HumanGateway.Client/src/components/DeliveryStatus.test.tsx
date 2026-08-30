import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DeliveryStatus } from './DeliveryStatus'

describe('DeliveryStatus', () => {
  it('renders the label text (the accessible meaning) for each state', () => {
    const { rerender } = render(<DeliveryStatus state="DELIVERED" />)
    expect(screen.getByText('Delivered')).toBeInTheDocument()

    rerender(<DeliveryStatus state="WAITING_FOR_SYNC" />)
    expect(screen.getByText('Waiting for sync')).toBeInTheDocument()

    rerender(<DeliveryStatus state="FAILED" />)
    expect(screen.getByText('Failed')).toBeInTheDocument()
  })

  it('marks the icon decorative so text carries the meaning (ACC-03)', () => {
    render(<DeliveryStatus state="QUEUED" />)
    const icon = document.querySelector('.delivery-status__icon')
    expect(icon).toHaveAttribute('aria-hidden', 'true')
  })

  it('exposes the description as a hover tooltip', () => {
    render(<DeliveryStatus state="ACKNOWLEDGED" />)
    const status = screen.getByText('Acknowledged').closest('.delivery-status')
    expect(status).toHaveAttribute('title', 'The recipient acknowledged receipt.')
  })
})
