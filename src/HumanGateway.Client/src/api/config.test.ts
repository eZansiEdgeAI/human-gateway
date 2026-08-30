import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  clearEdgeBaseUrl,
  DEFAULT_EDGE_BASE_URL,
  EDGE_BASE_URL_STORAGE_KEY,
  getEdgeBaseUrl,
  resolveApiUrl,
  setEdgeBaseUrl,
} from './config'

describe('Edge base URL config', () => {
  beforeEach(() => {
    localStorage.clear()
    clearEdgeBaseUrl()
  })

  afterEach(() => {
    localStorage.clear()
    clearEdgeBaseUrl()
  })

  it('defaults to the documented localhost dev URL when nothing is configured', () => {
    expect(getEdgeBaseUrl()).toBe(DEFAULT_EDGE_BASE_URL)
  })

  it('persists and reads back a runtime override, normalising trailing slashes', () => {
    setEdgeBaseUrl('http://edge.school.example:5187///')
    expect(getEdgeBaseUrl()).toBe('http://edge.school.example:5187')
    expect(localStorage.getItem(EDGE_BASE_URL_STORAGE_KEY)).toBe('http://edge.school.example:5187')
  })

  it('clears the runtime override so resolution falls back to the default', () => {
    setEdgeBaseUrl('http://edge.school.example:5187')
    clearEdgeBaseUrl()
    expect(getEdgeBaseUrl()).toBe(DEFAULT_EDGE_BASE_URL)
  })

  it('resolves a leading-slash path against the base URL', () => {
    setEdgeBaseUrl('http://edge.school.example:5187')
    expect(resolveApiUrl('/conversations')).toBe('http://edge.school.example:5187/conversations')
  })

  it('normalises a path without a leading slash', () => {
    setEdgeBaseUrl('http://edge.school.example:5187')
    expect(resolveApiUrl('conversations')).toBe('http://edge.school.example:5187/conversations')
  })
})
