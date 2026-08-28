# Feature: local-edge

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| EDGE-US-01 | US-01 | Teacher sends and receives messages over LAN with no Internet |
| EDGE-US-02 | US-04 | ICT Admin power-cycles the gateway with no data loss |
| EDGE-FR-01 | FR-07 | Edge Gateway runs as a local service on Linux (Raspberry Pi, old PC) and Windows |
| EDGE-FR-02 | FR-08 | Stores all messages/tasks/artifacts in SQLite + local filesystem |
| EDGE-FR-03 | FR-09 | Exposes a local REST API for PWA clients on the LAN |
| EDGE-FR-04 | FR-10 | Maintains inbox/outbox with durable local writes before any network attempt |
| EDGE-FR-05 | FR-11 | Runs a background sync worker that periodically attempts outbound sync to the Relay |
| EDGE-FR-06 | FR-12 | Supports concurrent local clients synchronising simultaneously |
| EDGE-FR-07 | FR-13 | Survives process kill / power loss without data loss or duplicate delivery |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** local-edge
**ID Prefix:** EDGE
**Summary:** The on-site Edge Gateway: a .NET/ASP.NET Core service with SQLite and a local filesystem artifact store, serving a local REST API over the school LAN. It is fully functional with no Internet and queues everything for later sync.
**Dependencies:** protocol
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| EDGE-US-01 | Teacher | Send and receive messages entirely over the school LAN with no Internet | I can work when connectivity is down | Must |
| EDGE-US-02 | ICT Admin | Power-cycle the gateway with no data loss | An outage doesn't destroy queued messages | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| EDGE-FR-01 | Edge Gateway runs as a local service on Linux (Raspberry Pi, old PC) and Windows | Must |
| EDGE-FR-02 | Stores all messages/tasks/artifacts in SQLite + local filesystem | Must |
| EDGE-FR-03 | Exposes a local REST API for PWA clients on the LAN | Must |
| EDGE-FR-04 | Maintains inbox/outbox with durable local writes before any network attempt | Must |
| EDGE-FR-05 | Runs a background sync worker that periodically attempts outbound sync to the Relay | Must |
| EDGE-FR-06 | Supports concurrent local clients synchronising simultaneously | Must |
| EDGE-FR-07 | Survives process kill / power loss without data loss or duplicate delivery | Must |

## 4. UI / Interaction Design

No user-facing UI (service-side). Admin visibility via structured logs and (later) health endpoint.

---

## 5. Implementation Tasks

### Phase 1: Local Edge
- [ ] Scaffold `src/HumanGateway.Core` (outbox/inbox, idempotency, sync engine interface) and `src/HumanGateway.Edge`
- [ ] Build ASP.NET Core minimal API with SQLite (WAL mode) schema for conversations, messages, deliveries, artifacts, participants
- [ ] Implement durable inbox/outbox: every create is committed to SQLite before any network attempt
- [ ] Implement local REST API endpoints: conversations, messages, tasks, artifacts, sync status
- [ ] Local filesystem artifact store with content-hash naming and deduplication
- [ ] Background sync worker skeleton (outbound sync hooks; full protocol in synchronisation feature)
- [ ] Docker/Podman image for the Edge; run script for Raspberry Pi / old PC

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | SQLite store + outbox/inbox logic | xUnit; in-memory/temp DB tests; crash-consistency (kill -9 during write) |
| Integration | Two clients over LAN with no Internet | Spin two PWA/dev clients against one Edge; assert delivery without Relay |
| Chaos | Process kill / power loss | Kill the Edge mid-sync; restart; assert no loss/duplication |

Key test scenarios:
1. Message created → stored durably → delivered to a local recipient with no Internet.
2. Edge killed during a write → restart → message present exactly once.
3. Concurrent local clients send/read simultaneously → consistent results.

---

## 7. Acceptance Criteria

1. Two devices communicate entirely over a local network with no Internet (Phase 1 exit).
2. All messages/tasks/artifacts are durably stored in SQLite + filesystem; committed writes survive process/power loss.
3. The Edge exposes a local REST API consumable by the PWA.
4. Outbox entries survive an Edge restart and are retained for later sync.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | SQLite migration tooling? | EF Core Migrations (in-box with .NET 10) |
| 2 | LAN discovery for PWA (hostname vs IP)? | Documented fixed host/IP config (v1); mDNS later |
