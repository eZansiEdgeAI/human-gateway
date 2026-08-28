# Agent Responsibility Matrix

- **Source layout:** features (decomposed)
- **Source:** docs/product-vision.md + docs/features/*.md

## Team Validation

- Unassigned tasks: **0** (all 49 feature FRs mapped)
- Duplicate file owners: **0** (each `src/` project owned by exactly one agent; shared interfaces coordinated via Collaboration)
- Orphan agents: **0** (every agent has cross-links in `## Collaboration`)

## Ownership by Agent

### infrastructure-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 0 | foundation | Monorepo scaffold | `src/*` solution, `schemas/`, `tests/`, `adapters/`, `deployment/` |
| 0 | foundation | .NET solution + projects | `src/HumanGateway.{Protocol,Core,Edge,Relay,Workflow}` |
| 0 | foundation | PWA scaffold | `src/HumanGateway.Client` (Vite + React + TS + Workbox) |
| 4 | cloud-relay | Docker Compose (RELAY-FR-05) | `docker-compose.yml`, Dockerfiles |
| 1 | local-edge | Edge image + Pi scripts (EDGE-FR-01) | `deployment/` |
| all | CI/CD | Build → test → lint → E2E | `.github/workflows/` |

### protocol-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 0 | protocol | Schemas (PROTO-FR-01..06) | `schemas/{message,artifact,participant,delivery,sync,human-task}.schema.json` |
| 0 | protocol | .NET entity model | `src/HumanGateway.Protocol/` |
| 0 | protocol | TS validators | Client protocol module |
| 0 | protocol | Sync/identity/error model | schema documents |

### sync-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 3 | synchronisation | Sync engine (SYNC-FR-01..07) | `src/HumanGateway.Core/` (SyncEngine) |
| 3 | synchronisation | Convergence + ordering | Core engine logic |
| 3 | synchronisation | Delivery ack + state transitions | Core engine logic |

### edge-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 1 | local-edge | Edge service (EDGE-FR-01..07) | `src/HumanGateway.Edge/` |
| 1 | local-edge | SQLite schema + store | EF Core migrations |
| 1 | local-edge | Local REST API (EDGE-FR-03) | minimal API endpoints |
| 1 | local-edge | Durable inbox/outbox (EDGE-FR-04) | Core outbox logic |
| 1 | local-edge | Background sync worker (EDGE-FR-05) | `BackgroundService` |

### relay-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 4 | cloud-relay | Relay service (RELAY-FR-01..05) | `src/HumanGateway.Relay/` |
| 4 | cloud-relay | PostgreSQL schema | EF Core migrations |
| 4 | cloud-relay | Sync API + registration + rendezvous | Relay endpoints |
| 6 | external-web-access | Rendezvous routing (WEBX-FR-02) | Relay routing |

### artifact-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 3/4/5 | artifacts | ArtifactStore interface (ARTF-FR-01..03) | `src/HumanGateway.Core/Artifacts` |
| 1 | artifacts | Edge filesystem store | Edge store impl |
| 4 | artifacts | Relay BYTEA store | Relay store impl |
| 3 | artifacts | Resumable transfer, quotas | sync transfer layer |

### pwa-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 2 | offline-pwa | PWA app (PWA-FR-01..07) | `src/HumanGateway.Client/` |
| 2 | offline-pwa | Service Worker + IndexedDB | Workbox config, store modules |
| 2 | offline-pwa | Inbox/Compose/Task views | React components |
| 6 | external-web-access | PWA via Relay (WEBX-FR-01) | remote-auth path |

### security-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 5 | identity-security | Gateway identity (AUTH-FR-01) | registration + tokens |
| 5 | identity-security | User authn Edge + Relay (AUTH-FR-02) | auth endpoints |
| 5 | identity-security | Authz middleware (AUTH-FR-03/05) | authz middleware |
| 5 | identity-security | TLS + secrets + correlation passthrough (AUTH-FR-04/06, SP-*) | security layer |

### workflow-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| 7 | flowforge-integration | Provider abstraction (FLOW-FR-01..06) | `src/HumanGateway.Workflow/` |
| 7 | flowforge-integration | Concept mapping + correlation | translation layer |
| 7 | flowforge-integration | CLI harness (FLOW-FR-06) | headless runner |
| 7 | flowforge-integration | Contract-based integration via stub `WorkflowRunner`/`PendingHumanTask` (no pinned commit) | stub + contract tests |

### qa-engineer
| Phase | Feature | Task | Outputs |
|---|---|---|---|
| all | all | Test infra + quality gates | `tests/`, CI gates |
| all | all | Unit/integration/chaos/E2E suites | per-feature test suites |
| 7 | flowforge-integration | FlowForge round-trip (via provider-contract stub) + headless comparison tests | contract-based integration suite |

## Phase Execution Order

1. **PROTO-0** — protocol — infrastructure-engineer, protocol-engineer, qa-engineer
2. **EDGE-1** — local-edge — edge-engineer, artifact-engineer (store), qa-engineer
3. **PWA-2** — offline-pwa — pwa-engineer, qa-engineer
4. **SYNC-3** — synchronisation — sync-engineer, edge-engineer, relay-engineer, artifact-engineer, qa-engineer
5. **RELAY-4** — cloud-relay — relay-engineer, artifact-engineer, qa-engineer
6. **AUTH-5** — identity-security — security-engineer, edge-engineer, relay-engineer, qa-engineer
7. **WEBX-6** — external-web-access — relay-engineer, pwa-engineer, security-engineer, qa-engineer
8. **FLOW-7** — flowforge-integration — workflow-engineer, qa-engineer
