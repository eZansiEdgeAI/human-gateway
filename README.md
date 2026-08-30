# HumanGateway

<p align="center">
  <img src="src/HumanGateway.Client/public/favicon.svg" alt="HumanGateway logo" width="96" height="96" />
</p>

HumanGateway is an offline-first communication fabric for human-in-the-loop workflows. It lets a local Edge Gateway queue messages and tasks when connectivity is poor, keep them durable on-site, and reconcile them later when an outbound connection is available.

> [!NOTE]
> This repository is the current implementation foundation for the Edge Gateway, durable sync engine, protocol contracts, and the offline-capable client. The cloud Relay layer is planned as the next major deployment surface, while the local-first behavior is already implemented in the codebase.

## Why this project exists

Modern distributed and agentic workflows often fail at the last mile: the human is available, but the network is not. HumanGateway addresses that gap by treating communication like durable email rather than real-time chat.

- Messages, tasks, and artifacts are stored locally first.
- The Edge remains functional without Internet access.
- Outbound-only synchronization keeps the school or site network boundary simple and secure.
- Workflows can continue asynchronously even when humans are offline for hours or days.

## Architecture at a glance

HumanGateway is designed around three layers:

1. Edge Gateway
   - Runs on inexpensive local hardware.
   - Stores messages, tasks, artifacts, and sync state in SQLite.
   - Exposes a local REST API for the PWA and local clients.
   - Keeps outbound-only sync running when connectivity comes back.

2. Durable sync core
   - Shared protocol and synchronization logic live in the Core project.
   - Handles durable outbox/inbox behavior, idempotency, ordering, and cursor-based sync.

3. Offline-capable client
   - React + TypeScript PWA, designed to be installable and usable offline.
   - Manages local queueing and user-facing task flows for schools, field staff, and remote reviewers.

## Current repository status

This repo already includes the foundational pieces for the local-first system:

- SQLite-backed Edge Gateway service with local REST API endpoints
- Durable outbox/inbox/idempotency state in the Core library
- Protocol models and validation for messages, tasks, delivery records, synchronization batches, and artifacts
- Offline-first React + TypeScript client shell and PWA setup
- Background sync worker skeleton with outbound-only sync hooks

The cloud Relay service and full HTTPS synchronization transport are still part of the broader feature roadmap rather than a completed implementation in this workspace.

## How the flow works

```text
Human workflow request
        ↓
Local Edge Gateway
        ↓
SQLite + durable artifacts + tasks
        ↓
Queue / retry / reconcile
        ↓
When connectivity returns: outbound sync to Relay
        ↓
Remote delivery / acknowledgements / convergence
```

This is not a real-time messenger. It is a store-and-forward system designed for eventual consistency, reliable recovery, and offline resilience.

## Repository layout

```text
.
├── deployment/              # container and runtime packaging
├── docs/                    # PRD, vision, execution state, research notes
├── schemas/                 # protocol JSON schemas
├── src/
│   ├── HumanGateway.Protocol/   # protocol models and validation
│   ├── HumanGateway.Core/       # sync engine, outbox, inbox, idempotency
│   ├── HumanGateway.Edge/       # local Edge Gateway service
│   └── HumanGateway.Client/     # offline-capable React + TypeScript PWA
├── tests/
│   ├── HumanGateway.Core.Tests/
│   ├── HumanGateway.Edge.Tests/
│   └── HumanGateway.Edge.CrashProbe/
├── HumanGateway.slnx
├── LICENSE
├── README.md
└── dotnet-tools.json
```

## Key capabilities

- Durable local-first storage for messages and tasks
- Local artifact handling with content-addressed storage
- Background sync lifecycle with retry and recovery semantics
- Cursor-based sync model for eventual convergence
- Message and task protocol structures aligned with workflow integration
- Offline-capable client PWA shell with service worker support

## Getting started

### Prerequisites

- .NET 10 SDK
- Node.js 20+ and npm
- Docker or Podman (optional, for containerized deployment)

### Run the Edge service

From the repository root:

```bash
dotnet run --project src/HumanGateway.Edge
```

The service exposes the local API and health endpoints, including `/healthz` and `/sync/status`.

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

### Containerized Edge deployment

```bash
./deployment/docker/run-edge.sh
```

## Documentation

The project documentation suite is already in the repo and is the best source for requirements and architecture decisions:

- [docs/PRD.md](docs/PRD.md)
- [docs/product-vision.md](docs/product-vision.md)
- [docs/IDEA.md](docs/IDEA.md)
- [deployment/README.md](deployment/README.md)
- [src/HumanGateway.Edge/README.md](src/HumanGateway.Edge/README.md)
- [src/HumanGateway.Core/README.md](src/HumanGateway.Core/README.md)
- [src/HumanGateway.Client/README.md](src/HumanGateway.Client/README.md)

## Design principles

> [!IMPORTANT]
> HumanGateway is intentionally not a real-time chat system. It is designed for durable, asynchronous communication when the network is unreliable but the task still matters.

The implementation follows a few clear principles:

- Offline-first by default
- Local durability before any remote sync
- Outbound-only connectivity at the Edge boundary
- Idempotent message and task handling for recovery
- Explicit protocol contracts rather than implicit application behavior

## Project direction

The project is positioned as a reusable communication fabric for human-in-the-loop automation, with FlowForge as the first concrete consumer reference. The long-term goal is a robust system that works across intermittent networks without forcing humans or workloads to depend on continuous connectivity.

This repository is intentionally practical and incremental: it establishes the durable protocol, the local Edge runtime, and the PWA client foundation before the remote cloud synchronization layer is added in the next implementation stages.
