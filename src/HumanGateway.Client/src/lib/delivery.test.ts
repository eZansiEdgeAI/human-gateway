import { describe, expect, it } from 'vitest'
import {
  aggregateDeliveryState,
  DELIVERY_STATE_META,
  messageDeliveryState,
} from './delivery'
import type { Delivery, DeliveryState } from '../types/protocol'
import { makeMessageView } from '../test/fixtures'

const ALL_STATES: DeliveryState[] = [
  'QUEUED',
  'SYNCING',
  'DELIVERED',
  'ACKNOWLEDGED',
  'WAITING_FOR_SYNC',
  'FAILED',
]

function delivery(state: DeliveryState): Delivery {
  return {
    id: `dlv-${state}`,
    messageId: 'msg-1',
    recipient: { address: 'human:r@school.example', displayName: 'Recipient' },
    state,
    attempts: 0,
    maxAttempts: 5,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  }
}

describe('DELIVERY_STATE_META', () => {
  it('labels and describes every lifecycle state (icon + text, never colour alone)', () => {
    for (const state of ALL_STATES) {
      expect(DELIVERY_STATE_META[state].label).toBeTruthy()
      expect(DELIVERY_STATE_META[state].description).toBeTruthy()
      expect(DELIVERY_STATE_META[state].tone).toBeTruthy()
    }
  })
})

describe('aggregateDeliveryState', () => {
  it('returns null when there are no deliveries', () => {
    expect(aggregateDeliveryState([])).toBeNull()
  })

  it('returns the single state for one delivery', () => {
    expect(aggregateDeliveryState([delivery('DELIVERED')])).toBe('DELIVERED')
  })

  it('surfaces FAILED over any other state', () => {
    expect(
      aggregateDeliveryState([delivery('ACKNOWLEDGED'), delivery('FAILED'), delivery('DELIVERED')]),
    ).toBe('FAILED')
  })

  it('surfaces SYNCING over completed states', () => {
    expect(aggregateDeliveryState([delivery('DELIVERED'), delivery('SYNCING')])).toBe('SYNCING')
  })

  it('collapses mixed delivered/acknowledged to DELIVERED (least progressed)', () => {
    expect(aggregateDeliveryState([delivery('ACKNOWLEDGED'), delivery('DELIVERED')])).toBe(
      'DELIVERED',
    )
  })
})

describe('messageDeliveryState', () => {
  it('reads the aggregate state from a message view', () => {
    const view = makeMessageView({ deliveries: [delivery('ACKNOWLEDGED')] })
    expect(messageDeliveryState(view)).toBe('ACKNOWLEDGED')
  })

  it('returns null for a message with no deliveries', () => {
    const view = makeMessageView({ deliveries: [] })
    expect(messageDeliveryState(view)).toBeNull()
  })
})
