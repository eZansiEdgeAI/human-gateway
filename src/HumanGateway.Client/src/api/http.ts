/**
 * Thin HTTP transport for the Edge local REST API (EDGE-FR-03).
 *
 * The only module that touches `fetch`. It serialises JSON request bodies,
 * parses JSON responses, and maps every failure — HTTP status or network-level —
 * into the protocol error model so callers above it (the dispatcher, the
 * offline-first wrapper, and the flush worker) only ever reason about
 * {@link ProtocolError}, never about raw `fetch` failure modes.
 *
 * The Edge returns `ProtocolError`-shaped bodies on non-2xx (see
 * `HumanGateway.Edge/Api/ApiErrors.cs`); this module parses those faithfully
 * and falls back to a generic status-based error when the body is not JSON.
 */

import type { ProtocolError } from '../types/protocol'
import { getAuthSession } from '../auth/session'

/** Default request timeout before a request is treated as unreachable. */
export const DEFAULT_TIMEOUT_MS = 10_000

/** Client-local error code for a request that never reached the Edge. */
export const NETWORK_UNREACHABLE = 'NETWORK_UNREACHABLE'

/**
 * An HTTP response in the non-2xx range, carrying the parsed
 * {@link ProtocolError} body when one was present.
 */
export class HttpError extends Error {
  readonly status: number
  readonly protocolError: ProtocolError

  constructor(status: number, protocolError: ProtocolError) {
    super(protocolError.message)
    this.name = 'HttpError'
    this.status = status
    this.protocolError = protocolError
  }
}

/**
 * A transport-level failure: the request never received an HTTP response
 * (connection refused, DNS failure, or timeout). Always retryable.
 */
export class NetworkError extends Error {
  readonly protocolError: ProtocolError

  constructor(protocolError: ProtocolError) {
    super(protocolError.message)
    this.name = 'NetworkError'
    this.protocolError = protocolError
  }
}

export type HttpMethod = 'GET' | 'POST'

export interface HttpRequestInit {
  /** Fully-resolved URL (use {@link resolveApiUrl} from `./config`). */
  url: string
  method: HttpMethod
  /** Request body; serialised as JSON and sent with a JSON content type. */
  body?: unknown
  /** Abort timeout in milliseconds (defaults to {@link DEFAULT_TIMEOUT_MS}). */
  timeoutMs?: number
  /** External abort signal (e.g. React effect cleanup). */
  signal?: AbortSignal
}

/**
 * Performs an HTTP request and returns the parsed JSON response. Throws
 * {@link HttpError} for non-2xx responses and {@link NetworkError} for
 * transport failures.
 */
export async function httpRequest<T>(init: HttpRequestInit): Promise<T> {
  const { url, method, body, timeoutMs = DEFAULT_TIMEOUT_MS, signal } = init

  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeoutMs)
  const onExternalAbort = () => controller.abort()
  signal?.addEventListener('abort', onExternalAbort)

  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers: {
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(getAuthSession()?.token ? { Authorization: `Bearer ${getAuthSession()!.token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    })
  } catch (error) {
    // Distinguish a self-imposed timeout from an external abort: when the
    // caller aborted, let the underlying AbortError propagate rather than
    // relabel it as a timeout.
    if (controller.signal.aborted && !signal?.aborted) {
      throw new NetworkError({
        code: 'TIMEOUT',
        message: `Request to ${url} timed out after ${timeoutMs}ms.`,
        retryable: true,
      })
    }
    throw new NetworkError({
      code: NETWORK_UNREACHABLE,
      message: error instanceof Error ? error.message : 'Network request failed.',
      retryable: true,
    })
  } finally {
    clearTimeout(timer)
    signal?.removeEventListener('abort', onExternalAbort)
  }

  if (!response.ok) {
    throw new HttpError(response.status, await parseProtocolError(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

/**
 * Maps any thrown error to a {@link ProtocolError}. Pass-through for
 * {@link HttpError}/{@link NetworkError}; a generic internal error otherwise.
 */
export function toProtocolError(error: unknown): ProtocolError {
  if (error instanceof HttpError || error instanceof NetworkError) {
    return error.protocolError
  }
  return {
    code: 'INTERNAL_ERROR',
    message: error instanceof Error ? error.message : 'An unknown error occurred.',
    retryable: true,
  }
}

async function parseProtocolError(response: Response): Promise<ProtocolError> {
  try {
    const body: unknown = await response.json()
    if (isProtocolError(body)) {
      return {
        code: body.code,
        message: body.message,
        details: body.details,
        retryable: body.retryable,
      }
    }
  } catch {
    // Non-JSON error body — fall through to the status-based default.
  }
  return {
    code: 'INTERNAL_ERROR',
    message: `Request failed with status ${response.status}.`,
    retryable: response.status >= 500,
  }
}

function isProtocolError(value: unknown): value is ProtocolError {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  return typeof candidate.code === 'string' && typeof candidate.message === 'string'
}
