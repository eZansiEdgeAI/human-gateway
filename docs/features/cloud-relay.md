# Feature: cloud-relay

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| RELAY-US-01 | US-06 (partial) | Remote Reviewer's task is delivered and its response reaches the school |
| RELAY-FR-01 | FR-28 | Relay stores messages/tasks and artifact bytes in PostgreSQL (BYTEA) |
| RELAY-FR-02 | FR-29 | Relay exposes a sync API that requires no inbound connectivity at the school |
| RELAY-FR-03 | FR-30 | Gateway registration and rendezvous for remote web access |
| RELAY-FR-04 | FR-31 | Multiple disconnected schools exchange messages through the cloud |
| RELAY-FR-05 | FR-32 | Relay is containerised and deployable via Docker Compose for dev/test |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** cloud-relay
**ID Prefix:** RELAY
**Summary:** The cloud side: an ASP.NET Core service backed by PostgreSQL (message metadata + artifact bytes via BYTEA) that acts as a relay and rendezvous point. It accepts outbound sync from Edge Gateways (no inbound connectivity needed at the school) and enables cross-school message exchange and remote web access.
**Dependencies:** protocol, synchronisation
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| RELAY-US-01 | Remote Reviewer | Have my responses reach the school workflow | I can participate from outside the school | Should |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| RELAY-FR-01 | Relay stores messages/tasks and artifact bytes in PostgreSQL (BYTEA) | Must |
| RELAY-FR-02 | Relay exposes a sync API that requires no inbound connectivity at the school | Must |
| RELAY-FR-03 | Gateway registration and rendezvous for remote web access | Must |
| RELAY-FR-04 | Multiple disconnected schools exchange messages through the cloud | Must |
| RELAY-FR-05 | Relay is containerised and deployable via Docker Compose for dev/test | Should |

## 4. UI / Interaction Design

No user-facing UI (service-side).

---

## 5. Implementation Tasks

### Phase 4: Cloud Relay
- [x] Scaffold `src/HumanGateway.Relay` (ASP.NET Core minimal API + EF Core/PostgreSQL)
- [x] PostgreSQL schema: gateways, conversations, messages, deliveries, artifacts, cursors
- [x] Gateway registration + rendezvous endpoints
- [x] Sync endpoint: push/pull cursors, delivery ack (consumes the synchronisation protocol)
- [ ] `ArtifactStore` interface with a PostgreSQL BYTEA implementation (streaming reads; S3-compatible adapter as an optional later step)
- [ ] Docker Compose environment: Relay + PostgreSQL + Edge
- [ ] Structured logging and health endpoint

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Relay store logic, cursor handling | xUnit with test DB |
| Integration | Edge ↔ Relay over HTTP | Testcontainers: PostgreSQL; assert sync round-trip |
| Multi-site | Two Edge Gateways → one Relay | Exchange messages between schools through the cloud |

Key test scenarios:
1. A registered Edge pushes and pulls cursors; messages converge.
2. Two schools exchange messages through the Relay without inbound connections at either site.
3. Relay restart → registered gateways reconnect and resume; no duplication.

---

## 7. Acceptance Criteria

1. Multiple disconnected schools exchange messages through the cloud (Phase 4 exit).
2. The Relay requires only outbound connections from the Edge (no inbound firewall rules at the school).
3. Gateway registration works; unregistered gateways are rejected.
4. The full stack runs via Docker Compose for dev/test.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Relay database migrations? | EF Core Migrations |
| 2 | Relay artifact storage implementation? | PostgreSQL BYTEA via `ArtifactStore` interface |
