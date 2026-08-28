# Feature: offline-pwa

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| PWA-US-01 | US-03 | Teacher responds to a workflow task and attaches a photo or PDF |
| PWA-US-02 | US-07 | Teacher sees delivery status per message |
| PWA-US-03 | US-08 | Teacher installs the PWA once and uses it offline |
| PWA-FR-01 | FR-14 | React/TS PWA installable and fully usable offline (Service Worker + IndexedDB) |
| PWA-FR-02 | FR-15 | Local outbox in IndexedDB: messages created offline are queued and pushed to the Edge when reachable |
| PWA-FR-03 | FR-16 | Works from the school LAN and, when authenticated, from the Internet via the Relay |
| PWA-FR-04 | FR-17 | Composes a message with attached artifacts (photo, PDF, document, audio) |
| PWA-FR-05 | FR-18 | Displays delivery status per message (queued / syncing / delivered / acknowledged / failed) |
| PWA-FR-06 | FR-19 | Supports answering workflow tasks (input and approval) including optional artifact upload |
| PWA-FR-07 | FR-20 | Responsive UI usable on inexpensive Android devices and old desktops |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** offline-pwa
**ID Prefix:** PWA
**Summary:** The React + TypeScript Progressive Web App teachers use on phones, tablets, and old desktops. Fully usable offline via Service Worker + IndexedDB, with a local outbox, task answering, artifact attachments, and visible delivery status.
**Dependencies:** local-edge
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| PWA-US-01 | Teacher | Respond to a workflow task and attach a photo or PDF | The assessment agent gets the evidence it needs | Must |
| PWA-US-02 | Teacher | See delivery status (queued / syncing / delivered / acknowledged) | I trust that my message will arrive | Should |
| PWA-US-03 | Teacher | Install the PWA once and use it offline | I don't depend on the browser cache or connectivity | Should |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| PWA-FR-01 | React/TS PWA installable and fully usable offline (Service Worker + IndexedDB) | Must |
| PWA-FR-02 | Local outbox in IndexedDB: messages created offline are queued and pushed to the Edge when reachable | Must |
| PWA-FR-03 | Works from the school LAN and, when authenticated, from the Internet via the Relay | Should |
| PWA-FR-04 | Supports composing a message with attached artifacts (photo, PDF, document, audio) | Must |
| PWA-FR-05 | Displays delivery status per message (queued / syncing / delivered / acknowledged / failed) | Should |
| PWA-FR-06 | Supports answering workflow tasks (input and approval) including optional artifact upload | Must |
| PWA-FR-07 | Responsive UI usable on inexpensive Android devices and old desktops | Must |

## 4. UI / Interaction Design

- **Inbox/Outbox view:** conversation list with unread indicators and per-message delivery status (icon + text, not colour alone).
- **Compose view:** recipient selection by participant address, message body, optional artifact attachments (camera/file picker for photo, PDF, document, audio).
- **Task view:** presents a human task question with response type (free text, single/multi choice, approval approve/reject with reason) and optional artifact upload; shows expiry when set.
- **Sync banner:** clear offline/online indicator and "queued, will sync when connected" rather than error states.
- **PWA install prompt** and offline-friendly app shell cached by the Service Worker.
- Targets WCAG 2.1 AA (see product vision §9) and touch targets ≥ 44×44 px.

---

## 5. Implementation Tasks

### Phase 2: Offline PWA
- [ ] Scaffold `src/HumanGateway.Client` (Vite + React + TS PWA, Workbox)
- [ ] Service Worker app-shell caching and offline detection; versioned caches
- [ ] IndexedDB store for conversations, messages, tasks, and local outbox
- [ ] Edge API client with offline-first fetch wrapper (queue to outbox when offline)
- [ ] Inbox/Outbox + Compose + delivery-status UI
- [ ] Task answering UI (input and approval), artifact attachment UI
- [ ] Sync banner / offline indicator
- [ ] Responsive layout for small Android screens and old desktops

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Reducers/stores, outbox queue logic, API client | Vitest + React Testing Library |
| Integration | PWA ↔ Edge over LAN | Playwright; dev server against a running Edge |
| Manual/E2E | Offline flows on real browsers/devices | DevTools offline mode; old Android device matrix |

Key test scenarios:
1. App shell loads from the Service Worker with no network.
2. Composing offline queues to IndexedDB; message appears after reconnect (Edge reachable).
3. Task answered offline with a photo attachment; queued and delivered on reconnect.
4. Delivery status renders correctly through each lifecycle state.

---

## 7. Acceptance Criteria

1. A user can send and receive messages even when the Internet is unavailable (Phase 2 exit).
2. The PWA is installable and its app shell loads offline.
3. Offline-created messages and task responses are queued in IndexedDB and flushed when the Edge is reachable.
4. Task answering (input + approval) with artifact attachment works.
5. UI is usable on an inexpensive Android device and an old desktop browser; accessibility targets met per product vision §9.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | State management library? | React Context + hooks + plain TS modules (v1); no heavy store dependency |
| 2 | Polling for new messages while on LAN? | HTTP polling with backoff (v1); WebSocket later (product vision Open Q #10) |
