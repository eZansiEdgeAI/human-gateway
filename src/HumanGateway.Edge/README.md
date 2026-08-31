# HumanGateway.Edge

The on-site Edge Gateway: an ASP.NET Core service serving PWA clients over the school LAN, fully functional
with no Internet, queuing everything for later sync to the Relay (product vision §6.2, EDGE-FR-01..07).

- **Target framework:** .NET 10 (LTS)
- **Depends on:** `HumanGateway.Protocol` (entity model + schemas + validation) and `HumanGateway.Core`
  (sync engine, outbox/inbox, idempotency)

## Status: local REST API + background sync worker skeleton (LOCAL-EDGE-1.6)

This project carries the durable SQLite (WAL) store (EDGE-FR-02): an EF Core schema for conversations,
messages, deliveries, artifacts, and participants, with the durability PRAGMAs (WAL + synchronous=NORMAL +
foreign_keys + busy_timeout) applied on every connection by `SqlitePragmaInterceptor`. Protocol envelopes are
stored as canonical wire JSON alongside denormalised scalar columns for indexed querying.

It also carries the durable inbox/outbox (EDGE-FR-04): `SqliteOutbox`, `SqliteInbox`, and
`SqliteIdempotencyStore` replace the in-memory ports, committing every create to SQLite before any network
attempt. Sequence allocation is a single atomic `INSERT … ON CONFLICT DO UPDATE … RETURNING` per gateway
(concurrency-safe, EDGE-FR-06). A crash-consistency test (`CrashConsistencyTests`) SIGKILLs a write loop
mid-write and verifies committed writes survive a restart exactly once (EDGE-FR-07).

It now also exposes the local REST API the PWA consumes over the LAN (EDGE-FR-03). Endpoints are mapped in
`Endpoints/LocalApiEndpoints.cs` and delegate to `Api/LocalApiService.cs`, which performs the durable
write-then-queue dance for every create (EDGE-FR-04). All responses use the protocol wire contract — camelCase
keys, exact string enum tokens, omit-null — and errors are `ProtocolError`-shaped via the global exception
handler (`ApiErrors.FromException`) so the PWA always receives the stable machine-readable error contract.

It also carries the background sync worker skeleton (`Sync/SyncWorker.cs`, EDGE-FR-05): a hosted
`BackgroundService` that periodically dials out to the Relay (outbound-only, SP-01) through the
`IRelaySyncClient` hook, driving the `ISyncEngine` for push (durable outbox flush — entries marked sent only
after the Relay acks) and pull (inbound apply + delivery-ack enqueue). It walks the product-vision §10
lifecycle (`STARTING → RECOVERING → STARTED → SYNCING → STOPPING`) and retries transient failures with capped,
jittered exponential backoff. The full HTTPS transport arrives with the synchronisation feature; until then the
`DisabledRelaySyncClient` keeps outbound sync off and the durable outbox retains every entry for later sync.

### Local REST API endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/conversations` | List conversations with membership + activity metadata |
| `POST`   | `/conversations` | Create a conversation (upserts participants) |
| `GET`    | `/conversations/{id}` | Get one conversation |
| `GET`    | `/conversations/{id}/messages` | List a conversation's messages (chronological) with deliveries |
| `POST`   | `/messages` | Compose + send a message; durable store + per-recipient delivery + outbox enqueue |
| `GET`    | `/messages/{id}` | Get a message with its delivery records |
| `POST`   | `/tasks` | Create a human task (input/approval); stores task + sends request message |
| `GET`    | `/tasks?status=REQUESTED` | List tasks, optionally filtered by lifecycle state |
| `GET`    | `/tasks/{id}` | Get a task |
| `POST`   | `/tasks/{id}/response` | Answer a task (text / approval decision + reason + artifact refs) |
 | `POST`   | `/artifacts` | Register artifact metadata (bytes land via the artifact store, LOCAL-EDGE-1.5) |
 | `GET`    | `/artifacts` | List artifact metadata |
 | `GET`    | `/artifacts/{id}` | Get artifact metadata |
 | `PUT`    | `/artifacts/{id}/content` | Upload artifact bytes: size-limit + quota enforced, content-hash verified (SP-06), deduplicated (ARTF-FR-01) |
 | `GET`    | `/artifacts/{id}/content` | Download artifact bytes with the artifact's MIME type/filename (Range-capable, ARTF-FR-02) |
 | `GET`    | `/artifacts/{id}/content/status` | Presence + configured size limit + quota usage snapshot (ARTF-FR-03) |
 | `GET`    | `/sync/status` | Sync-status snapshot for the PWA sync banner (queued count, last sequence, delivery-state summary, artifact limits) |

 | Task | What lands here |
 |------|-----------------|
 | LOCAL-EDGE-1.1 | Scaffold: boots the `HumanGateway.Core` sync engine over in-memory ports, health probe, sync-status stub |
 | LOCAL-EDGE-1.2 | **Done** — ASP.NET Core minimal API + SQLite (WAL) schema (conversations, messages, deliveries, artifacts, participants) |
 | LOCAL-EDGE-1.3 | **Done** — Durable SQLite inbox/outbox/idempotency (`SqliteOutbox`/`SqliteInbox`/`SqliteIdempotencyStore`) |
 | LOCAL-EDGE-1.4 | **Done** — Local REST API endpoints (conversations, messages, tasks, artifacts, sync status) |
 | LOCAL-EDGE-1.5 | **Done** — Local filesystem artifact store (content-hash naming, dedup) |
 | LOCAL-EDGE-1.6 | **Done** — Background sync worker skeleton (outbound `IRelaySyncClient` hook; full protocol in synchronisation feature) |
 | ARTF-FR-01/02/03 | **Done** — Artifact byte transfer (ARTF): Edge filesystem + Relay BYTEA stores, authenticated serving endpoints, hash-verified dedup transfer over sync, resumable chunked transfer, configurable limits/quotas |

The `ISyncEngine` contract (Core) is already fixed; swapping the in-memory ports for durable stores requires
no change to the engine or the endpoints in `Program.cs`.

## Run

```bash
dotnet run --project src/HumanGateway.Edge
# http://localhost:5187/healthz       -> {"status":"ok"}
# http://localhost:5187/sync/status   -> {"queued":0,"lastSequence":0}
# http://localhost:5187/conversations -> []
# http://localhost:5187/messages      -> POST to compose + send
```
