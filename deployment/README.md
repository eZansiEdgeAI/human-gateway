# Deployment

HumanGateway is a monorepo with three deployable surfaces. This directory holds the packaging and
run scripts for each (product vision §6.2 "deployment/").

| Surface | Target | Where |
|---------|--------|-------|
| **Edge Gateway** | Raspberry Pi (arm64) / old PC (amd64), Linux or Windows | [`docker/`](docker/) |
| **Cloud Relay** | containerised (Docker/Podman), PostgreSQL backend | [`docker/`](docker/) |

## Full stack via Docker Compose (Relay + PostgreSQL + Edge)

The whole system — the Cloud Relay, its PostgreSQL store, and an Edge Gateway —
runs together for dev/test with a single Compose command (CLOUD-RELAY-4.6,
RELAY-FR-05). The compose file lives at the **repo root** so the build context
and `dockerfile:` paths resolve identically under Docker Compose v2 and Podman
(`podman-compose`); it builds both container images natively for the local
architecture.

```bash
# from the repo root
docker compose up -d --build
# or, with Podman:
podman-compose up -d
```

What starts:

| Service | Purpose | Published |
|---------|---------|-----------|
| `postgres` | Relay's durable store (RELAY-FR-01) | `127.0.0.1:5433` (internal otherwise; override `HG_DB_PORT`) |
| `relay` | Cloud Relay: sync + registration + rendezvous API | `http://127.0.0.1:5275` |
| `edge` | Edge Gateway: local REST API + SQLite/artifacts | `http://127.0.0.1:8080` |

The Relay applies its EF Core migration on startup (it waits for `postgres` to
be healthy) and the Edge waits for the Relay; both expose `/healthz` and are
health-checked by Compose. The Edge's SQLite store and artifact files live on
the `edge-data` volume (`/data`); PostgreSQL data on `relay-pgdata`.

Verify:

```bash
curl http://127.0.0.1:5275/healthz   # {"status":"ok","store":"postgres"}
curl http://127.0.0.1:8080/healthz   # {"status":"ok","store":"sqlite"}
curl http://127.0.0.1:5275/relay     # service identity probe
docker compose ps
```

All settings are optional environment overrides (see
[`.env.example`](../.env.example)): `HG_DB_PORT`, `HG_RELAY_PORT`, `HG_EDGE_PORT`,
`HG_GATEWAY_ID`. Tear the stack down with `docker compose down` (add `-v` to
also remove the data volumes).

## Edge Gateway (Raspberry Pi / old PC)

The Edge is a LAN-only ASP.NET Core service: SQLite (WAL) + a filesystem artifact store, a local
REST API for the PWA, and a background sync worker that dials out to the Relay (outbound-only).
It is fully functional with no Internet.

Two ways to run it:

### 1. Container (recommended)

Uses Docker or Podman, building the image natively for the local architecture (`arm64` on a
64-bit Raspberry Pi OS, `amd64` on an old PC):

```bash
sudo ./docker/run-edge.sh
```

Configuration is via environment variables (all optional):

| Variable | Default | Purpose |
|----------|---------|---------|
| `HG_CONTAINER` | auto (podman, else docker) | container runtime |
| `HG_IMAGE` | `humangateway-edge:latest` | image name/ref (set a registry ref + `HG_PULL=1` to pull prebuilt) |
| `HG_DATA_DIR` | `/var/lib/humangateway` (root) / `~/.local/share/humangateway` | durable SQLite + artifacts volume |
| `HG_PORT` | `8080` | host port published to the container's 8080 |
| `HG_GATEWAY_ID` | `edge:local` | gateway identity (AUTH-FR-01) |
| `HG_NAME` | `humangateway-edge` | container name |

The container runs unprivileged (uid 1654), publishes `8080`, and bind-mounts the data directory at
`/data`. It restarts automatically after a power loss or reboot (`--restart unless-stopped`) — the
SQLite WAL store and content-addressed artifacts are durable (EDGE-FR-02/07, NF-04).

After it starts, verify:

```bash
curl http://127.0.0.1:8080/healthz      # {"status":"ok",...}
curl http://127.0.0.1:8080/sync/status  # sync-status snapshot
```

### 2. Bare-metal (fallback when a container runtime is unavailable)

```bash
dotnet publish src/HumanGateway.Edge -c Release -o out
ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet out/HumanGateway.Edge.dll
```

The Edge defaults its SQLite store to `<content-root>/data/edge.db`; set
`ConnectionStrings__Edge` and `Artifacts__RootPath` to relocate durable state.

## Building the image manually

```bash
# from the repo root
docker build -f deployment/docker/Dockerfile.edge -t humangateway-edge .
# or
podman build -f deployment/docker/Dockerfile.edge -t humangateway-edge .
```

For a cross-architecture build (e.g. build an arm64 image from an x86_64 host):

```bash
docker buildx build --platform linux/arm64 \
  -f deployment/docker/Dockerfile.edge -t humangateway-edge:arm64 .
```
