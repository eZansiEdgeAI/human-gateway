# HumanGateway — Product Requirements Document

## 1. Overview

**Product Name:** HumanGateway

**Summary:** HumanGateway is a standalone, offline-first communication platform that connects humans, AI agents, and applications when network connectivity is unreliable or intermittent. It treats communication like email rather than real-time messaging: messages, human tasks, and artifacts are durable, queued locally at the edge, and forwarded when connectivity becomes available. A small **Edge Gateway** runs on low-cost hardware at a site (initially a rural school), serves an offline-capable **Progressive Web App (PWA)** over the local LAN, and synchronises over outbound HTTPS with a cloud **Relay** that acts as a rendezvous point for remote users and workflow systems.

**Target Platform:**
- **Edge Gateway:** .NET/ASP.NET Core service on Linux (Raspberry Pi, old PC) or Windows; SQLite storage; local REST API.
- **Client:** React + TypeScript PWA running in modern mobile/desktop browsers, installable, offline-capable via Service Worker + IndexedDB.
- **Cloud Relay:** ASP.NET Core service; PostgreSQL (message metadata + artifact bytes via BYTEA); containerised (Docker) deployment over HTTPS.
- **First consumer:** FlowForge (an Agent Workforce Platform) — workflow `human-input` / `human-approval` nodes delivered through HumanGateway.

**Key Constraints:**
- Offline-first: assume everyone and everything can be offline; delivery is eventual, durable, recoverable.
- Edge operates fully without Internet; the workflow must not depend on continuous connectivity.
- Low-cost hardware target (Raspberry Pi / old PC / inexpensive Android devices) and low bandwidth.
- School Edge Gateway makes only **outbound** connections — no inbound firewall rules, port forwarding, or public IP at the school.
- HumanGateway is a **standalone reusable communication fabric**, not tied to FlowForge, education, or AI. FlowForge is the first reference consumer.

---

## 2. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-08-28 | forge-build-prd (headless) | Initial PRD from docs/IDEA.md + docs/research/communication-research.md |

---

## 3. Goals and Non-Goals

### 3.1 Goals
- **G1.** Enable reliable human participation in agentic and distributed workflows when the network is unreliable.
- **G2.** Keep the Edge Gateway fully functional offline: local messaging, task delivery, artifact storage, local user access.
- **G3.** Deliver durable store-and-forward semantics: messages survive disconnection for hours or days and converge without loss or duplication.
- **G4.** Provide a transport-agnostic message protocol independent of any implementation language.
- **G5.** Let the workflow engine decide *that* human interaction is required; HumanGateway decides *how* the human is reached and how the response returns.
- **G6.** Support the first reference integration with FlowForge: `human-input` and `human-approval` nodes delivered and returned through HumanGateway.
- **G7.** Run the school Edge Gateway on cheap hardware over local Wi-Fi with no dependency on cloud availability for local operation.
- **G8.** Keep artifacts first-class: messages reference content (images, PDFs, documents, audio) rather than embedding large files.

### 3.2 Non-Goals
- **NG1.** Not a workflow engine: HumanGateway does not own workflow execution, workflow state, human-task semantics, authentication/authorisation of workflow actors, or the audit trail. FlowForge (or another consumer) owns those.
- **NG2.** Not a real-time messaging system: no presence guarantees, no low-latency guarantees, no live chat.
- **NG3.** No SMS, USSD, WhatsApp, or email adapters in the first release (future transport adapters only).
- **NG4.** No AI/LLM features inside HumanGateway.
- **NG5.** Not responsible for deciding whether a human response is valid, whether an actor is authorised for a workflow node, or how the workflow proceeds.
- **NG6.** No direct inbound exposure of the Edge Gateway to the Internet.
- **NG7.** Not a general-purpose file-sync tool: artifact transfer exists to support messaging and human tasks.

---

## 4. User Stories / Personas

### 4.1 Personas

| Persona | Description | Key Needs |
|---------|-------------|-----------|
| Teacher | Works in a rural school with old computers, cheap Android devices, intermittent Internet, low bandwidth. Reviews student work and answers workflow questions. | See and respond to tasks offline; attachments on low bandwidth; simple reliable UI on old hardware. |
| School ICT Admin | Maintains one Edge Gateway (Raspberry Pi or old PC) per school. Not a network expert. | No port forwarding or public IP; gateway self-heals after power loss; easy install and updates. |
| Workflow Developer | Builds FlowForge workflows with human-in-the-loop steps and wants them to work across flaky networks. | Send a human interaction request and get a response back without worrying about transport. |
| Remote Reviewer | An external human (e.g., district coordinator or parent) who needs to respond from outside the school. | Access the same service over the web; responses reach the school workflow. |

### 4.2 User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| US-01 | Teacher | Send and receive messages entirely over the school LAN with no Internet | I can work when connectivity is down | Must |
| US-02 | Teacher | Have my queued messages delivered automatically when Internet returns | Nothing is lost and I don't have to retry manually | Must |
| US-03 | Teacher | Respond to a workflow task and attach a photo or PDF | The assessment agent gets the evidence it needs | Must |
| US-04 | ICT Admin | Power-cycle the gateway with no data loss | An outage doesn't destroy queued messages | Must |
| US-05 | Workflow Developer | Route a FlowForge `human-input` node through HumanGateway | Workflows resume after a delayed, asynchronous human response | Must |
| US-06 | Remote Reviewer | Log in over the web and respond to a task assigned to me | I can participate from outside the school | Should |
| US-07 | Teacher | See delivery status (queued / syncing / delivered / acknowledged) | I trust that my message will arrive | Should |
| US-08 | Teacher | Install the PWA once and use it offline | I don't depend on the browser cache or connectivity | Should |

---

## 5. Research Findings

This section summarises `docs/research/communication-research.md` (authoritative source) and new technology-currency verification. Key findings:

### 5.1 Architecture
- **Three-layer split:** Application/Workflow layer → Human Interaction Fabric (HumanGateway) → Network/Channel layer. The fabric must not assume the application is an AI system, the recipient is a teacher, or the transport is HTTP.
- **Edge-first operation:** the school operates without Internet; the cloud is a relay/rendezvous point, not a dependency for local interaction.
- **Boundary with FlowForge:** FlowForge owns workflow execution, state, human-task semantics, authN/authZ, and audit (its workflows already have `human-input`/`human-approval` nodes and require authenticated, role-checked `Principal`s per ADR-0010). HumanGateway owns delivery, store/forward, routing, artifact transfer, connectivity/sync.
- **Boundary principle:** *"FlowForge decides that a human interaction is required. HumanGateway decides how that human is reached and how the response gets back."*
- **Protocol primitives:** `Participant`, `Message`, `Artifact`, `Delivery` (states QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED), `HumanTask`. Message = communication primitive; Human Task = workflow primitive.

### 5.2 Technology selections and currency (verified 2026-08-28)
| Component | Choice | Verified version | Rationale |
|-----------|--------|------------------|-----------|
| Edge Gateway runtime | .NET / ASP.NET Core (minimal API) | .NET 10 (LTS), SDK 10.0.111 | Cross-platform, runs on Pi/old PC/Windows; strong async I/O for sync workers |
| Edge storage | SQLite (Microsoft.Data.Sqlite) | 10.x (in-box with .NET 10) | Zero-admin embedded store; survives power loss |
| Relay runtime | ASP.NET Core | .NET 10 (LTS) | Shared skills with Edge; container-friendly |
| Relay database | PostgreSQL | 18 (current, supported to 2030-11) | Durable relational store for messages/gateways |
| Relay artifact store | PostgreSQL BYTEA via `ArtifactStore` interface | PG 18 (in-database) | Zero-cost default (no paid storage service); S3-compatible adapter optional later |
| Client UI | React | 19.2.8 | Industry-standard component model |
| Client language | TypeScript | 7.x (native compiler; 5.x line acceptable fallback) | Type safety across protocol types; pin version in Open Questions |
| Client build | Vite + @vitejs/plugin-react | 8.2.2 / 6.1.1 | Fast, PWA-friendly build tooling |
| Client offline | Service Worker + IndexedDB (Workbox or hand-rolled) | standard web platform | Offline cache + local outbox |
| Client tests | Vitest | 4.1.11 | Unit/component testing |
| Development | Docker / Docker Compose (v2) | tooling | Reproducible Edge+Relay+DB environment |
| FlowForge | TypeScript monorepo; `WorkflowRunner` + `PendingHumanTask` (`kind: 'input' | 'approval'`) | current main | Reference consumer; headless `--answers` mode exists for baseline comparison |

### 5.3 Design principles
- Durable synchronisation requires: durable message IDs, sequence numbers, cursors, delivery states, content hashes, idempotent operations.
- Messages reference artifacts by ID/hash rather than embedding content.
- Outbound-only sync from the Edge keeps a clean security boundary and avoids inbound firewall configuration.

---

## 6. Concept

### 6.1 Core Loop / Workflow

**Store-and-forward lifecycle of a message:**
```text
Create Message ─▶ Local Store ─▶ Outbox ─▶ Internet available? ─yes─▶ Relay ─▶ Inbox(recipient) ─▶ ACK
                                          │no
                                          ▼
                                     WAIT (WAITING_FOR_SYNC) ──(connectivity returns)──▶ Relay
```
Connectivity failure results in `WAITING_FOR_SYNC`, never system failure.

**FlowForge human interaction through HumanGateway:**
```text
FlowForge ──HumanInteractionRequested──▶ HumanGateway
        ──▶ School Edge ──▶ Teacher PWA ──(response + optional artifact)──▶ School Edge
        ──(store-and-forward)──▶ Relay ──▶ FlowForge ──▶ Workflow resumes
```

### 6.2 Success / Completion Criteria
From the user's perspective, "done" means: a teacher can send and receive messages and workflow tasks offline over the school LAN, and every message/artifact arrives **exactly once** from the user's perspective — even after power loss, long disconnection, duplication, or out-of-order delivery. A school can disappear from the network for an extended period and, when connectivity returns, the system converges without losing or duplicating messages.

---

## 7. Technical Architecture

### 7.1 Technology Stack
| Component | Technology | Version note |
|-----------|------------|--------------|
| Edge Gateway | ASP.NET Core minimal API | .NET 10 (LTS) |
| Edge storage | SQLite (Microsoft.Data.Sqlite) + local filesystem for artifacts | 10.x |
| Edge sync | Background worker performing outbound HTTPS sync | in-process |
| Relay | ASP.NET Core minimal API | .NET 10 (LTS) |
| Relay storage | PostgreSQL (message metadata + artifact bytes via BYTEA) | PG 18 |
| Client | React + TypeScript PWA (Vite build) | React 19.2.8, TS 7.x, Vite 8.2.2 |
| Client offline | Service Worker + IndexedDB outbox + local cache | platform |
| Protocol | Transport-agnostic JSON schemas (in `schemas/`) | v1 |
| Dev/CI | Docker Compose, Vitest, xUnit/.NET test, chaos scripts | |

### 7.2 Project Structure
```text
human-gateway/
├── docs/                      # PRD, architecture, protocol, security, deployment
├── schemas/                   # message, artifact, participant, delivery, sync, human-task
├── src/
│   ├── HumanGateway.Protocol/ # entity model + schemas + validation
│   ├── HumanGateway.Core/     # sync engine, outbox/inbox, idempotency
│   ├── HumanGateway.Edge/     # Edge Gateway ASP.NET service + SQLite + artifact store
│   ├── HumanGateway.Relay/    # Cloud Relay ASP.NET service + PostgreSQL + object store
│   ├── HumanGateway.Client/   # React/TS PWA
│   └── HumanGateway.Workflow/ # FlowForge integration (provider + CLI bridge)
├── adapters/                  # (future) Sms/ Ussd/ Email/ — stubs only in v1
├── deployment/                # docker/, raspberry-pi/, compose files
└── tests/                     # unit/, integration/, sync/, chaos/
```

### 7.3 Key APIs / Interfaces
| API / Interface | Description |
|-----------------|-------------|
| `Edge Local API` (REST) | LAN endpoints for the PWA: conversations, messages, tasks, artifacts, sync status. |
| `Relay Sync API` (REST/HTTPS) | Outbound sync: gateway registration, push cursor, pull cursor, artifact upload/download, ack. |
| `Human Interaction API` | The FlowForge-side boundary: request human interaction, return human response (+ artifacts), signal expiry. |
| `SyncEngine` (core interface) | Cursor/sequence/idempotency handling shared by Edge and Relay. |
| `HumanInteractionProvider` (FlowForge) | Pluggable mechanism: `ConsoleHumanInteractionProvider` (baseline) and `HumanGatewayInteractionProvider` (reference). |

---

## 8. Functional Requirements

### 8.1 Protocol & Schemas
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Define JSON schemas for `Participant`, `Message`, `Artifact`, `Delivery`, `SyncBatch`, `HumanTask` | Must |
| FR-02 | Participants are typed addresses: `human:`, `agent:`, `system:` | Must |
| FR-03 | Messages are durable envelopes carrying ID, sender, recipient(s), conversation, workflow/task references, payload, attachments, timestamps | Must |
| FR-04 | Artifacts are referenced by ID + hash, never embedded in messages | Must |
| FR-05 | Delivery lifecycle: QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED (+ WAITING_FOR_SYNC) | Must |
| FR-06 | The protocol is language- and transport-independent (JSON over HTTP v1; adapters later) | Must |

### 8.2 Edge Gateway
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-07 | Edge Gateway runs as a local service on Linux (Raspberry Pi, old PC) and Windows | Must |
| FR-08 | Stores all messages/tasks/artifacts in SQLite + local filesystem | Must |
| FR-09 | Exposes a local REST API for PWA clients on the LAN | Must |
| FR-10 | Maintains inbox/outbox with durable local writes before any network attempt | Must |
| FR-11 | Runs a background sync worker that periodically attempts outbound sync to the Relay | Must |
| FR-12 | Supports concurrent local clients synchronising simultaneously | Must |
| FR-13 | Survives process kill / power loss without data loss or duplicate delivery | Must |

### 8.3 Offline PWA Client
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-14 | React/TS PWA installable and fully usable offline (Service Worker + IndexedDB) | Must |
| FR-15 | Local outbox in IndexedDB: messages created offline are queued and pushed to the Edge when reachable | Must |
| FR-16 | Works from the school LAN and, when authenticated, from the Internet via the Relay | Should |
| FR-17 | Supports composing a message with attached artifacts (photo, PDF, document, audio) | Must |
| FR-18 | Displays delivery status per message (queued / syncing / delivered / acknowledged / failed) | Should |
| FR-19 | Supports answering workflow tasks (input and approval) including optional artifact upload | Must |
| FR-20 | Responsive UI usable on inexpensive Android devices and old desktops | Must |

### 8.4 Synchronisation
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-21 | Durable message IDs, per-gateway sequence numbers, cursors, and delivery states | Must |
| FR-22 | Content hashes for every message payload and artifact; idempotent operations on both sides | Must |
| FR-23 | Cursor-based incremental sync both directions (Edge↔Relay) | Must |
| FR-24 | Retry handling with backoff for transient failures; long-disconnect convergence | Must |
| FR-25 | Delivery acknowledgements returned to senders | Must |
| FR-26 | Convergence without loss or duplication after multi-day disconnection | Must |
| FR-27 | Out-of-order message delivery is tolerated and reordered deterministically | Must |

### 8.5 Cloud Relay
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-28 | Relay stores messages/tasks and artifact bytes in PostgreSQL (BYTEA via `ArtifactStore` interface) | Must |
| FR-29 | Relay exposes a sync API that requires no inbound connectivity at the school | Must |
| FR-30 | Gateway registration and rendezvous for remote web access | Must |
| FR-31 | Multiple disconnected schools exchange messages through the cloud | Must |
| FR-32 | Relay is containerised and deployable via Docker Compose for dev/test | Should |

### 8.6 Identity & Security
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-33 | Gateway identity: each Edge Gateway has a unique identity + secret for Relay authentication | Must |
| FR-34 | User identity: local users authenticated at the Edge; remote users authenticated at the Relay | Must |
| FR-35 | Authorisation: participants are restricted to conversations/tasks they are permitted to access | Must |
| FR-36 | Signed requests/tokens for all Edge↔Relay traffic; encryption in transit (TLS) | Must |
| FR-37 | Secure artifact access: downloads authorised per participant/conversation | Must |
| FR-38 | HumanGateway does not duplicate FlowForge's role-checking/audit; it preserves workflow/task correlation tokens for consumers to enforce those | Must |

### 8.7 FlowForge Integration
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-39 | Provide a FlowForge `HumanInteractionProvider` abstraction | Must |
| FR-40 | `HumanGatewayInteractionProvider`: translate FlowForge human interaction requests into HumanGateway messages and responses back into workflow events | Must |
| FR-41 | `ConsoleHumanInteractionProvider` retained as a baseline for comparison (synchronous/headless) | Must |
| FR-42 | Map concepts: `HumanInteractionRequested`, `HumanResponseReceived`, `HumanInteractionCompleted`, `ArtifactReceived`, `HumanInteractionExpired` | Must |
| FR-43 | Support both `human-input` and `human-approval` node kinds, including `PendingHumanTask` correlation (`nodeId`, `role`, `prompt`, `subject`) | Must |
| FR-44 | Expose a CLI/test harness to drive the provider headlessly (`--answers`-style) for comparison with FlowForge's built-in headless mode | Should |

### 8.8 Artifacts
| ID | Requirement | Priority |
|----|-------------|----------|
| FR-45 | Artifact upload/download over sync with content hashing and deduplication | Must |
| FR-46 | Resumable uploads/downloads for large artifacts over low bandwidth | Should |
| FR-47 | Artifact size limits and storage quotas configurable per gateway | Should |

---

## 9. Non-Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| NF-01 | **Performance (Edge):** handles a class-sized school (tens of concurrent clients) on Raspberry Pi-class hardware; local operations served without Internet | Must |
| NF-02 | **Performance (sync):** efficient cursor-based batches; no full-state resync after reconnection | Must |
| NF-03 | **Low bandwidth:** delta/cursor sync and deduplicated artifact transfer keep transfer minimal | Must |
| NF-04 | **Durability:** committed local writes survive process/power failure (SQLite WAL) | Must |
| NF-05 | **Reliability:** at-least-once delivery with idempotency → exactly-once effect for users; convergence after multi-day outage | Must |
| NF-06 | **Maintainability:** monorepo with separated Protocol/Core/Edge/Relay/Client/Workflow projects; shared schemas as source of truth | Must |
| NF-07 | **Accessibility:** WCAG 2.1 AA for the PWA UI (see §11) | Should |
| NF-08 | **Compatibility:** PWA runs on current Chrome/Edge/Firefox/Safari mobile and desktop | Must |
| NF-09 | **Observability:** structured logs on Edge and Relay; sync health surfaced to admins | Should |
| NF-10 | **Operational cost:** default deployment requires no paid cloud services; Relay artifact storage reuses existing PostgreSQL (no object-store dependency) | Must |

---

## 10. Security and Privacy

| ID | Requirement | Priority |
|----|-------------|----------|
| SP-01 | All Edge↔Relay traffic over TLS; school gateway makes outbound connections only | Must |
| SP-02 | Gateway identity via unique ID + secret/registration token; Relay rejects unregistered gateways | Must |
| SP-03 | User authentication at Edge (local) and Relay (remote) with signed tokens/sessions | Must |
| SP-04 | Authorisation enforced per conversation/task/artifact; no cross-participant access | Must |
| SP-05 | Message payloads and artifacts are protected at rest on the Edge; Relay storage encrypted (PG + object store encryption) | Should |
| SP-06 | Content hashes verified on download to detect tamper/corruption | Must |
| SP-07 | No secrets in code or repos; secrets managed via environment/secret stores | Must |
| SP-08 | **Privacy:** the system stores and transmits message content, conversation metadata, human responses, and artifacts. State explicitly: (a) data is processed only to deliver communication; (b) no data is sold or used for advertising; (c) content retention policy is site-controlled (default: keep until deleted by a participant or admin); (d) any compliance regime (e.g., GDPR for personal data, child-data protections) is the responsibility of the deploying site in coordination with consumer systems | Must |
| SP-09 | HumanGateway does not duplicate FlowForge's OIDC identity / role-based authorisation / audit (ADR-0010); it must forward workflow correlation tokens unchanged so consumers enforce authorisation and audit | Must |

---

## 11. Accessibility

| ID | Requirement | Priority |
|----|-------------|----------|
| ACC-01 | PWA targets WCAG 2.1 AA: keyboard navigable, logical focus order, visible focus states | Should |
| ACC-02 | Screen reader support: semantic markup, ARIA labels on interactive controls, alt text for attached media | Should |
| ACC-03 | Sufficient colour contrast (4.5:1 for normal text) and no colour-only status indicators (delivery status also uses icons/text) | Should |
| ACC-04 | Touch targets ≥ 44×44 px for the mobile PWA | Should |

---

## 12. User Interface / Interaction Design

- **Inbox/Outbox view:** conversation list with unread indicators and per-message delivery status (icon + text, not colour alone).
- **Compose view:** recipient selection by participant address, message body, optional artifact attachments (photo via camera/file picker, PDF, document, audio).
- **Task view:** presents a human task question with response type (free text, single/multi choice, approval approve/reject with reason) and optional artifact upload; shows expiry when set.
- **Sync banner:** clear offline/online indicator and a "queued, will sync when connected" message rather than error states.
- **PWA install prompt** and offline-friendly shell (app shell cached by Service Worker).
- Simple, high-contrast, low-bandwidth design; minimal client-side dependencies; works on old Android devices and small screens.
- Wireframes/mockups: none yet — see Open Questions.

---

## 13. System States / Lifecycle

**Message delivery state machine:**
```text
QUEUED ─▶ SYNCING ─▶ DELIVERED ─▶ ACKNOWLEDGED
   │          │
   ▼          ▼
WAITING_FOR_SYNC ─▶ (retry) ─▶ SYNCING ... ─▶ FAILED (after max retries, with alert)
```

**Edge Gateway lifecycle:** `STARTING → STARTED (serving LAN) → SYNCING (outbound to Relay) → RECOVERING (after failure/reboot, reconcile local state) → STOPPING`. On restart the gateway reconciles the local store with Relay cursors before resuming sync.

**PWA lifecycle:** `ONLINE → OFFLINE (service worker serves shell; IndexedDB queues outbox) → RECONNECTING → ONLINE (flush outbox)`. State changes are surfaced to the user via the sync banner.

**Human task lifecycle (consumed by FlowForge):** `REQUESTED → DELIVERED_TO_HUMAN → RESPONSE_RECEIVED → COMPLETED` or `EXPIRED`.

---

## 14. Implementation Phases

### Phase 0: Protocol
- [ ] Define JSON schemas: Message, Artifact, Participant, Delivery, HumanTask, SyncBatch
- [ ] Define the sync model (IDs, sequence numbers, cursors, idempotency, content hashes)
- [ ] Define identity model (gateway, participant, user) and error model
- [ ] Publish schemas under `schemas/` with validation tests
- **Exit:** schemas validated and versioned; a message can be serialised/deserialised round-trip.

### Phase 1: Local Edge
- [ ] Scaffold `HumanGateway.Protocol` and `HumanGateway.Core`
- [ ] Build `HumanGateway.Edge` (ASP.NET Core minimal API, SQLite, local filesystem artifact store)
- [ ] Implement inbox/outbox with durable local writes
- [ ] Implement local REST API
- **Exit:** two devices communicate entirely over a local network with no Internet (message sent, stored, delivered locally, acknowledged).

### Phase 2: Offline PWA
- [ ] Scaffold `HumanGateway.Client` (React + TS + Vite PWA)
- [ ] Service Worker app-shell caching and offline detection
- [ ] IndexedDB local outbox and offline store
- [ ] Compose/send/read UI with delivery status and task answering (input + approval)
- **Exit:** a user can send and receive messages even when the Internet is unavailable.

### Phase 3: Synchronisation
- [ ] Implement the sync protocol (cursors, sequence numbers, idempotency, retry/backoff, ACKs)
- [ ] Background sync worker on the Edge; durable outbox flush on reconnect
- [ ] Convergence and ordering tests (duplication, out-of-order, long disconnects)
- **Exit:** messages survive connectivity loss and eventually reach their destination exactly-once.

### Phase 4: Cloud Relay
- [ ] Scaffold `HumanGateway.Relay` (ASP.NET Core + PostgreSQL; artifact bytes stored as BYTEA)
- [ ] Gateway registration and rendezvous
- [ ] Sync endpoint (push/pull cursors, artifact transfer, ack)
- [ ] Docker Compose environment (Edge + Relay + DB)
- **Exit:** multiple disconnected schools exchange messages through the cloud.

### Phase 5: Identity and Security
- [ ] Gateway identity + registration tokens
- [ ] User identity and authentication at Edge and Relay
- [ ] Authorisation per conversation/task/artifact; signed tokens; TLS everywhere
- [ ] Secure artifact access; content-hash verification
- **Exit:** only authorised participants can read/write conversations and artifacts; gateway-to-Relay traffic is authenticated and encrypted.

### Phase 6: External Web Access
- [ ] Remote authenticated access to the same service via the Relay web endpoint
- [ ] Rendezvous routing from Relay to the school Edge
- **Exit:** an authenticated user outside the school can access their messages/tasks; responses flow back to the workflow.

### Phase 7: FlowForge Reference Integration
- [ ] Add `HumanInteractionProvider` abstraction in the FlowForge integration (`HumanGateway.Workflow`)
- [ ] `HumanGatewayInteractionProvider`: translate requests → messages and responses → workflow events
- [ ] Map `HumanInteractionRequested / HumanResponseReceived / HumanInteractionCompleted / ArtifactReceived / HumanInteractionExpired`
- [ ] CLI/test harness for headless comparison against `ConsoleHumanInteractionProvider`
- **Exit:** a FlowForge `human-input` / `human-approval` node is delivered through HumanGateway and resumes the workflow with the human response and artifacts.

### Phase 8: Additional Channels (future)
- [ ] Design transport-adapter interface (stubs in `adapters/`)
- [ ] (Stretch) SMS / USSD / WhatsApp / Email adapters
- **Exit:** transport adapters can be added without changing the message/workflow model.

---

## 15. Testing Strategy

| Level | Scope | Tools / Approach |
|-------|-------|------------------|
| Unit | Protocol validation, core sync logic, idempotency, delivery state machine | xUnit (Edge/Relay), Vitest (Client) |
| Integration | Edge↔PWA over LAN; Edge↔Relay sync; artifact transfer; FlowForge provider round-trip | Testcontainers / Docker Compose, mock HTTP |
| Sync/chaos | Deliberate network failure scenarios | Chaos scripts controlling connectivity, restarts, reordering, duplication |
| Manual/E2E | Teacher and admin journeys on real browsers/devices | Playwright or exploratory testing |
| Cross-platform | Old desktop + Android browsers; Raspberry Pi target | Manual matrix |

**Key test scenarios (numbered checklist):**
1. Internet disappears mid-session → messages queue, UI shows WAITING_FOR_SYNC.
2. Internet returns → queued messages and artifacts converge exactly-once.
3. Edge Gateway reboots with unsynced outbox → no loss, no duplication after restart.
4. Client (PWA) reboots with offline outbox → outbox flushes on reconnect.
5. Cloud Relay becomes unavailable → Edge keeps operating; sync retries with backoff.
6. Messages duplicated in transit → idempotency deduplicates; user sees exactly one.
7. Messages arrive out of order → deterministic reordering by sequence.
8. Artifact transfer interrupted → resumable transfer completes; hash verified.
9. Device remains offline for several days → long-disconnect convergence.
10. Multiple clients synchronise simultaneously → no lost updates, consistent cursors.
11. FlowForge workflow pauses at `waitingForHuman` → resumes correctly after a delayed response; expiry handled.

**Defining acceptance criterion:** *A school can disappear from the network for an extended period and, when connectivity returns, the system reliably converges without losing or duplicating messages.*

---

## 16. Analytics / Success Metrics

No telemetry or analytics are planned in v1 (privacy-preserving default). Success will be evaluated by:
| Metric | Target | Measurement Method |
|--------|--------|--------------------|
| Offline capability | 100% of Phase 1–2 user stories pass with Internet disabled | Integration/chaos tests |
| Exactly-once delivery | 0 lost / 0 duplicate messages observed in chaos suite | Chaos test assertions |
| Convergence after outage | All messages converge within one sync cycle after reconnect | Sync tests |
| FlowForge round-trip | Human response resumes workflow with artifacts intact | Integration test |
| E2E reliability | Manual E2E suite green on target browsers/Pi | Manual/E2E runs |

---

## 17. Acceptance Criteria

The project is considered complete when all of the following are true:
1. `schemas/` defines and validates all core protocol entities (Message, Artifact, Participant, Delivery, HumanTask, SyncBatch).
2. Two PWA clients can exchange messages and artifacts entirely over a school LAN with no Internet (Phase 1–2 exit criteria met).
3. A user can create, read, and respond to messages and workflow tasks while offline; offline-created messages are queued and delivered later.
4. The Edge and Relay converge without loss or duplication after a multi-day disconnection, gateway reboot, client reboot, message duplication, and out-of-order delivery (chaos suite green).
5. A Relay accepts registered gateways and supports multiple disconnected schools exchanging messages through the cloud.
6. All Edge↔Relay traffic is authenticated (gateway identity), authorised (per-conversation/task/artifact), and encrypted in transit; artifacts are hash-verified.
7. An authenticated remote user can access the service over the web and respond to tasks routed back to the school.
8. The FlowForge `HumanGatewayInteractionProvider` round-trips a `human-input` and a `human-approval` node, returning the human response and any artifacts, and the workflow resumes; expiry is handled.
9. The chaos test suite covering scenarios 1–11 in §15 passes with the defining acceptance criterion met.
10. The PWA meets the accessibility requirements in §11 at WCAG 2.1 AA (target).

---

## 18. Dependencies and Risks

### 18.1 Dependencies
| Dependency | Type | Risk if Unavailable | Mitigation |
|------------|------|---------------------|------------|
| .NET 10 SDK / runtime | runtime | Cannot build/run Edge & Relay | Pin LTS; CI images pinned |
| PostgreSQL 18 | service | Relay store unavailable | Use Docker Compose for dev; containerised prod |
| PostgreSQL artifact bytes | storage | Relay artifact capacity/throughput limited to app-served egress | BYTEA storage (default); S3-compatible `ArtifactStore` adapter available for high-scale deployments |
| React 19 / TypeScript 7 / Vite 8 | npm | Client toolchain issues | Pin versions; TS 5.x fallback if ecosystem lags (Open Q) |
| Service Worker / IndexedDB | platform | Older browsers lack support | Target current Chrome/Edge/Firefox/Safari; feature-detect |
| FlowForge | external repo | Integration surface changes | Depend on published interfaces (`WorkflowRunner`, `PendingHumanTask`); pin commit for integration tests |
| Docker / Docker Compose | dev tooling | Dev environment harder | Provide bare-metal scripts as fallback |

### 18.2 Risks
| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Sync algorithm bugs cause loss/duplication | Medium | High (core value prop) | Schemas-first; property/chaos tests before Relay integration |
| Low-bandwidth artifact sync is too slow | Medium | Medium | Resumable transfer, dedup, size limits; content-hash only-when-changed |
| Edge hardware too weak for concurrent clients | Low | Medium | Load-test on Pi; horizontal tuning; cheap UX |
| FlowForge interface drift during integration | Medium | Medium | Pin FlowForge commit; adapter interface isolates changes |
| PWA offline correctness (service worker cache pitfalls) | Medium | Medium | Versioned caches; explicit cache-busting strategy; chaos/E2E tests |
| Scope creep into workflow/auth/audit territory | Medium | High | Non-goals enforced (§3.2); boundary principle in §5 |
| Security of a device anyone can walk up to | Medium | High | Local authn + TLS; outbound-only; per-conversation authz |

---

## 19. Future Considerations

| Item | Description | Potential Version |
|------|-------------|-------------------|
| SMS / USSD / WhatsApp / Email adapters | Additional transport channels via adapter interface | v2 |
| S3 / Blob artifact store adapter | Optional `ArtifactStore` implementation for high-scale or archival deployments; offloads egress from the app server | v2 |
| Resumable large-artifact streaming | Chunked/resumable transfers fully specified | v2 |
| End-to-end encryption of message content | Protect content from the Relay | v2 |
| Multi-gateway per school / gateway failover | High-availability edge | v3 |
| iOS/Safari-specific PWA hardening | Full offline on iOS | v2 |
| Admin console for gateway health/monitoring | Sync status, quotas, device management | v2 |
| Analytics/telemetry (opt-in) | Success metrics collection | v3 |
| Webhook/event subscription API | Push notifications to consumers beyond FlowForge | v3 |

---

## 20. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Which TypeScript major to pin? | 7.x (native compiler); fall back to 5.x if ecosystem compatibility issues arise |
| 2 | Service Worker strategy: Workbox vs hand-rolled? | Workbox for cache management; hand-rolled only if dependency weight unacceptable |
| 3 | Authentication method for local users at the Edge? | Simple local username+password with signed session tokens (v1); OIDC federation later |
| 4 | Relay artifact storage in production? | PostgreSQL BYTEA (default); S3-compatible adapter optional for high-scale deployments |
| 5 | Deployment target for the Relay? | Containerised via Docker Compose (dev) → any container host (prod); cloud-native later |
| 6 | Should local Edge auth be required in the PoC (Phase 1–2)? | Not required for LAN-only PoC; required from Phase 5 |
| 7 | Max artifact size default? | 50 MB default, configurable per gateway |
| 8 | Message/artifact retention policy default? | Keep until deleted by participant/admin; site-configurable |
| 9 | Regulatory compliance (GDPR, child-data)? | Site + consumer responsibility; HumanGateway provides deletion/export primitives (v1 minimal) |
| 10 | Real-time notification mechanism (WebSocket/polling) for PWA within LAN? | HTTP polling with backoff (v1); WebSocket later |
| 11 | UI mockups/wireframes? | None yet; build from §12 interaction description |
| 12 | FlowForge integration requires changes upstream? | Prefer consumer-only adapter; upstream changes only if unavoidable |

---

## 21. Glossary

| Term | Definition |
|------|------------|
| Edge Gateway | On-site .NET service (Raspberry Pi/old PC) providing local messaging, storage, sync, and the LAN API |
| Cloud Relay | Internet-accessible rendezvous + store for cross-site message delivery and remote access |
| PWA | Progressive Web App: installable React client with Service Worker + IndexedDB offline support |
| Store-and-forward | Durable queuing of messages with eventual delivery when connectivity exists |
| Outbox | Local durable queue of messages waiting for sync/delivery |
| Cursor | Position marker enabling incremental sync |
| Idempotency | Guarantee that processing the same message twice has the effect of processing it once |
| Delivery | Lifecycle state of a message (QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED) |
| Artifact | Content object (image, PDF, document, audio) referenced by a message by ID + hash |
| Participant | Typed address of a sender/recipient (`human:`/`agent:`/`system:`) |
| Human Task | Workflow-level primitive transported by HumanGateway (input or approval) |
| FlowForge | First reference consumer: an Agent Workforce Platform with human-in-the-loop workflows |
| WAITING_FOR_SYNC | State indicating delivery is deferred until connectivity returns (not an error) |
