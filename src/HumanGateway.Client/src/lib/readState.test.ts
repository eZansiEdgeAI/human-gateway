import { beforeEach, describe, expect, it } from 'vitest'
import { getLastReadAt, isConversationUnread, markRead } from './readState'

describe('readState', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('treats a never-read conversation with activity as unread', () => {
    expect(isConversationUnread({ id: 'c1', lastMessageAt: '2026-01-01T00:00:00Z' })).toBe(true)
  })

  it('treats a conversation with no messages as read', () => {
    expect(isConversationUnread({ id: 'c1' })).toBe(false)
  })

  it('clears unread once marked read at the latest message time', () => {
    markRead('c1', '2026-01-01T00:00:00Z')
    expect(isConversationUnread({ id: 'c1', lastMessageAt: '2026-01-01T00:00:00Z' })).toBe(false)
  })

  it('stays unread when a newer message arrives after the marker', () => {
    markRead('c1', '2026-01-01T00:00:00Z')
    expect(isConversationUnread({ id: 'c1', lastMessageAt: '2026-01-02T00:00:00Z' })).toBe(true)
  })

  it('records the marker so it survives reads', () => {
    markRead('c1', '2026-01-01T00:00:00Z')
    expect(getLastReadAt('c1')).toBe('2026-01-01T00:00:00Z')
  })
})
