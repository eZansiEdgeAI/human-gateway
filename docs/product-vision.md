# Product Vision: HumanGateway

## 1. Overview

**Product Name:** HumanGateway

**Summary:** HumanGateway is a standalone, offline-first communication platform that connects humans, AI agents, and applications when network connectivity is unreliable or intermittent. It treats communication like email rather than real-time messaging: messages, human tasks, and artifacts are durable, queued locally at the edge, and forwarded when connectivity becomes available. A small **Edge Gateway** runs on low-cost hardware at a site (initially a rural school), serves an offline-capable **Progressive Web App (PWA)** over the local LAN, and synchronises over outbound HTTPS with a cloud **Relay** that acts as a rendezvous point for remote users and workflow systems.

**Target Platform:**
- **Edge Gateway:** .NET/ASP.NET Core service on Linux (Raspberry Pi, old PC) or Windows; SQLite storage; local REST API.
- **Client:** React + TypeScript PWA running in modern mobile/desktop browsers, installable, offline-capable via Service Worker + IndexedDB.
- **Cloud Relay:** ASP.NET Core service; PostgreSQL; object storage; containerised (Docker) deployment over HTTPS.
- **First consumer:** FlowForge (an Agent Workforce Platform) — workflow `human-input` / `human-approval` nodes delivered through HumanGateway.

**Key Constraints:**
- Offline-first: assume everyone and everything can be offline; delivery is eventual, durable, recoverable.
- Edge operates fully without Internet; the workflow must not depend on continuous connectivity.
- Low-cost hardware target (Raspberry Pi / old PC / inexpensive Android devices) and low bandwidth.
- School Edge Gateway makes only **outbound** connections — no inbound firewall rules, port forwarding, or public IP at the school.
- HumanGateway is a **standalone reusable communication fabric**, not tied to FlowForge, education, or AI. FlowForge is the first reference consumer.

**Original PRD:** [docs/PRD.md](PRD.md)

---

## 2. Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-08-28 | forge-decompose-prd (headless) | Initial product vision (decomposed from PRD) |

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

## 4. Personas

| Persona | Description | Key Needs |
|---------|-------------|-----------|
| Teacher | Works in a rural school with old computers, cheap Android devices, intermittent Internet, low bandwidth. Reviews student work and answers workflow questions. | See and respond to tasks offline; attachments on low bandwidth; simple reliable UI on old hardware. |
| School ICT Admin | Maintains one Edge Gateway (Raspberry Pi or old PC) per school. Not a network expert. | No port forwarding or public IP; gateway self-heals after power loss; easy install and updates. |
| Workflow Developer | Builds FlowForge workflows with human-in-the-loop steps and wants them to work across flaky networks. | Send a human interaction request and get a response back without worrying about transport. |
| Remote Reviewer | An external human (e.g., district coordinator or parent) who needs to respond from outside the school. | Access the same service over the web; responses reach the school workflow. |

---

## 5. Research Findings

This section summarises `docs/research/communication-research.md` (authoritative source) and technology-currency verification performed 2026-08-28.

### 5.1 Architecture
- **Three-layer split:** Application/Workflow layer → Human Interaction Fabric (HumanGateway) → Network/Channel layer. The fabric must not assume the application is an AI system, the recipient is a teacher, or the transport is HTTP.
- **Edge-first operation:** the school operates without Internet; the cloud is a relay/rendezvous point, not a dependency for local interaction.
- **Boundary with FlowForge:** FlowForge owns workflow execution, state, human-task semantics, authN/authZ, and audit (its workflows already have `human-input`/`human-approval` nodes and require authenticated, role-checked `Principal`s per ADR-0010). HumanGateway owns delivery, store/forward, routing, artifact transfer, connectivity/sync.
- **Boundary principle:** *"FlowForge decides that a human interaction is required. HumanGateway decides how the human is reached and how the response gets back."*
- **Protocol primitives:** `Participant`, `Message`, `Artifact`, `Delivery` (states QUEUED → SYNCING → DELIVERED → ACKNOWLEDGED → FAILED), `HumanTask`. Message = communication primitive; Human Task = workflow primitive.

### 5.2 Technology selections and currency (verified 2026-08-28)
| Component | Choice | Verified version | Rationale |
|-----------|--------|------------------|-----------|
| Edge Gateway runtime | .NET / ASP.NET Core (minimal API) | .NET 10 (LTS), SDK 10.0.111 | Cross-platform, runs on Pi/old PC/Windows; strong async I/O for sync workers |
| Edge storage | SQLite (Microsoft.Data.Sqlite) | 10.x (in-box with .NET 10) | Zero-admin embedded store; survives power loss |
| Relay runtime | ASP.NET Core | .NET 10 (LTS) | Shared skills with Edge; container-friendly |
| Relay database | PostgreSQL | 18 (current, supported to 2030-11) | Durable relational store for messages/gateways |
| Relay object store | Azure Blob Storage or S3-compatible | n/a (service) | Artifact storage; swappable provider |
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

## 6. Technical Architecture

### 6.1 Technology Stack
| Component | Technology | Version note |
|-----------|------------|--------------|
| Edge Gateway | ASP.NET Core minimal API | .NET 10 (LTS) |
| Edge storage | SQLite (Microsoft.Data.Sqlite) + local filesystem for artifacts | 10.x |
| Edge sync | Background worker performing outbound HTTPS sync | in-process |
| Relay | ASP.NET Core minimal API | .NET 10 (LTS) |
| Relay storage | PostgreSQL + object storage (Azure Blob / S3-compatible) | PG 18 |
| Client | React + TypeScript PWA (Vite build) | React 19.2.8, TS 7.x, Vite 8.2.2 |
| Client offline | Service Worker + IndexedDB outbox + local cache | platform |
| Protocol | Transport-agnostic JSON schemas (in `schemas/`) | v1 |
| Dev/CI | Docker Compose, Vitest, xUnit/.NET test, chaos scripts | |

### 6.2 Project Structure
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

### 6.3 Key APIs / Interfaces
| API / Interface | Description |
|-----------------|-------------|
| `Edge Local API` (REST) | LAN endpoints for the PWA: conversations, messages, tasks, artifacts, sync status. |
| `Relay Sync API` (REST/HTTPS) | Outbound sync: gateway registration, push cursor, pull cursor, artifact upload/download, ack. |
| `Human Interaction API` | The FlowForge-side boundary: request human interaction, return human response (+ artifacts), signal expiry. |
| `SyncEngine` (core interface) | Cursor/sequence/idempotency handling shared by Edge and Relay. |
| `HumanInteractionProvider` (FlowForge) | Pluggable mechanism: `ConsoleHumanInteractionProvider` (baseline) and `HumanGatewayInteractionProvider` (reference). |

---

## 7. Non-Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| NF-01 | **Performance (Edge):** handles a class-sized school (tens of concurrent clients) on Raspberry Pi-class hardware; local operations served without Internet | Must |
| NF-02 | **Performance (sync):** efficient cursor-based batches; no full-state resync after reconnection | Must |
| NF-03 | **Low bandwidth:** delta/cursor sync and deduplicated artifact transfer keep transfer minimal | Must |
| NF-04 | **Durability:** committed local writes survive process/power failure (SQLite WAL) | Must |
| NF-05 | **Reliability:** at-least-once delivery with idempotency → exactly-once effect for users; convergence after multi-day outage | Must |
| NF-06 | **Maintainability:** monorepo with separated Protocol/Core/Edge/Relay/Client/Workflow projects; shared schemas as source of truth | Must |
| NF-07 | **Accessibility:** WCAG 2.1 AA for the PWA UI (see §9) | Should |
| NF-08 | **Compatibility:** PWA runs on current Chrome/Edge/Firefox/Safari mobile and desktop | Must |
| NF-09 | **Observability:** structured logs on Edge and Relay; sync health surfaced to admins | Should |

---

## 8. Security and Privacy

| ID | Requirement | Priority |
|----|-------------|----------|
| SP-01 | All Edge↔Relay traffic over TLS; school gateway makes outbound connections only | Must |
| SP-02 | Gateway identity via unique ID + secret/registration token; Relay rejects unregistered gateways | Must |
| SP-03 | User authentication at Edge (local) and Relay (remote) with signed tokens/sessions | Must |
| SP-04 | Authorisation enforced per conversation/task/artifact; no cross-participant access | Must |
| SP-05 | Message payloads and artifacts are protected at rest on the Edge; Relay storage encrypted (PG + object store encryption) | Should |
| SP-06 | Content hashes verified on download to detect tamper/corruption | Must |
| SP-07 | No secrets in code or repos; secrets managed via environment/secret stores | Must |
| SP-08 | **Privacy:** the system stores and transmits message content, conversation metadata, human responses, and artifacts. Data is processed only to deliver communication; no sale or advertising use; retention policy is site-controlled (default: keep until deleted by a participant or admin); compliance (e.g., GDPR, child-data protections) is the responsibility of the deploying site in coordination with consumer systems | Must |
| SP-09 | HumanGateway does not duplicate FlowForge's OIDC identity / role-based authorisation / audit (ADR-0010); it must forward workflow correlation tokens unchanged so consumers enforce authorisation and audit | Must |

---

## 9. Accessibility

| ID | Requirement | Priority |
|----|-------------|----------|
| ACC-01 | PWA targets WCAG 2.1 AA: keyboard navigable, logical focus order, visible focus states | Should |
| ACC-02 | Screen reader support: semantic markup, ARIA labels on interactive controls, alt text for attached media | Should |
| ACC-03 | Sufficient colour contrast (4.5:1 for normal text) and no colour-only status indicators (delivery status also uses icons/text) | Should |
| ACC-04 | Touch targets ≥ 44×44 px for the mobile PWA | Should |

---

## 10. System States / Lifecycle

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

## 11. Analytics / Success Metrics

No telemetry or analytics are planned in v1 (privacy-preserving default). Success will be evaluated by:
| Metric | Target | Measurement Method |
|--------|--------|--------------------|
| Offline capability | 100% of offline user stories pass with Internet disabled | Integration/chaos tests |
| Exactly-once delivery | 0 lost / 0 duplicate messages observed in chaos suite | Chaos test assertions |
| Convergence after outage | All messages converge within one sync cycle after reconnect | Sync tests |
| FlowForge round-trip | Human response resumes workflow with artifacts intact | Integration test |
| E2E reliability | Manual E2E suite green on target browsers/Pi | Manual/E2E runs |

---

## 12. Dependencies and Risks

### 12.1 Dependencies
| Dependency | Type | Risk if Unavailable | Mitigation |
|------------|------|---------------------|------------|
| .NET 10 SDK / runtime | runtime | Cannot build/run Edge & Relay | Pin LTS; CI images pinned |
| PostgreSQL 18 | service | Relay store unavailable | Use Docker Compose for dev; containerised prod |
| Object storage (Azure Blob / S3) | service | Artifact store unavailable | Provider-agnostic interface; local/MinIO fallback in dev |
| React 19 / TypeScript 7 / Vite 8 | npm | Client toolchain issues | Pin versions; TS 5.x fallback if ecosystem lags (Open Q) |
| Service Worker / IndexedDB | platform | Older browsers lack support | Target current Chrome/Edge/Firefox/Safari; feature-detect |
| FlowForge | external repo | Integration surface changes | Depend on published interfaces (`WorkflowRunner`, `PendingHumanTask`); pin commit for integration tests |
| Docker / Docker Compose | dev tooling | Dev environment harder | Provide bare-metal scripts as fallback |

### 12.2 Risks
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

## 13. Future Considerations

| Item | Description | Potential Version |
|------|-------------|-------------------|
| SMS / USSD / WhatsApp / Email adapters | Additional transport channels via adapter interface | v2 |
| Resumable large-artifact streaming | Chunked/resumable transfers fully specified | v2 |
| End-to-end encryption of message content | Protect content from the Relay | v2 |
| Multi-gateway per school / gateway failover | High-availability edge | v3 |
| iOS/Safari-specific PWA hardening | Full offline on iOS | v2 |
| Admin console for gateway health/monitoring | Sync status, quotas, device management | v2 |
| Analytics/telemetry (opt-in) | Success metrics collection | v3 |
| Webhook/event subscription API | Push notifications to consumers beyond FlowForge | v3 |

---

## 14. Features

Summary of all features decomposed from this product vision:

| # | Feature | File | Dependencies | Priority |
|---|---------|------|-------------|----------|
| 1 | protocol | [docs/features/protocol.md](features/protocol.md) | None | Must |
| 2 | local-edge | [docs/features/local-edge.md](features/local-edge.md) | protocol | Must |
| 3 | offline-pwa | [docs/features/offline-pwa.md](features/offline-pwa.md) | local-edge | Must |
| 4 | synchronisation | [docs/features/synchronisation.md](features/synchronisation.md) | protocol, local-edge | Must |
| 5 | cloud-relay | [docs/features/cloud-relay.md](features/cloud-relay.md) | protocol, synchronisation | Must |
| 6 | artifacts | [docs/features/artifacts.md](features/artifacts.md) | protocol, local-edge, synchronisation, cloud-relay, offline-pwa | Must |
| 7 | identity-security | [docs/features/identity-security.md](features/identity-security.md) | protocol, local-edge, cloud-relay | Must |
| 8 | external-web-access | [docs/features/external-web-access.md](features/external-web-access.md) | identity-security, cloud-relay, offline-pwa | Must |
| 9 | flowforge-integration | [docs/features/flowforge-integration.md](features/flowforge-integration.md) | protocol, cloud-relay, offline-pwa, external-web-access | Must |

### Feature Dependency Graph
```
protocol (foundation)
├── local-edge
│   └── offline-pwa
├── synchronisation
│   └── cloud-relay
├── artifacts (needs protocol, local-edge, synchronisation, cloud-relay, offline-pwa)
└── identity-security (needs protocol, local-edge, cloud-relay)
    └── external-web-access (needs identity-security, cloud-relay, offline-pwa)
        └── flowforge-integration (needs protocol, cloud-relay, offline-pwa, external-web-access)
```

---

## 15. Glossary

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

---

## 16. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Which TypeScript major to pin? | 7.x (native compiler); fall back to 5.x if ecosystem compatibility issues arise |
| 2 | Service Worker strategy: Workbox vs hand-rolled? | Workbox for cache management; hand-rolled only if dependency weight unacceptable |
| 3 | Authentication method for local users at the Edge? | Simple local username+password with signed session tokens (v1); OIDC federation later |
| 4 | Relay object store provider in production? | S3-compatible API (e.g., MinIO for dev); Azure Blob as deployable option |
| 5 | Deployment target for the Relay? | Containerised via Docker Compose (dev) → any container host (prod); cloud-native later |
| 6 | Should local Edge auth be required in the PoC (Phases 1–2)? | Not required for LAN-only PoC; required from Phase 5 (identity-security) |
| 7 | Max artifact size default? | 50 MB default, configurable per gateway |
| 8 | Message/artifact retention policy default? | Keep until deleted by participant/admin; site-configurable |
| 9 | Regulatory compliance (GDPR, child-data)? | Site + consumer responsibility; HumanGateway provides deletion/export primitives (v1 minimal) |
| 10 | Real-time notification mechanism (WebSocket/polling) for PWA within LAN? | HTTP polling with backoff (v1); WebSocket later |
| 11 | UI mockups/wireframes? | None yet; build from interaction description in feature docs |
| 12 | FlowForge integration requires changes upstream? | Prefer consumer-only adapter; upstream changes only if unavoidable |
