# HumanGateway

<p align="center">
  <img src="src/HumanGateway.Client/public/favicon.svg" alt="HumanGateway logo" width="96" height="96" />
</p>

HumanGateway is an offline-first communication fabric for human-in-the-loop workflows. It keeps human tasks, messages, and artifacts durable at the edge, allows local work to continue without connectivity, and reconciles changes when an outbound connection becomes available again.

## Why this project exists

Modern agentic and distributed workflows often fail at the last mile: the task is ready, the human is available, but the network is not. HumanGateway treats communication like durable store-and-forward mail rather than real-time chat.

The core idea is simple:

- local work continues even when the internet is unavailable
- message delivery is durable and retried automatically
- the edge gateway remains the trust boundary for site operations
- sync happens outbound-only, keeping the local network simple and secure

## Architecture overview

The repository is organized around a few clear layers:

1. Edge Gateway
   - ASP.NET Core service running on local infrastructure
   - SQLite-backed durable storage for messages, artifacts, deliveries, and tasks
   - local REST API used by the PWA and local clients
   - background sync worker responsible for outbound reconciliation

2. Shared sync engine and protocol core
   - durable outbox/inbox and idempotency logic
   - cursor-based sync semantics and ordering rules
   - common protocol models that define message, task, artifact, delivery, and participant behavior

3. Client experience
   - React + TypeScript PWA designed for offline-first operation
   - service worker and app-shell caching for installable use
   - local-first UI semantics for task and message flows

4. Relay and security
   - cloud-facing relay and security components for authenticated outbound sync and gateway identity
   - transport-level protections and registration flow for the site edge

## Current implementation status

This repository already contains a substantial working foundation rather than a purely design-only prototype.

Implemented today:

- SQLite-backed Edge Gateway service with local API endpoints
- durable inbox/outbox and idempotency stores in the core engine
- protocol and schema definitions for messages, participants, artifacts, delivery states, and sync batches
- local artifact storage with content-addressed handling and hash verification
- background sync worker skeleton and gateway registration/security wiring
- React + TypeScript PWA scaffold with service worker and offline detection
- .NET test projects covering core behavior and edge crash-safety paths

The project is intentionally incremental. The durable local-first system and protocol backbone are in place; the relay sync transport and broader deployment layer continue as part of the planned extension path.

## Repository layout

```text
.
├── deployment/                  # docker and runtime deployment assets
├── docs/                        # PRD, vision, execution state, research, and workflow notes
├── schemas/                     # JSON schemas for protocol and sync contracts
├── src/
│   ├── HumanGateway.Protocol/   # protocol models, validation, and shared contracts
│   ├── HumanGateway.Core/       # sync engine, outbox, inbox, cursor, idempotency logic
│   ├── HumanGateway.Edge/       # local edge gateway service and SQLite-backed API
│   ├── HumanGateway.Relay/      # cloud relay service and sync endpoints
│   ├── HumanGateway.Security/   # identity, auth, and security primitives
│   └── HumanGateway.Client/     # React + TypeScript offline PWA
├── tests/
│   ├── HumanGateway.Core.Tests/
│   ├── HumanGateway.Edge.Tests/
│   ├── HumanGateway.Relay.Tests/
│   ├── HumanGateway.Security.Tests/
│   └── HumanGateway.Edge.CrashProbe/
├── HumanGateway.slnx
├── docker-compose.yml
├── dotnet-tools.json
├── LICENSE
├── README.md
└── docs/PRD.md
```

## Core workflow

```text
Human request or task
        ↓
Local Edge Gateway
        ↓
SQLite + durable artifacts + task records
        ↓
Queue / retry / reconcile
        ↓
When connectivity returns: outbound sync to Relay
        ↓
Remote delivery / acknowledgements / convergence
```

This is not a real-time messenger. It is designed for eventual consistency, durable recovery, and resilient offline operation.

## Features

- durable local-first message and task storage
- edge-operated queueing with retry and backoff behavior
- outbox/inbox synchronization semantics with idempotency protection
- artifact-first handling with hash-based deduplication and content validation
- protocol-driven interoperability rather than custom app-only assumptions
- installable, offline-capable PWA client experience
- security-aware registration and signed request handling for outbound edge-to-relay traffic

## Getting started

### Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- Docker or Podman for containerized deployment

### Run the Edge service

From the repository root:

```bash
dotnet run --project src/HumanGateway.Edge
```

The service exposes local endpoints for health and sync status, including:

- `/healthz`
- `/sync/status`
- `/conversations`
- `/messages`
- `/tasks`
- `/artifacts`

### Run the client app

```bash
cd src/HumanGateway.Client
npm install
npm run dev
```

### Build the client

```bash
cd src/HumanGateway.Client
npm run build
```

### Run the .NET solution

```bash
dotnet build HumanGateway.slnx
```

### Container deployment

```bash
./deployment/docker/run-edge.sh
```

## Documentation

The most relevant project documentation lives in the repository and is the best source for product intent and implementation details:

- [docs/PRD.md](docs/PRD.md)
- [docs/product-vision.md](docs/product-vision.md)
- [docs/IDEA.md](docs/IDEA.md)
- [docs/WORKFLOW-STATE.json](docs/WORKFLOW-STATE.json)
- [deployment/README.md](deployment/README.md)
- [src/HumanGateway.Edge/README.md](src/HumanGateway.Edge/README.md)
- [src/HumanGateway.Core/README.md](src/HumanGateway.Core/README.md)
- [src/HumanGateway.Client/README.md](src/HumanGateway.Client/README.md)

## Design principles

> [!IMPORTANT]
> HumanGateway is intentionally not a real-time chat system. It is built for durable, asynchronous communication when the network is unreliable but the task still matters.

The implementation follows a few guiding principles:

- offline-first by default
- local durability before any remote sync
- outbound-only connectivity at the edge boundary
- idempotent handling for replay, retries, and reconciliation
- explicit protocol contracts instead of implicit or brittle app behavior

## Project direction

HumanGateway is positioned as a reusable communication fabric for human-in-the-loop automation. The initial design focus is a school or site edge gateway that works offline-first and remains operational even when connectivity is intermittent or unavailable.

FlowForge is the first concrete consumer reference, but the system is designed to be reusable beyond a single workflow product. The repository is intentionally practical and incremental: it establishes the durable protocol, local edge runtime, and client foundation before broad cloud synchronization and deployment expansions are layered on top.

## Contributing

The repository is organized around a few high-signal areas:

- protocol and schema changes: [schemas/](schemas/)
- edge runtime and SQLite storage: [src/HumanGateway.Edge/](src/HumanGateway.Edge/)
- sync, idempotency, and ordering logic: [src/HumanGateway.Core/](src/HumanGateway.Core/)
- relay and security behavior: [src/HumanGateway.Relay/](src/HumanGateway.Relay/), [src/HumanGateway.Security/](src/HumanGateway.Security/)
- client behavior and UI shell: [src/HumanGateway.Client/](src/HumanGateway.Client/)

When changing behavior, prefer the existing domain-specific README files and design documents as the grounding source for intent and constraints.
