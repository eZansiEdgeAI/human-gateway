/**
 * Presentation formatting helpers shared by the Inbox and Thread views
 * (offline-pwa §4).
 *
 * Pure functions with no React dependency, so they are trivially unit-testable
 * and reusable across views without pulling component concerns in.
 */

import type { ConversationView } from '../types/protocol'

/**
 * A human-readable conversation label: the explicit title when set, otherwise
 * the joined participant display names, otherwise a stable fallback.
 */
export function conversationTitle(conversation: ConversationView): string {
  const title = conversation.title?.trim()
  if (title) return title

  const names = conversation.participants
    .map((participant) => participant.displayName || participant.address)
    .filter((name) => name.length > 0)
  if (names.length > 0) return names.join(', ')

  return 'Conversation'
}

/**
 * A compact relative timestamp ("just now", "5m ago", "3h ago", "2d ago"), then
 * an absolute locale date for anything a week or older. ISO-8601 timestamps
 * compare correctly as strings, but relative rendering needs real time math.
 */
export function formatRelativeTime(iso: string): string {
  const time = new Date(iso).getTime()
  if (Number.isNaN(time)) return ''

  const minutes = Math.floor((Date.now() - time) / 60_000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`

  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`

  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`

  return new Date(time).toLocaleDateString()
}
