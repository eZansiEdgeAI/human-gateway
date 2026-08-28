# Feature: protocol

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| PROTO-FR-01 | FR-01 | Define JSON schemas for Participant, Message, Artifact, Delivery, SyncBatch, HumanTask |
| PROTO-FR-02 | FR-02 | Participants are typed addresses (human:, agent:, system:) |
| PROTO-FR-03 | FR-03 | Messages are durable envelopes carrying ID, sender, recipients, conversation, workflow/task refs, payload, attachments, timestamps |
| PROTO-FR-04 | FR-04 | Artifacts are referenced by ID + hash, never embedded in messages |
| PROTO-FR-05 | FR-05 | Delivery lifecycle: QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED (+ WAITING_FOR_SYNC) |
| PROTO-FR-06 | FR-06 | Protocol is language- and transport-independent |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** protocol
**ID Prefix:** PROTO
**Summary:** Defines the transport-agnostic message protocol and JSON schemas that every other component validates against. This is the foundation: schemas first, nothing consumes a format without a validating schema.
**Dependencies:** None
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| PROTO-US-01 | Workflow Developer | A stable, versioned protocol across Edge, Relay, and Client | All components interoperate without coupling to a language | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| PROTO-FR-01 | Define JSON schemas for `Participant`, `Message`, `Artifact`, `Delivery`, `SyncBatch`, `HumanTask` under `schemas/` | Must |
| PROTO-FR-02 | Participants are typed addresses: `human:`, `agent:`, `system:` | Must |
| PROTO-FR-03 | Messages are durable envelopes carrying ID, sender, recipient(s), conversation, workflow/task references, payload, attachments, timestamps | Must |
| PROTO-FR-04 | Artifacts are referenced by ID + hash, never embedded in messages | Must |
| PROTO-FR-05 | Delivery lifecycle: QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED (+ WAITING_FOR_SYNC) | Must |
| PROTO-FR-06 | The protocol is language- and transport-independent (JSON over HTTP v1; adapters later) | Must |

## 4. UI / Interaction Design

No UI. Protocol is schema/API-level.

---

## 5. Implementation Tasks

### Phase 0: Protocol
- [ ] Define JSON schemas: Message, Artifact, Participant, Delivery, HumanTask, SyncBatch
- [ ] Define the sync model (IDs, sequence numbers, cursors, idempotency, content hashes)
- [ ] Define the identity model (gateway, participant, user) and error model
- [ ] Publish schemas under `schemas/` with validation tests (JSON Schema validators)
- [ ] Scaffold `src/HumanGateway.Protocol` (.NET) with schema-backed entity model and validation

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Schema validation | JSON Schema validators: valid/invalid fixtures for every entity |
| Unit | Round-trip serialisation | Message/Artifact/HumanTask serialize→deserialize→equal across .NET and TS |
| Compatibility | Cross-implementation | Same JSON fixtures accepted by .NET and TypeScript validators |

Key test scenarios:
1. A valid Message envelope validates and round-trips in both .NET and TS.
2. Invalid envelopes (missing ID, unknown recipient type, hash/size mismatch on Artifact) are rejected.
3. Delivery state transitions follow the allowed lifecycle.

---

## 7. Acceptance Criteria

1. `schemas/` defines and validates all core protocol entities (Message, Artifact, Participant, Delivery, HumanTask, SyncBatch) with a versioned release.
2. .NET (`HumanGateway.Protocol`) and TypeScript validators accept identical fixtures.
3. The delivery state machine and error model are specified and enforced by validation.
4. The sync model (IDs, sequence numbers, cursors, idempotency, content hashes) is specified in the schemas.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | JSON Schema draft version? | Draft 2020-12 |
| 2 | Should the .NET and TS validators be generated from schemas? | Hand-written validators driven by shared schemas (v1); codegen later if needed |
