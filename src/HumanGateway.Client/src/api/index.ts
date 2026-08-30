/**
 * Edge API client barrel (offline-pwa Phase 2: "Edge API client with
 * offline-first fetch wrapper").
 *
 * Public surface:
 *  - `edgeApi` / `createEdgeApiClient` — typed read + offline-first write API.
 *  - `enqueueWrite` — the low-level offline-first write wrapper.
 *  - `flushOutbox` / `flushEntry` — outbox replay (called on demand and on
 *    reconnect).
 *  - `dispatchOperation` — operation → HTTP translation (test injection point).
 *  - `httpRequest` / error types — the transport.
 *  - config (`getEdgeBaseUrl`, `setEdgeBaseUrl`, `resolveApiUrl`) — Edge origin.
 */

export { edgeApi, createEdgeApiClient } from './client'
export type { EdgeApiClient, EdgeApiClientOptions } from './client'
export { enqueueWrite } from './offlineFirst'
export type { WriteOutcome, WriteOptions } from './offlineFirst'
export { flushOutbox, flushEntry, reconcileSuccess } from './flush'
export type { FlushDeps, FlushEntryOutcome, FlushResult, ReconcileOperation } from './flush'
export { dispatchOperation } from './dispatcher'
export type { DispatchOperation, OperationResult } from './dispatcher'
export { httpRequest, HttpError, NetworkError, toProtocolError, DEFAULT_TIMEOUT_MS } from './http'
export { getEdgeBaseUrl, setEdgeBaseUrl, clearEdgeBaseUrl, resolveApiUrl, DEFAULT_EDGE_BASE_URL } from './config'
