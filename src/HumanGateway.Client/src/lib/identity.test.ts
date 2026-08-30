import { beforeEach, describe, expect, it } from 'vitest'
import {
  getLocalParticipant,
  inferKind,
  parseParticipantAddresses,
  setLocalParticipant,
} from './identity'

describe('inferKind', () => {
  it('infers human for untyped/plain addresses', () => {
    expect(inferKind('teacher@school.example')).toBe('human')
    expect(inferKind('human:teacher@school.example')).toBe('human')
  })

  it('infers agent and system from their prefixes', () => {
    expect(inferKind('agent:assistant@school.example')).toBe('agent')
    expect(inferKind('system:edge@school.example')).toBe('system')
  })
})

describe('parseParticipantAddresses', () => {
  it('splits on commas and whitespace and deduplicates', () => {
    const participants = parseParticipantAddresses(
      'human:a@school.example, agent:b@school.example human:a@school.example',
    )
    expect(participants).toHaveLength(2)
    expect(participants[0].address).toBe('human:a@school.example')
    expect(participants[0].kind).toBe('human')
    expect(participants[1].address).toBe('agent:b@school.example')
  })

  it('defaults the display name to the address', () => {
    const [participant] = parseParticipantAddresses('human:a@school.example')
    expect(participant.displayName).toBe('human:a@school.example')
  })

  it('returns an empty list for blank input', () => {
    expect(parseParticipantAddresses('   ')).toEqual([])
  })
})

describe('getLocalParticipant / setLocalParticipant', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('returns the classroom default when nothing is configured', () => {
    const participant = getLocalParticipant()
    expect(participant.kind).toBe('human')
    expect(participant.address).toBeTruthy()
  })

  it('returns a persisted override', () => {
    setLocalParticipant({ address: 'human:ms@school.example', kind: 'human', displayName: 'Ms. X' })
    expect(getLocalParticipant().address).toBe('human:ms@school.example')
  })
})
