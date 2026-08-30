# Feature: synchronisation

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| SYNC-US-01 | US-02 | Teacher's queued messages are delivered automatically when Internet returns |
| SYNC-FR-01 | FR-21 | Durable message IDs, per-gateway sequence numbers, cursors, and delivery states |
| SYNC-FR-02 | FR-22 | Content hashes for every message payload and artifact; idempotent operations on both sides |
| SYNC-FR-03 | FR-23 | Cursor-based incremental sync both directions (Edge↔Relay) |
| SYNC-FR-04 | FR-24 | Retry handling with backoff for transient failures; long-disconnect convergence |
| SYNC-FR-05 | FR-25 | Delivery acknowledgements returned to senders |
| SYNC-FR-06 | FR-26 | Convergence without loss or duplication after multi-day disconnection |
| SYNC-FR-07 | FR-27 | Out-of-order message delivery is tolerated and reordered deterministically |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** synchronisation
**ID Prefix:** SYNC
**Summary:** The durable sync engine at the core of the store-and-forward guarantee: cursor-based incremental sync, idempotency, retry/backoff, acknowledgements, and convergence after long disconnection or out-of-order delivery. This is the engineering heart of the "email-like" behaviour.
**Dependencies:** protocol, local-edge
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| SYNC-US-01 | Teacher | Have my queued messages delivered automatically when Internet returns | Nothing is lost and I don't have to retry manually | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| SYNC-FR-01 | Durable message IDs, per-gateway sequence numbers, cursors, and delivery states | Must |
| SYNC-FR-02 | Content hashes for every message payload and artifact; idempotent operations on both sides | Must |
| SYNC-FR-03 | Cursor-based incremental sync both directions (Edge↔Relay) | Must |
| SYNC-FR-04 | Retry handling with backoff for transient failures; long-disconnect convergence | Must |
| SYNC-FR-05 | Delivery acknowledgements returned to senders | Must |
| SYNC-FR-06 | Convergence without loss or duplication after multi-day disconnection | Must |
| SYNC-FR-07 | Out-of-order message delivery is tolerated and reordered deterministically | Must |

## 4. UI / Interaction Design

No direct UI. Surfaces through the PWA delivery-status indicators (SYNC feature) and the sync banner.

---

## 5. Implementation Tasks

### Phase 3: Synchronisation
- [x] Define the sync batch schema and cursor model in `schemas/syncbatch.schema.json` (with protocol feature)
- [x] Implement the Edge-side sync worker: push/pull cursors, idempotency keys, retry/backoff
- [x] Implement delivery-state transitions and acknowledgements
- [x] Deterministic ordering/reordering by sequence number
- [x] Convergence logic after long disconnects and partial failures
- [ ] Property/chaos tests: duplication, out-of-order, multi-day disconnection

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Cursor math, idempotency keys, ordering | xUnit property tests |
| Integration | Edge ↔ Relay sync loop | Two components over HTTP; controlled interruption |
| Sync/chaos | Network failure scenarios | Chaos scripts: disconnect, duplicate, reorder, long outage |

Key test scenarios:
1. Internet disappears mid-session → messages queue as WAITING_FOR_SYNC.
2. Internet returns → convergence within one sync cycle, exactly-once.
3. Messages duplicated in transit → deduplicated; user sees exactly one.
4. Messages arrive out of order → reordered deterministically.
5. Device offline for several days → long-disconnect convergence.

---

## 7. Acceptance Criteria

1. Messages survive connectivity loss and eventually reach their destination exactly-once (Phase 3 exit).
2. Sync is incremental (cursor-based), with no full-state resync after reconnection.
3. Retries use backoff; transient failures never escalate to data loss.
4. The chaos suite covering duplication, out-of-order, and multi-day outage passes.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Conflict resolution when both sides mutate a conversation? | Last-writer-wins per field, content-hash-verified; documented v1 semantics |
| 2 | Backoff policy? | Exponential backoff with jitter, capped; max retries configurable |
