/**
 * Compose view (offline-pwa §4, PWA-FR-04).
 *
 * Composes a message with recipient selection by participant address, a body,
 * and optional artifact attachments. Two modes:
 *
 *  - **Reply** (a `conversation` is passed): fully offline-capable — the
 *    message is queued to the outbox and flushed when reachable.
 *  - **New** (no `conversation`): creates a conversation first, which requires
 *    a connection (the Edge assigns the conversation id). Offline new-message
 *    composition is disabled with a calm explanation, matching the sync-banner
 *    philosophy that offline deferral is expected behaviour, never an error.
 */

import { useState, type FormEvent } from 'react'
import type { ConversationView, CreateConversationRequest, SendMessageRequest } from '../types/protocol'
import { getLocalParticipant, parseParticipantAddresses } from '../lib/identity'
import type { SendOutcome } from '../store/context'
import { ArtifactPicker, type PendingAttachment } from './ArtifactPicker'

export interface ComposeMessageProps {
  /** Existing conversation to send into (reply mode). Omit for a new message. */
  conversation?: ConversationView
  online: boolean
  sendMessage: (request: SendMessageRequest) => Promise<SendOutcome>
  createConversation: (request: CreateConversationRequest) => Promise<ConversationView>
  onSent: (outcome: SendOutcome) => void
  onCancel: () => void
}

export function ComposeMessage({
  conversation,
  online,
  sendMessage,
  createConversation,
  onSent,
  onCancel,
}: ComposeMessageProps) {
  const isNew = !conversation
  const sender = getLocalParticipant()

  const [title, setTitle] = useState('')
  const [to, setTo] = useState(() => initialRecipients(conversation))
  const [body, setBody] = useState('')
  const [attachments, setAttachments] = useState<PendingAttachment[]>([])
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setError(null)

    const recipients = parseParticipantAddresses(to)
    if (recipients.length === 0) {
      setError('Enter at least one recipient address.')
      return
    }

    if (body.trim().length === 0 && attachments.length === 0) {
      setError('Write a message or attach a file.')
      return
    }

    const artifactRefs = attachments.map((attachment) => attachment.ref)

    setSending(true)
    try {
      let conversationId = conversation?.id
      if (!conversationId) {
        if (!online) {
          setError(
            "You're offline — new conversations need a connection. Reply to an existing conversation to send offline.",
          )
          return
        }
        const created = await createConversation({
          title: title.trim() || undefined,
          participants: [sender, ...recipients],
        })
        conversationId = created.id
      }

      const outcome = await sendMessage({
        sender,
        recipients,
        conversationId,
        payload: { body, format: 'plaintext' },
        artifactRefs: artifactRefs.length > 0 ? artifactRefs : undefined,
      })
      onSent(outcome)
    } catch {
      setError('Could not send the message. Please try again.')
    } finally {
      setSending(false)
    }
  }

  return (
    <section aria-label={isNew ? 'New message' : 'Reply'} className="compose">
      <header className="compose__header">
        <h2 className="compose__heading">{isNew ? 'New message' : 'Reply'}</h2>
      </header>

      <form onSubmit={handleSubmit} noValidate>
        <div className="compose__field">
          <span className="compose__label">From</span>
          <span className="compose__from">
            {sender.displayName} &lt;{sender.address}&gt;
          </span>
        </div>

        {isNew && (
          <div className="compose__field">
            <label className="compose__label" htmlFor="compose-title">
              Title <span className="compose__optional">(optional)</span>
            </label>
            <input
              id="compose-title"
              type="text"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
            />
          </div>
        )}

        <div className="compose__field">
          <label className="compose__label" htmlFor="compose-to">
            To
          </label>
          <input
            id="compose-to"
            type="text"
            value={to}
            onChange={(event) => setTo(event.target.value)}
            placeholder="human:name@school.example"
            autoComplete="off"
          />
          <span className="compose__hint">
            Participant address(es), comma or space separated.
          </span>
        </div>

        <div className="compose__field">
          <label className="compose__label" htmlFor="compose-body">
            Message
          </label>
          <textarea
            id="compose-body"
            value={body}
            onChange={(event) => setBody(event.target.value)}
            rows={5}
          />
        </div>

        <div className="compose__field">
          <ArtifactPicker attachments={attachments} onAttachmentsChange={setAttachments} />
        </div>

        {error && (
          <p className="compose__error" role="alert">
            {error}
          </p>
        )}

        <div className="compose__actions">
          <button
            type="button"
            className="button button--secondary"
            onClick={onCancel}
            disabled={sending}
          >
            Cancel
          </button>
          <button type="submit" className="button button--primary" disabled={sending}>
            {sending ? 'Sending…' : 'Send'}
          </button>
        </div>
      </form>
    </section>
  )
}

/** Pre-fills the recipient field with the conversation's other participants. */
function initialRecipients(conversation?: ConversationView): string {
  if (!conversation) return ''
  const me = getLocalParticipant().address
  return conversation.participants
    .filter((participant) => participant.address !== me)
    .map((participant) => participant.address)
    .join(', ')
}
