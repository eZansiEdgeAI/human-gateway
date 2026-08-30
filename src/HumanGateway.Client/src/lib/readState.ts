/**
 * Client-side read/unread tracking for the Inbox (offline-pwa §4).
 *
 * The protocol's `ConversationView` carries `lastMessageAt` but no per-user
 * read marker (read receipts are a cross-site sync concern, not a local-Edge
 * one), so the PWA tracks "when I last opened this conversation" locally. A
 * conversation is unread when its `lastMessageAt` is newer than that marker.
 *
 * Storage is `localStorage` (per-origin, survives reloads, works offline).
 * ISO-8601 UTC timestamps compare correctly as plain strings, so `>` is a safe
 * unread test.
 */

const PREFIX = 'humangateway.read.'

function key(conversationId: string): string {
  return PREFIX + conversationId
}

/** The last time the user opened this conversation, or `null` if never. */
export function getLastReadAt(conversationId: string): string | null {
  try {
    if (typeof localStorage === 'undefined') return null
    return localStorage.getItem(key(conversationId))
  } catch {
    // Storage can throw in private/blocked contexts; treat as unread-never.
    return null
  }
}

/** Records that the user has read this conversation (optionally at a time). */
export function markRead(conversationId: string, at?: string): void {
  try {
    if (typeof localStorage === 'undefined') return
    localStorage.setItem(key(conversationId), at ?? new Date().toISOString())
  } catch {
    // Non-fatal: read markers are an enhancement, not a correctness guarantee.
  }
}

/** True when a conversation has activity newer than its local read marker. */
export function isConversationUnread(conversation: {
  id: string
  lastMessageAt?: string
}): boolean {
  const lastMessageAt = conversation.lastMessageAt
  if (!lastMessageAt) return false

  const lastReadAt = getLastReadAt(conversation.id)
  return lastReadAt === null || lastMessageAt > lastReadAt
}
