---
name: add-sync-endpoint
description: "Adds a cursor-based sync endpoint or worker step to an Edge/Relay service using the shared SyncEngine contract: push/pull cursors, idempotency, retry/backoff, delivery ack. Use this skill when implementing or extending synchronization endpoints on either side of the Edge↔Relay sync loop (SYNC-FR-01..07)."
---

# Skill: Add a Cursor-Based Sync Endpoint / Worker Step

Adds or extends sync capability on the Edge or Relay using the shared `SyncEngine` contract: a push/pull cursor step, idempotency handling, retry/backoff, and delivery acknowledgements (SYNC-FR-01..07).

---

## Process

### Step 1: Identify the Side and Direction

- **Edge → Relay (push):** Edge worker uploads its outbox batch; consumes push cursor from Relay
- **Relay → Edge (pull):** Edge worker pulls new batches; sends delivery acks; consumes pull cursor
- Edge runs the worker in a `BackgroundService` (owned by **edge-engineer**); Relay exposes sync HTTP endpoints (owned by **relay-engineer**). Both drive the **`SyncEngine`** (owned by **sync-engineer**).

### Step 2: Consume the SyncBatch Schema

- Validate request/response payloads against `schemas/sync.schema.json` (Draft 2020-12)
- The batch carries: durable message IDs, per-gateway sequence numbers, cursor(s), content hashes, idempotency keys (SYNC-FR-01/02)
- Coordinate with **protocol-engineer** if the schema needs a new field - never extend the wire format ad hoc

### Step 3: Implement Through the SyncEngine

- Call `SyncEngine` for cursor math, idempotency, and ordering - do NOT reimplement in the endpoint/worker
- Delivery-state transitions must match product vision §10 exactly (QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED, + WAITING_FOR_SYNC)

### Step 4: Add Idempotency + Retry/Backoff

- Idempotency keys so replaying the same batch has no duplicate effect (SYNC-FR-02)
- Exponential backoff with jitter, capped; max retries configurable (sync Open Q #2)
- WAITING_FOR_SYNC for deferred delivery - do NOT mark it FAILED

### Step 5: Return Delivery Acknowledgements

- After processing a pulled batch, the receiving side acks (SYNC-FR-05) so senders learn delivery

---

## Output Format

- Edge: a `BackgroundService` worker step that pushes/pulls and acks through `SyncEngine`
- Relay: a sync HTTP endpoint (push/pull/ack) validating against the SyncBatch schema
- Tests: property tests for cursor math + idempotency; an Edge↔Relay round-trip test

---

## Validation

- [ ] `dotnet build` on the affected project - zero errors
- [ ] `dotnet test` - property tests (cursor math, idempotency, ordering) pass (synchronisation §6)
- [ ] Edge ↔ Relay sync loop round-trips over HTTP (synchronisation §6)
- [ ] Chaos scenario: disconnect → WAITING_FOR_SYNC; reconnect → convergence exactly-once (synchronisation §6)
- [ ] Duplicated batch in transit → deduplicated; user sees exactly one message (SYNC-FR-02)

If validation fails, fix and re-validate.

---

## Gotchas

- **Never full-state resync** - a reconnect resumes from the last cursor (NF-02, SYNC-FR-03). Do not re-pull everything.
- **Out-of-order is normal** - reorder deterministically by sequence number, never drop (SYNC-FR-07).
- **Transport is at-least-once; idempotency gives exactly-once effect** - don't try to make the transport exactly-once (NF-05).
- **Edge is outbound-only** - the Edge never listens for inbound sync; it always dials out to the Relay (SP-01).
- **Unregistered gateways rejected** - the Relay verifies gateway identity on every sync call (SP-02, RELAY-FR-03).
- **`description:` must be double-quoted YAML** in generated files (forge frontmatter gate).

---

## Reference

See [docs/features/synchronisation.md](../../docs/features/synchronisation.md) for the full specification:
- **Section 3** - SYNC-FR-01..07 requirements
- **Section 5** - Phase 3 tasks (batch schema, worker, ack, ordering, convergence)
- **Section 6** - Testing strategy (unit, integration, sync/chaos)
- **Section 8** - Open Questions (conflict resolution, backoff policy)

The `SyncEngine` contract is defined in [docs/product-vision.md](../../docs/product-vision.md) §6.3; the delivery state machine is in §10.
