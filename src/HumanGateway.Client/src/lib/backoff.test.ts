import { describe, expect, it } from 'vitest'
import { createBackoffPolicy } from './backoff'

describe('createBackoffPolicy', () => {
  it('starts at the base interval and resets to it on success', () => {
    const policy = createBackoffPolicy({ baseMs: 1000, maxMs: 10_000, factor: 2 })
    expect(policy.delay()).toBe(1000)

    policy.backoff()
    policy.backoff()
    expect(policy.delay()).toBe(4000)

    policy.reset()
    expect(policy.delay()).toBe(1000)
  })

  it('caps the backoff at maxMs', () => {
    const policy = createBackoffPolicy({ baseMs: 1000, maxMs: 3000, factor: 2 })
    policy.backoff()
    policy.backoff()
    policy.backoff()
    expect(policy.delay()).toBe(3000)
  })
})
