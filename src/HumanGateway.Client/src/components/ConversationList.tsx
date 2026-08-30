/**
 * Inbox/Outbox view (offline-pwa §4).
 *
 * The conversation list with unread indicators and a per-conversation delivery
 * status summary (derived from each conversation's newest message). Status is
 * rendered icon + text via `DeliveryStatus` — never colour alone (ACC-03).
 * Each row is a real `<button>` so it is keyboard-focusable and tappable
 * (ACC-01, ACC-04).
 */

import type { ConversationView, DeliveryState } from '../types/protocol'
import { isConversationUnread } from '../lib/readState'
import { conversationTitle, formatRelativeTime } from '../lib/format'
import { DeliveryStatus } from './DeliveryStatus'

export interface ConversationListProps {
  conversations: ConversationView[]
  latestDeliveryByConversation: Readonly<Record<string, DeliveryState>>
  onSelectConversation: (id: string) => void
  onComposeNew: () => void
}

export function ConversationList({
  conversations,
  latestDeliveryByConversation,
  onSelectConversation,
  onComposeNew,
}: ConversationListProps) {
  return (
    <section aria-label="Conversations" className="conversation-list">
      <header className="conversation-list__header">
        <h2 className="conversation-list__heading">Inbox</h2>
        <button type="button" className="button button--primary" onClick={onComposeNew}>
          Compose
        </button>
      </header>

      {conversations.length === 0 ? (
        <p className="empty-state">
          No conversations yet. Compose a message to get started.
        </p>
      ) : (
        <ul className="conversation-list__items">
          {conversations.map((conversation) => {
            const unread = isConversationUnread(conversation)
            const status = latestDeliveryByConversation[conversation.id]
            return (
              <li key={conversation.id}>
                <button
                  type="button"
                  className="conversation-item"
                  onClick={() => onSelectConversation(conversation.id)}
                >
                  <span className="conversation-item__main">
                    <span className="conversation-item__title">
                      {conversationTitle(conversation)}
                    </span>
                    <span className="conversation-item__meta">
                      {conversation.messageCount > 0 && (
                        <span className="conversation-item__count">
                          {conversation.messageCount}{' '}
                          {conversation.messageCount === 1 ? 'message' : 'messages'}
                        </span>
                      )}
                      {conversation.lastMessageAt && (
                        <span className="conversation-item__time">
                          {formatRelativeTime(conversation.lastMessageAt)}
                        </span>
                      )}
                    </span>
                    {status && (
                      <span className="conversation-item__status">
                        <DeliveryStatus state={status} />
                      </span>
                    )}
                  </span>
                  {unread && (
                    <span className="conversation-item__unread">
                      <span className="conversation-item__unread-dot" aria-hidden="true" />
                      <span className="visually-hidden">Unread</span>
                    </span>
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
