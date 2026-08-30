import { afterEach, describe, expect, it, vi } from 'vitest'
import { HttpError, httpRequest, NetworkError, toProtocolError } from './http'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('http transport', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('returns the parsed JSON body of a successful GET', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([{ id: 'c1' }])))

    await expect(httpRequest<{ id: string }[]>({ url: 'http://edge/conversations', method: 'GET' }))
      .resolves.toEqual([{ id: 'c1' }])
  })

  it('serialises a POST body as JSON with a JSON content type', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'm1' }, 201))
    vi.stubGlobal('fetch', fetchMock)

    await httpRequest({ url: 'http://edge/messages', method: 'POST', body: { body: 'hi' } })

    expect(fetchMock).toHaveBeenCalledWith(
      'http://edge/messages',
      expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ body: 'hi' }),
      }),
    )
  })

  it('throws an HttpError carrying the parsed protocol error for a non-2xx response', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ code: 'NOT_FOUND', message: 'Message nope not found.', retryable: false }, 404),
      ),
    )

    await expect(httpRequest({ url: 'http://edge/messages/nope', method: 'GET' })).rejects.toMatchObject({
      name: 'HttpError',
      status: 404,
      protocolError: { code: 'NOT_FOUND', retryable: false },
    })
  })

  it('falls back to a status-based error when the error body is not JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('boom', { status: 500 })))

    await expect(httpRequest({ url: 'http://edge/messages', method: 'GET' })).rejects.toMatchObject({
      name: 'HttpError',
      status: 500,
      protocolError: { code: 'INTERNAL_ERROR', retryable: true },
    })
  })

  it('maps a transport-level failure to a retryable NetworkError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fetch failed')))

    await expect(httpRequest({ url: 'http://edge/messages', method: 'GET' })).rejects.toMatchObject({
      name: 'NetworkError',
      protocolError: { code: 'NETWORK_UNREACHABLE', retryable: true },
    })
  })

  it('times out an unresponsive request with a retryable TIMEOUT error', async () => {
    vi.useFakeTimers()
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url: RequestInfo | URL, init?: RequestInit) =>
          new Promise((_resolve, reject) => {
            init?.signal?.addEventListener('abort', () =>
              reject(new DOMException('Aborted', 'AbortError')),
            )
          }),
      ),
    )

    const promise = httpRequest({ url: 'http://edge/sync/status', method: 'GET', timeoutMs: 5000 })
    const assertion = expect(promise).rejects.toMatchObject({
      name: 'NetworkError',
      protocolError: { code: 'TIMEOUT', retryable: true },
    })

    await vi.advanceTimersByTimeAsync(5000)
    await assertion
  })

  it('passes HttpError/NetworkError through toProtocolError unchanged', () => {
    const http = new HttpError(404, { code: 'NOT_FOUND', message: 'nope', retryable: false })
    expect(toProtocolError(http)).toEqual({ code: 'NOT_FOUND', message: 'nope', retryable: false })

    const network = new NetworkError({ code: 'TIMEOUT', message: 'slow', retryable: true })
    expect(toProtocolError(network)).toEqual({ code: 'TIMEOUT', message: 'slow', retryable: true })
  })

  it('maps unknown errors to a generic retryable INTERNAL_ERROR', () => {
    expect(toProtocolError(new Error('surprise'))).toMatchObject({
      code: 'INTERNAL_ERROR',
      retryable: true,
    })
  })
})
