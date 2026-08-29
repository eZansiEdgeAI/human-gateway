# HumanGateway.Core

The durable synchronisation engine at the heart of HumanGateway's store-and-forward guarantee
(product vision §6.2: "sync engine, outbox/inbox, idempotency"). Shared by the Edge Gateway and the
Cloud Relay (product vision §6.3).

- **Target framework:** .NET 10 (LTS)
- **Depends on:** `HumanGateway.Protocol` (entity model + schemas + validation)

## Design

Core algorithms are **pure and deterministic** — no I/O, no clock reads, no randomness in the decision
logic (randomness is injected so property tests can fix a seed). Durable state is read and written only
through the **ports** in this assembly; the SQLite (Edge) and PostgreSQL (Relay) implementations are owned
by the edge/relay engineers.

## Contents

| Area | Types | Requirement |
|------|-------|-------------|
| Sync engine | `ISyncEngine`, `SyncEngine`, `SyncResults` | product vision §6.3 |
| Outbox | `OutboxEntry`, `IOutbox`, `InMemoryOutbox` | EDGE-FR-04, SYNC-FR-01 |
| Inbox | `InboxEntry`, `IInbox`, `InMemoryInbox` | SYNC-FR-01, SYNC-FR-07 |
| Idempotency | `IIdempotencyStore`, `InMemoryIdempotencyStore` | SYNC-FR-02, NF-05 |
| Content hashing | `ContentHasher` | SYNC-FR-02, SP-06 |
| Delivery lifecycle | `DeliveryStateMachine`, `DeliveryAckBuilder` | PROTO-FR-05, product vision §10, SYNC-FR-05 |
| Retry/backoff | `BackoffPolicy` | SYNC-FR-04, EDGE-FR-06 |
| Ordering | `SequenceOrdering` | SYNC-FR-07 |
| Cursor math | `CursorPosition`, `CursorCodec` | SYNC-FR-03 |
| Batch invariants | `BatchSequenceValidator` | schemas/README.md (cross-field checks) |
| Conflict resolution | `FieldMerge` | synchronisation Open Q #1 |

## Key semantics

- **At-least-once delivery + idempotency → exactly-once effect.** Transport is never made exactly-once;
  replayed batches are deduplicated by `(batchId, idempotencyKey)` (NF-05, SYNC-FR-02).
- **Cursor-based incremental sync only.** No full-state resync: a reconnect resumes from the last cursor
  (NF-02, SYNC-FR-03).
- **Out-of-order is normal.** Items are reordered deterministically by `(gatewayId, sequence)`; gaps are
  preserved, never dropped (SYNC-FR-07).
- **`WAITING_FOR_SYNC` is a valid state**, not a failure. The delivery state machine never transitions to
  `FAILED` from it until the retry budget (`maxAttempts`) is exhausted (product vision §10).

## Validation

```bash
dotnet build src/HumanGateway.Core            # library: zero errors
dotnet test tests/HumanGateway.Core.Tests     # property tests: cursor math, idempotency, ordering
```
