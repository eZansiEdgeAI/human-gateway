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

HumanGateway `0.1.0` is an implemented reference release, not only a design prototype. The repository includes:

- a durable SQLite/WAL Edge Gateway with local REST API, task handling, authentication, and filesystem artifacts
- cursor-based Edge-to-Relay synchronization with idempotency, retry/backoff, acknowledgements, and convergence handling
- a PostgreSQL-backed Cloud Relay with gateway registration, rendezvous routing, remote access, and artifact transfer
- content-addressed, hash-verified, deduplicated, resumable artifact uploads and downloads with configurable limits and quotas
- an installable React/TypeScript PWA with Service Worker caching, IndexedDB offline data, conversations, tasks, attachments, and delivery status
- shared v1 JSON schemas and .NET/TypeScript protocol validation
- FlowForge provider mappings for `human-input` and `human-approval`, validated against a published interface contract and in-repository stub
- Docker Compose development deployment, Edge container packaging, health checks, and structured logging

See the [0.1.0 release notes](docs/releases/0.1.0.md) for scope and limitations. Live end-to-end testing against a FlowForge runtime remains outside this release.

## Repository layout

```text
.
├── deployment/                  # docker and runtime deployment assets
├── docs/                        # Guides, ADRs, release notes, requirements, and feature docs
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
└── CHANGELOG.md
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

The recommended path is the interactive setup CLI. It checks prerequisites, installs JavaScript dependencies, builds
the services, starts the local stack, and verifies service health:

```bash
npm run setup
```

For a repeatable full-stack setup with defaults:

```bash
npm run setup -- --mode compose --yes
```

`npm run setup` starts the PostgreSQL, Relay, and Edge backend services. It does not start the PWA development
server. After setup completes, start the PWA from a second terminal:

```bash
cd src/HumanGateway.Client
VITE_EDGE_BASE_URL=http://127.0.0.1:8080 npm run dev
```

Open the Vite URL shown in the terminal, normally `http://localhost:5173`. Sign in with the Edge bootstrap
credentials entered during setup. For a non-interactive setup, provide credentials explicitly rather than leaving
the bootstrap account empty:

```bash
HG_EDGE_AUTH_BOOTSTRAP_USERNAME=admin \
HG_EDGE_AUTH_BOOTSTRAP_PASSWORD='change-this-password' \
HG_RELAY_AUTH_BOOTSTRAP_USERNAME=admin \
HG_RELAY_AUTH_BOOTSTRAP_PASSWORD='change-this-password' \
npm run setup -- --mode compose --yes
```

The default local endpoints are Edge `http://127.0.0.1:8080` and Relay `http://127.0.0.1:5275`. Check the running
services with:

```bash
npm run setup:status
npm run setup:verify
```

Stop the backend stack with `docker compose down`. Do not add `-v` unless you intentionally want to delete the
durable PostgreSQL and Edge data volumes.

The CLI also supports `--mode edge` for an Edge-only container deployment. Production TLS, secret stores, backup
automation, and other operational layers are tracked in the [backlog](docs/backlog.md).

For local development without containers, use `--mode dev`. This starts the Edge API and the Vite PWA server in the
background, using Edge port `5187` and the PWA at `http://127.0.0.1:5173`:

```bash
npm run setup -- --mode dev
```

### Prerequisites

- .NET 10 SDK
- Node.js 22.22.2+ and npm
- Docker Engine with Compose v2, or Podman with `podman-compose`, for containerized deployment

The container runtime must be installed and running before Compose setup. On Linux, Docker normally runs as a
system service:

```bash
sudo systemctl enable --now docker
```

For rootless Podman on Linux, Podman does not usually need a daemon; verify it is usable with:

```bash
podman info
```

On macOS or Windows, start the Podman virtual machine before setup:

```bash
podman machine init    # first time only
podman machine start
podman info
```

Install Docker Desktop from [docker.com](https://www.docker.com/products/docker-desktop/) or Podman from
[podman.io](https://podman.io/getting-started/installation). On Debian/Ubuntu Linux, Podman can be installed with
`sudo apt-get install podman podman-compose`.

The setup CLI installs the client and workflow packages from their committed npm lockfiles using `npm ci`. This keeps
dependency versions reproducible and avoids npm re-resolving the workflow test dependency tree on every setup run. If
you are updating an older checkout that does not contain the workflow lockfile, pull the latest changes before running
setup again.

npm may report that a newer major npm release is available. This is informational; setup does not upgrade npm
automatically. If desired, update npm explicitly after confirming your Node.js version:

```bash
npm install --global npm@12.0.2
```

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

For the full Relay, PostgreSQL, and Edge development stack:

```bash
docker compose up -d --build
```

See the [administrator guide](docs/admin-guide.md) for production deployment, configuration, secrets, TLS, backups, upgrades, and troubleshooting.

## Documentation

- [User guide](docs/user-guide.md)
- [Administrator guide](docs/admin-guide.md)
- [Release notes for 0.1.0](docs/releases/0.1.0.md)
- [Changelog](CHANGELOG.md)
- [Architecture Decision Records](docs/adr/README.md)
- [Product requirements](docs/PRD.md)
- [Product vision](docs/product-vision.md)
- [Deployment details](deployment/README.md)
- [Protocol and schemas](schemas/README.md)
- [Edge service](src/HumanGateway.Edge/README.md)
- [Relay service](src/HumanGateway.Relay/README.md)
- [Client PWA](src/HumanGateway.Client/README.md)
- [Core sync engine](src/HumanGateway.Core/README.md)
- [Workflow integration](docs/features/flowforge-integration.md)
- [Setup and production backlog](docs/backlog.md)

## Design principles

> [!IMPORTANT]
> HumanGateway is intentionally not a real-time chat system. It is built for durable, asynchronous communication when the network is unreliable but the task still matters.

The implementation follows a few guiding principles:

- offline-first by default
- local durability before any remote sync
- outbound-only connectivity at the edge boundary
- idempotent handling for replay, retries, and reconciliation
- explicit protocol contracts instead of implicit or brittle app behavior

## Scope and limitations

HumanGateway is a reusable communication fabric for human-in-the-loop automation. FlowForge is the first reference consumer, but workflow execution, workflow role authorization, and consumer audit remain outside HumanGateway. Delivery is asynchronous and eventual rather than real-time. The initial release does not include SMS, USSD, WhatsApp, or email adapters.

## Contributing

The repository is organized around a few high-signal areas:

- protocol and schema changes: [schemas/](schemas/)
- edge runtime and SQLite storage: [src/HumanGateway.Edge/](src/HumanGateway.Edge/)
- sync, idempotency, and ordering logic: [src/HumanGateway.Core/](src/HumanGateway.Core/)
- relay and security behavior: [src/HumanGateway.Relay/](src/HumanGateway.Relay/), [src/HumanGateway.Security/](src/HumanGateway.Security/)
- client behavior and UI shell: [src/HumanGateway.Client/](src/HumanGateway.Client/)

When changing behavior, prefer the existing domain-specific README files and design documents as the grounding source for intent and constraints.
