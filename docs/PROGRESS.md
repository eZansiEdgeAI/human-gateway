# Project Progress

## Current State
**Phase**: EXTERNAL-WEB-ACCESS-6
**Status**: In Progress
**Last Updated**: 2026-09-02T20:33:01.259Z
**Run ID**: cc1dad89-27a5-4765-8655-91bea28ec9c3
**Harness**: opencode

## Completed Tasks
- [x] Phase PROTOCOL-0, Task PROTOCOL-0.1: [x] Define JSON schemas (@protocol-engineer)
- [x] Phase PROTOCOL-0, Task PROTOCOL-0.2: [x] Define the sync model (IDs, sequence numbers, cursors, idempotency, content hashes) (@protocol-engineer)
- [x] Phase PROTOCOL-0, Task PROTOCOL-0.3: [x] Define the identity model (gateway, participant, user) and error model (@security-engineer)
- [x] Phase PROTOCOL-0, Task PROTOCOL-0.4: [x] Publish schemas under `schemas/` with validation tests (JSON Schema validators) (@protocol-engineer)
- [x] Phase PROTOCOL-0, Task PROTOCOL-0.5: [ ] Scaffold `src/HumanGateway (@protocol-engineer)
  - Files: src/HumanGateway.Protocol
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.1: [ ] Scaffold `src/HumanGateway (@sync-engineer)
  - Files: src/HumanGateway.Core, src/HumanGateway.Edge
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.2: [ ] Build ASP (@edge-engineer)
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.3: [ ] Implement durable inbox/outbox (@edge-engineer)
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.4: [ ] Implement local REST API endpoints (@edge-engineer)
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.5: [ ] Local filesystem artifact store with content-hash naming and deduplication (@edge-engineer)
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.6: [ ] Background sync worker skeleton (outbound sync hooks; full protocol in synchronisation feature) (@edge-engineer)
- [x] Phase LOCAL-EDGE-1, Task LOCAL-EDGE-1.7: [ ] Docker/Podman image for the Edge; run script for Raspberry Pi / old PC (@infrastructure-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.1: [ ] Scaffold `src/HumanGateway (@pwa-engineer)
  - Files: src/HumanGateway.Client
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.2: [ ] Service Worker app-shell caching and offline detection; versioned caches (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.3: [ ] IndexedDB store for conversations, messages, tasks, and local outbox (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.4: [ ] Edge API client with offline-first fetch wrapper (queue to outbox when offline) (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.5: [ ] Inbox/Outbox + Compose + delivery-status UI (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.6: [ ] Task answering UI (input and approval), artifact attachment UI (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.7: [ ] Sync banner / offline indicator (@pwa-engineer)
- [x] Phase OFFLINE-PWA-2, Task OFFLINE-PWA-2.8: [ ] Responsive layout for small Android screens and old desktops (@pwa-engineer)
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.1: [x] Define the sync batch schema and cursor model in `schemas/syncbatch (@protocol-engineer)
  - Files: schemas/syncbatch.schema.json
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.2: [ ] Implement the Edge-side sync worker (@sync-engineer)
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.3: [ ] Implement delivery-state transitions and acknowledgements (@sync-engineer)
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.4: [ ] Deterministic ordering/reordering by sequence number (@sync-engineer)
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.5: [ ] Convergence logic after long disconnects and partial failures (@sync-engineer)
- [x] Phase SYNCHRONISATION-3, Task SYNCHRONISATION-3.6: [ ] Property/chaos tests (@sync-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.1: [ ] Scaffold `src/HumanGateway (@infrastructure-engineer)
  - Files: src/HumanGateway.Relay
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.2: [ ] PostgreSQL schema (@relay-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.3: [ ] Gateway registration + rendezvous endpoints (@relay-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.4: [ ] Sync endpoint (@relay-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.5: [ ] `ArtifactStore` interface with a PostgreSQL BYTEA implementation (streaming reads; S3-compatible adapter as an optional later step) (@relay-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.6: [ ] Docker Compose environment (@infrastructure-engineer)
- [x] Phase CLOUD-RELAY-4, Task CLOUD-RELAY-4.7: [ ] Structured logging and health endpoint (@relay-engineer)
- [x] Phase ARTIFACTS-1, Task ARTIFACTS-1.1: | ID | Requirement | Priority | (@artifact-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.1: [ ] Gateway identity (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.2: [ ] User identity + authentication at the Edge (local) and Relay (remote) with signed tokens/sessions (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.3: [ ] Authorisation middleware (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.4: [ ] TLS everywhere; signed request tokens for Edge↔Relay traffic (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.5: [ ] Secure artifact access control; content-hash verification on download (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.6: [ ] Secret management (env/secret store, no secrets in repo) (@security-engineer)
- [x] Phase IDENTITY-SECURITY-5, Task IDENTITY-SECURITY-5.7: [ ] Correlation-token passthrough so consumers (FlowForge) enforce role checks and audit (@security-engineer)
- [x] Phase EXTERNAL-WEB-ACCESS-6, Task EXTERNAL-WEB-ACCESS-6.1: [ ] Relay-hosted web entry point for the PWA (@pwa-engineer)
- [x] Phase EXTERNAL-WEB-ACCESS-6, Task EXTERNAL-WEB-ACCESS-6.2: [ ] Rendezvous routing (@relay-engineer)

## Current Task
- None currently running

## Remaining
- [ ] Phase EXTERNAL-WEB-ACCESS-6: Phase 6: External Web Access
- [ ] Phase FLOWFORGE-INTEGRATION-7: Phase 7: FlowForge Reference Integration

## Blockers
- None

## Notes
- Workflow engine run cc1dad89-27a5-4765-8655-91bea28ec9c3
- Harness: opencode
