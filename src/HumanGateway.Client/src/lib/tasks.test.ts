import { describe, expect, it } from 'vitest'
import {
  formatExpiry,
  isOpenTask,
  isTaskExpired,
  taskKindLabel,
  taskResponseType,
  taskStatusLabel,
} from './tasks'
import { makeHumanTask } from '../test/fixtures'

describe('taskResponseType', () => {
  it('is approval for kind=approval', () => {
    expect(taskResponseType(makeHumanTask({ kind: 'approval' }))).toBe('approval')
  })

  it('is choice for input tasks with options', () => {
    expect(taskResponseType(makeHumanTask({ kind: 'input', options: ['a', 'b'] }))).toBe('choice')
  })

  it('is freeText for input tasks without options', () => {
    expect(taskResponseType(makeHumanTask({ kind: 'input', options: undefined }))).toBe('freeText')
  })

  it('treats an empty options array as freeText', () => {
    expect(taskResponseType(makeHumanTask({ kind: 'input', options: [] }))).toBe('freeText')
  })
})

describe('isOpenTask', () => {
  it('is true for REQUESTED and DELIVERED_TO_HUMAN', () => {
    expect(isOpenTask(makeHumanTask({ status: 'REQUESTED' }))).toBe(true)
    expect(isOpenTask(makeHumanTask({ status: 'DELIVERED_TO_HUMAN' }))).toBe(true)
  })

  it('is false once answered, completed, or expired', () => {
    expect(isOpenTask(makeHumanTask({ status: 'RESPONSE_RECEIVED' }))).toBe(false)
    expect(isOpenTask(makeHumanTask({ status: 'COMPLETED' }))).toBe(false)
    expect(isOpenTask(makeHumanTask({ status: 'EXPIRED' }))).toBe(false)
  })
})

describe('isTaskExpired', () => {
  it('is true for EXPIRED regardless of expiresAt', () => {
    expect(isTaskExpired(makeHumanTask({ status: 'EXPIRED' }), 0)).toBe(true)
  })

  it('is true once expiresAt has passed', () => {
    const task = makeHumanTask({ status: 'REQUESTED', expiresAt: '2026-01-01T00:00:00Z' })
    expect(isTaskExpired(task, Date.parse('2026-02-01T00:00:00Z'))).toBe(true)
  })

  it('is false before expiresAt', () => {
    const task = makeHumanTask({ status: 'REQUESTED', expiresAt: '2026-02-01T00:00:00Z' })
    expect(isTaskExpired(task, Date.parse('2026-01-01T00:00:00Z'))).toBe(false)
  })

  it('is false with no expiry', () => {
    expect(isTaskExpired(makeHumanTask({ status: 'REQUESTED' }), 0)).toBe(false)
  })
})

describe('labels', () => {
  it('labels approval vs input kinds', () => {
    expect(taskKindLabel('approval')).toBe('Approval')
    expect(taskKindLabel('input')).toBe('Input')
    expect(taskKindLabel(undefined)).toBe('Input')
  })

  it('labels each status', () => {
    expect(taskStatusLabel('REQUESTED')).toBe('Requested')
    expect(taskStatusLabel('DELIVERED_TO_HUMAN')).toBe('Open')
    expect(taskStatusLabel('RESPONSE_RECEIVED')).toBe('Answered')
    expect(taskStatusLabel('COMPLETED')).toBe('Completed')
    expect(taskStatusLabel('EXPIRED')).toBe('Expired')
    expect(taskStatusLabel(undefined)).toBe('Unknown')
  })
})

describe('formatExpiry', () => {
  it('is empty when absent or invalid', () => {
    expect(formatExpiry()).toBe('')
    expect(formatExpiry('not-a-date')).toBe('')
  })

  it('returns a non-empty locale string for a valid timestamp', () => {
    expect(formatExpiry('2026-01-01T00:00:00Z')).not.toBe('')
  })
})
