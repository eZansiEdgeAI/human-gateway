# Feature: external-web-access

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| WEBX-US-01 | US-06 | Remote Reviewer accesses the same service over the web and responds from outside the school |
| WEBX-FR-01 | PWA-FR-03 (remote part) | The PWA works over the Internet, authenticated via the Relay, in addition to the school LAN |
| WEBX-FR-02 | RELAY-FR-03 (rendezvous part) | Rendezvous routing from Relay to the school Edge for remote users |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** external-web-access
**ID Prefix:** WEBX
**Summary:** Lets authenticated users outside the school access the same service over the web. The Relay acts as the rendezvous point: external requests are routed to the school Edge via the existing outbound sync path — the Edge Gateway is never directly exposed to the Internet.
**Dependencies:** identity-security, cloud-relay, offline-pwa
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| WEBX-US-01 | Remote Reviewer | Log in over the web and respond to a task assigned to me | I can participate from outside the school | Should |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| WEBX-FR-01 | The PWA works from the Internet when authenticated via the Relay (in addition to the school LAN) | Should |
| WEBX-FR-02 | Rendezvous routing from the Relay to the school Edge for remote users; Edge remains outbound-only | Must |

## 4. UI / Interaction Design

Remote users use the same PWA surface; a Relay-hosted entry point and remote login gate (from identity-security). Sync/route states remain visible; no inbound connectivity at the school.

---

## 5. Implementation Tasks

### Phase 6: External Web Access
- [x] Relay-hosted web entry point for the PWA
- [x] Rendezvous routing: map remote participant → school Edge → deliver via outbound sync channel
- [x] Remote login integration (uses identity-security)
- [x] Responses from remote users flow back to the school and, via correlation tokens, to the workflow consumer
- [x] End-to-end test: external user responds; school Edge + workflow receive it

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Integration | External user → Relay → Edge | End-to-end over HTTP; assert response reaches the school workflow |
| Security | Unauthenticated/unauthorised remote access | Denied; only authorised participants see their tasks |
| Chaos | Edge offline while external user submits | Response queued at Relay; delivered when Edge reconnects |

Key test scenarios:
1. An authenticated external user views and answers a task; the response reaches the school Edge.
2. Unauthenticated remote access is rejected.
3. If the Edge is offline, the external response queues and delivers on reconnect (store-and-forward).

---

## 7. Acceptance Criteria

1. An authenticated user outside the school accesses their messages/tasks over the web (Phase 6 exit).
2. Responses reach the school Edge (and the workflow consumer) via the outbound sync path.
3. The Edge Gateway is never directly exposed to the Internet.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Relay web entry: separate URL vs same PWA build? | Same PWA build served by the Relay with remote auth (product vision Open Q #5) |
