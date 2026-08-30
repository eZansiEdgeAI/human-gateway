import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { MessageThread } from './MessageThread'
import { makeMessageView } from '../test/fixtures'

describe('MessageThread', () => {
  it('renders message bodies in chronological order', () => {
    const view = makeMessageView()
    render(<MessageThread messages={[view]} onReply={() => {}} onBack={() => {}} />)
    expect(screen.getByText(view.message.payload.body)).toBeInTheDocument()
  })

  it('shows delivery status on my own messages', () => {
    const view = makeMessageView()
    render(
      <MessageThread
        messages={[view]}
        selfAddress={view.message.sender.address}
        onReply={() => {}}
        onBack={() => {}}
      />,
    )
    expect(screen.getByText('Queued')).toBeInTheDocument()
  })

  it('does not show delivery status on others messages', () => {
    const view = makeMessageView()
    render(
      <MessageThread
        messages={[view]}
        selfAddress="human:someone-else@school.example"
        onReply={() => {}}
        onBack={() => {}}
      />,
    )
    expect(screen.queryByText('Queued')).not.toBeInTheDocument()
  })

  it('renders "Not sent" for my own message with no delivery records yet', () => {
    const view = makeMessageView({ deliveries: [] })
    render(
      <MessageThread
        messages={[view]}
        selfAddress={view.message.sender.address}
        onReply={() => {}}
        onBack={() => {}}
      />,
    )
    expect(screen.getByText('Not sent')).toBeInTheDocument()
  })

  it('calls onReply and onBack', async () => {
    const onReply = vi.fn()
    const onBack = vi.fn()
    const user = userEvent.setup()
    render(<MessageThread messages={[]} onReply={onReply} onBack={onBack} />)

    await user.click(screen.getByRole('button', { name: 'Reply' }))
    await user.click(screen.getByRole('button', { name: 'Back' }))

    expect(onReply).toHaveBeenCalledOnce()
    expect(onBack).toHaveBeenCalledOnce()
  })
})
