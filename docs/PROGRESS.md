# Project Progress

## Current State
**Phase**: FLOWFORGE-INTEGRATION-7
**Status**: Paused
**Last Updated**: 2026-09-03T21:38:03.820Z
**Run ID**: cc1dad89-27a5-4765-8655-91bea28ec9c3
**Harness**: opencode
**Execution Mode**: auto

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
- [x] Phase EXTERNAL-WEB-ACCESS-6, Task EXTERNAL-WEB-ACCESS-6.3: [ ] Remote login integration (uses identity-security) (@pwa-engineer)
- [x] Phase EXTERNAL-WEB-ACCESS-6, Task EXTERNAL-WEB-ACCESS-6.4: [ ] Responses from remote users flow back to the school and, via correlation tokens, to the workflow consumer (@workflow-engineer)
  - Files: src/HumanGateway.Edge/Api/LocalApiService.cs, src/HumanGateway.Edge/Program.cs, src/HumanGateway.Edge/Sync/SyncWorker.cs, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Edge.xml, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.csproj.CoreCompileInputs.cache, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.xml, tests/HumanGateway.Edge.CrashProbe/bin/Debug/net10.0/HumanGateway.Edge.xml, tests/HumanGateway.Edge.Tests/bin/Debug/net10.0/HumanGateway.Edge.xml, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Edge.xml, src/HumanGateway.Edge/Sync/IInboundMessageHandler.cs, src/HumanGateway.Edge/Sync/InboundMessageProjector.cs
- [x] Phase EXTERNAL-WEB-ACCESS-6, Task EXTERNAL-WEB-ACCESS-6.5: [ ] End-to-end test (@qa-engineer)
  - Files: src/HumanGateway.Core/bin/Debug/net10.0/HumanGateway.Core.dll, src/HumanGateway.Core/bin/Debug/net10.0/HumanGateway.Core.pdb, src/HumanGateway.Core/bin/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Core/bin/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.AssemblyInfo.cs, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.AssemblyInfoInputs.cache, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.csproj.AssemblyReference.cache, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.dll, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.pdb, src/HumanGateway.Core/obj/Debug/net10.0/HumanGateway.Core.sourcelink.json, src/HumanGateway.Core/obj/Debug/net10.0/ref/HumanGateway.Core.dll, src/HumanGateway.Core/obj/Debug/net10.0/refint/HumanGateway.Core.dll, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Core.dll, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Core.pdb, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Edge.dll, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Edge.pdb, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Security.dll, src/HumanGateway.Edge/bin/Debug/net10.0/HumanGateway.Security.pdb, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.AssemblyInfo.cs, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.AssemblyInfoInputs.cache, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.csproj.AssemblyReference.cache, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.dll, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.pdb, src/HumanGateway.Edge/obj/Debug/net10.0/HumanGateway.Edge.sourcelink.json, src/HumanGateway.Edge/obj/Debug/net10.0/ref/HumanGateway.Edge.dll, src/HumanGateway.Edge/obj/Debug/net10.0/refint/HumanGateway.Edge.dll, src/HumanGateway.Edge/obj/Debug/net10.0/rjsmcshtml.dswa.cache.json, src/HumanGateway.Edge/obj/Debug/net10.0/rjsmrazor.dswa.cache.json, src/HumanGateway.Protocol/bin/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Protocol/bin/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Protocol/obj/Debug/net10.0/HumanGateway.Protocol.AssemblyInfo.cs, src/HumanGateway.Protocol/obj/Debug/net10.0/HumanGateway.Protocol.AssemblyInfoInputs.cache, src/HumanGateway.Protocol/obj/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Protocol/obj/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Protocol/obj/Debug/net10.0/HumanGateway.Protocol.sourcelink.json, src/HumanGateway.Protocol/obj/Debug/net10.0/ref/HumanGateway.Protocol.dll, src/HumanGateway.Protocol/obj/Debug/net10.0/refint/HumanGateway.Protocol.dll, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Core.dll, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Core.pdb, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Relay.dll, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Relay.pdb, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Security.dll, src/HumanGateway.Relay/bin/Debug/net10.0/HumanGateway.Security.pdb, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.AssemblyInfo.cs, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.AssemblyInfoInputs.cache, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.csproj.AssemblyReference.cache, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.dll, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.pdb, src/HumanGateway.Relay/obj/Debug/net10.0/HumanGateway.Relay.sourcelink.json, src/HumanGateway.Relay/obj/Debug/net10.0/ref/HumanGateway.Relay.dll, src/HumanGateway.Relay/obj/Debug/net10.0/refint/HumanGateway.Relay.dll, src/HumanGateway.Relay/obj/Debug/net10.0/rjsmcshtml.dswa.cache.json, src/HumanGateway.Relay/obj/Debug/net10.0/rjsmrazor.dswa.cache.json, src/HumanGateway.Security/bin/Debug/net10.0/HumanGateway.Protocol.dll, src/HumanGateway.Security/bin/Debug/net10.0/HumanGateway.Protocol.pdb, src/HumanGateway.Security/bin/Debug/net10.0/HumanGateway.Security.dll, src/HumanGateway.Security/bin/Debug/net10.0/HumanGateway.Security.pdb, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.AssemblyInfo.cs, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.AssemblyInfoInputs.cache, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.csproj.AssemblyReference.cache, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.dll, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.pdb, src/HumanGateway.Security/obj/Debug/net10.0/HumanGateway.Security.sourcelink.json, src/HumanGateway.Security/obj/Debug/net10.0/ref/HumanGateway.Security.dll, src/HumanGateway.Security/obj/Debug/net10.0/refint/HumanGateway.Security.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Core.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Core.pdb, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Edge.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Edge.pdb, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Protocol.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Protocol.pdb, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Relay.Tests.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Relay.Tests.pdb, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Relay.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Relay.pdb, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Security.dll, tests/HumanGateway.Relay.Tests/bin/Debug/net10.0/HumanGateway.Security.pdb, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.AssemblyInfo.cs, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.AssemblyInfoInputs.cache, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.csproj.AssemblyReference.cache, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.csproj.CoreCompileInputs.cache, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.dll, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.pdb, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/HumanGateway.Relay.Tests.sourcelink.json, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/ref/HumanGateway.Relay.Tests.dll, tests/HumanGateway.Relay.Tests/obj/Debug/net10.0/refint/HumanGateway.Relay.Tests.dll, tests/HumanGateway.Relay.Tests/RemoteResponseIntegrationTests.cs

## Current Task
- None currently running

## Remaining
- [ ] Phase FLOWFORGE-INTEGRATION-7: Phase 7: FlowForge Reference Integration

## Blockers
- None

## Notes
- Workflow engine run cc1dad89-27a5-4765-8655-91bea28ec9c3
- Harness: opencode
