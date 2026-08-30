/**
 * Message thread view (offline-pwa §4, PWA-FR-05).
 *
 * Renders a conversation's messages in chronological order, aligning the local
 * participant's messages right ("mine") and others' left ("theirs"). Delivery
 * status is shown on the local participant's own messages — icon + text, never
 * colour alone (ACC-03) — because delivery is the journey of *my* message to
 * its recipients.
 */

import type { MessageView } from '../types/protocol'
import { getLocalParticipant } from '../lib/identity'
import { messageDeliveryState } from '../lib/delivery'
import { formatRelativeTime } from '../lib/format'
import { DeliveryStatus } from './DeliveryStatus'

export interface MessageThreadProps {
  messages: MessageView[]
  /** Address of the local participant; defaults to the configured sender. */
  selfAddress?: string
  onReply: () => void
  onBack: () => void
}

export function MessageThread({ messages, selfAddress, onReply, onBack }: MessageThreadProps) {
  const me = selfAddress ?? getLocalParticipant().address

  return (
    <section aria-label="Conversation" className="message-thread">
      <header className="message-thread__header">
        <button type="button" className="button button--secondary" onClick={onBack}>
          Back
        </button>
        <button type="button" className="button button--primary" onClick={onReply}>
          Reply
        </button>
      </header>

      {messages.length === 0 ? (
        <p className="empty-state">No messages yet. Reply to start the conversation.</p>
      ) : (
        <ol className="message-thread__list">
          {messages.map((view) => (
            <MessageItem key={view.message.id} view={view} mine={view.message.sender.address === me} />
          ))}
        </ol>
      )}
    </section>
  )
}

function MessageItem({ view, mine }: { view: MessageView; mine: boolean }) {
  const { message } = view
  const status = messageDeliveryState(view)

  return (
    <li className={`message message--${mine ? 'mine' : 'theirs'}`}>
      <div className="message__bubble">
        <span className="message__sender">{message.sender.displayName}</span>
        <p className="message__body">{message.payload.body}</p>
        {message.artifactRefs && message.artifactRefs.length > 0 && (
          <ul className="message__artifacts" aria-label="Attachments">
            {message.artifactRefs.map((ref) => (
              <li key={ref.id}>{ref.filename ?? ref.id}</li>
            ))}
          </ul>
        )}
        <span className="message__meta">
          <time dateTime={message.createdAt}>{formatRelativeTime(message.createdAt)}</time>
          {mine && <DeliveryStatus state={status} />}
        </span>
      </div>
    </li>
  )
}
