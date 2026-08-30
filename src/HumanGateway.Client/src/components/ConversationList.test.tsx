import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ConversationList } from './ConversationList'
import { makeConversationView } from '../test/fixtures'

function renderList(overrides: Partial<Parameters<typeof ConversationList>[0]> = {}) {
  const props = {
    conversations: [],
    latestDeliveryByConversation: {},
    onSelectConversation: () => {},
    onComposeNew: () => {},
    ...overrides,
  }
  return render(<ConversationList {...props} />)
}

describe('ConversationList', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('renders conversation titles', () => {
    renderList({ conversations: [makeConversationView({ title: 'Assessment', participants: [] })] })
    expect(screen.getByText('Assessment')).toBeInTheDocument()
  })

  it('shows the delivery status summary for a conversation (icon + text)', () => {
    const conversation = makeConversationView({ participants: [] })
    renderList({
      conversations: [conversation],
      latestDeliveryByConversation: { [conversation.id]: 'DELIVERED' },
    })
    expect(screen.getByText('Delivered')).toBeInTheDocument()
  })

  it('shows an unread indicator for a conversation with new activity', () => {
    const conversation = makeConversationView({
      title: 'Assessment',
      participants: [],
      lastMessageAt: '2026-01-01T00:00:00Z',
    })
    renderList({ conversations: [conversation] })
    expect(screen.getByText('Unread')).toBeInTheDocument()
  })

  it('calls onComposeNew when Compose is clicked', async () => {
    const onComposeNew = vi.fn()
    const user = userEvent.setup()
    renderList({ onComposeNew })
    await user.click(screen.getByRole('button', { name: 'Compose' }))
    expect(onComposeNew).toHaveBeenCalledOnce()
  })

  it('calls onSelectConversation with the id when a conversation is clicked', async () => {
    const onSelectConversation = vi.fn()
    const conversation = makeConversationView({ title: 'Assessment', participants: [] })
    const user = userEvent.setup()
    renderList({ conversations: [conversation], onSelectConversation })
    await user.click(screen.getByRole('button', { name: /Assessment/ }))
    expect(onSelectConversation).toHaveBeenCalledWith(conversation.id)
  })
})
