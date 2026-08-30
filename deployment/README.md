# Deployment

HumanGateway is a monorepo with three deployable surfaces. This directory holds the packaging and
run scripts for each (product vision §6.2 "deployment/").

| Surface | Target | Where |
|---------|--------|-------|
| **Edge Gateway** | Raspberry Pi (arm64) / old PC (amd64), Linux or Windows | [`docker/`](docker/) |
| **Cloud Relay** | containerised (Docker/Podman), PostgreSQL backend | (arrives with the cloud-relay feature) |

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
