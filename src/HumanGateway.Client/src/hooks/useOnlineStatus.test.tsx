import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { useOnlineStatus } from './useOnlineStatus'

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', {
    value,
    configurable: true,
  })
}

describe('useOnlineStatus', () => {
  afterEach(() => {
    setOnline(true)
  })

  it('returns true while online', () => {
    setOnline(true)
    const { result } = renderHook(() => useOnlineStatus())
    expect(result.current).toBe(true)
  })

  it('flips to false on the offline event and back to true on the online event', () => {
    setOnline(true)
    const { result } = renderHook(() => useOnlineStatus())

    act(() => {
      setOnline(false)
      window.dispatchEvent(new Event('offline'))
    })
    expect(result.current).toBe(false)

    act(() => {
      setOnline(true)
      window.dispatchEvent(new Event('online'))
    })
    expect(result.current).toBe(true)
  })
})
