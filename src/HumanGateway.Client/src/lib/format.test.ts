import { describe, expect, it } from 'vitest'
import { conversationTitle, formatRelativeTime } from './format'
import { makeConversationView } from '../test/fixtures'

describe('conversationTitle', () => {
  it('prefers an explicit title', () => {
    const conversation = makeConversationView({ title: '  Assessment  ' })
    expect(conversationTitle(conversation)).toBe('Assessment')
  })

  it('falls back to joined participant display names', () => {
    const conversation = makeConversationView({
      title: undefined,
      participants: [
        { address: 'human:a@school.example', displayName: 'Alice' },
        { address: 'agent:b@school.example', displayName: 'Bob' },
      ],
    })
    expect(conversationTitle(conversation)).toBe('Alice, Bob')
  })

  it('falls back to a stable label when there is nothing else', () => {
    expect(conversationTitle(makeConversationView({ title: undefined, participants: [] }))).toBe(
      'Conversation',
    )
  })
})

describe('formatRelativeTime', () => {
  it('renders just now for the present', () => {
    expect(formatRelativeTime(new Date().toISOString())).toBe('just now')
  })

  it('renders minutes for under an hour ago', () => {
    const fiveMinutesAgo = new Date(Date.now() - 5 * 60_000).toISOString()
    expect(formatRelativeTime(fiveMinutesAgo)).toBe('5m ago')
  })

  it('returns empty for an unparseable timestamp', () => {
    expect(formatRelativeTime('not-a-date')).toBe('')
  })
})
