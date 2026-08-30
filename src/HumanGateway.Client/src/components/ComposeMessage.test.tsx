import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ComposeMessage } from './ComposeMessage'
import type { SendOutcome } from '../store/context'
import { makeConversationView, makeMessageView } from '../test/fixtures'

function makeOutcome(conversationId: string): SendOutcome {
  return { disposition: 'queued', conversationId, message: makeMessageView() }
}

describe('ComposeMessage', () => {
  it('sends into an existing conversation (reply) with recipient + body', async () => {
    const conversation = makeConversationView({ title: 'Assessment' })
    const sendMessage = vi.fn().mockResolvedValue(makeOutcome(conversation.id))
    const createConversation = vi.fn()
    const onSent = vi.fn()
    const user = userEvent.setup()

    render(
      <ComposeMessage
        conversation={conversation}
        online
        sendMessage={sendMessage}
        createConversation={createConversation}
        onSent={onSent}
        onCancel={() => {}}
      />,
    )

    await user.type(screen.getByLabelText('To'), 'human:a@school.example')
    await user.type(screen.getByLabelText('Message'), 'Hello there')
    await user.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(sendMessage).toHaveBeenCalledOnce())
    expect(createConversation).not.toHaveBeenCalled()
    expect(sendMessage.mock.calls[0][0]).toMatchObject({
      conversationId: conversation.id,
      payload: { body: 'Hello there', format: 'plaintext' },
    })
    expect(onSent).toHaveBeenCalledOnce()
  })

  it('requires a recipient before sending', async () => {
    const sendMessage = vi.fn()
    const user = userEvent.setup()
    render(
      <ComposeMessage
        online
        sendMessage={sendMessage}
        createConversation={vi.fn()}
        onSent={() => {}}
        onCancel={() => {}}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Send' }))
    expect(screen.getByText(/at least one recipient/i)).toBeInTheDocument()
    expect(sendMessage).not.toHaveBeenCalled()
  })

  it('blocks new conversations while offline with a calm explanation', async () => {
    const sendMessage = vi.fn()
    const createConversation = vi.fn()
    const user = userEvent.setup()
    render(
      <ComposeMessage
        online={false}
        sendMessage={sendMessage}
        createConversation={createConversation}
        onSent={() => {}}
        onCancel={() => {}}
      />,
    )

    await user.type(screen.getByLabelText('To'), 'human:a@school.example')
    await user.type(screen.getByLabelText('Message'), 'hi')
    await user.click(screen.getByRole('button', { name: 'Send' }))

    expect(screen.getByText(/offline/i)).toBeInTheDocument()
    expect(createConversation).not.toHaveBeenCalled()
    expect(sendMessage).not.toHaveBeenCalled()
  })
})
