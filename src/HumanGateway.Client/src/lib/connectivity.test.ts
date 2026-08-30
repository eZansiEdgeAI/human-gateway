import { afterEach, describe, expect, it, vi } from 'vitest'
import { isOnline, subscribeConnectivity } from './connectivity'

/** Overrides `navigator.onLine` (jsdom reports a static `true` by default). */
function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', {
    value,
    configurable: true,
  })
}

function fire(name: 'online' | 'offline'): void {
  window.dispatchEvent(new Event(name))
}

describe('connectivity', () => {
  afterEach(() => {
    setOnline(true)
  })

  it('reports online when navigator.onLine is true', () => {
    setOnline(true)
    expect(isOnline()).toBe(true)
  })

  it('reports offline when navigator.onLine is false', () => {
    setOnline(false)
    expect(isOnline()).toBe(false)
  })

  it('invokes the listener immediately with the current state', () => {
    setOnline(false)
    const listener = vi.fn()
    subscribeConnectivity(listener)
    expect(listener).toHaveBeenCalledWith(false)
  })

  it('notifies the listener on offline then online events, and stops after unsubscribe', () => {
    setOnline(true)
    const listener = vi.fn()
    const unsubscribe = subscribeConnectivity(listener)
    listener.mockClear()

    fire('offline')
    expect(listener).toHaveBeenCalledWith(false)

    fire('online')
    expect(listener).toHaveBeenCalledWith(true)

    unsubscribe()
    fire('offline')
    expect(listener).toHaveBeenCalledTimes(2)
  })
})
