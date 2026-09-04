# HumanGateway Administrator Guide

This guide covers deployment and operation of the Edge Gateway, Cloud Relay, and PWA. It is written for site ICT administrators and operators of a Relay installation.

## Responsibilities and Architecture

The Edge Gateway serves the local REST API, SQLite metadata, filesystem artifacts, local authentication, and the PWA. It can operate without Internet access. The Relay provides PostgreSQL-backed rendezvous, remote access, and cross-site synchronization. The Edge initiates outbound synchronization; do not expose the Edge directly to the Internet.

## Prerequisites

- .NET 10 SDK for development or bare-metal operation.
- Docker Compose v2 or Podman for the full stack.
- Linux Raspberry Pi-class hardware or an old PC for the Edge.
- PostgreSQL 16+ for local Relay development; use the supported PostgreSQL version selected for your deployment.
- TLS termination and a secret store for production Relay traffic.

## Full Stack Development Deployment

Copy `.env.example` to `.env`, set bootstrap credentials, and start from the repository root:

```bash
docker compose up -d --build
docker compose ps
```

The default development ports are PostgreSQL `127.0.0.1:5433`, Relay `http://127.0.0.1:5275`, and Edge `http://127.0.0.1:8080`. Verify both services:

```bash
curl http://127.0.0.1:5275/healthz
curl http://127.0.0.1:8080/healthz
```

Stop the stack with `docker compose down`. Add `-v` only when intentionally deleting the `relay-pgdata` and `edge-data` volumes.

## Edge Installation

The recommended container installation is:

```bash
sudo ./deployment/docker/run-edge.sh
```

The script uses Podman when available, otherwise Docker. It bind-mounts durable state at `/data`, publishes port `8080` by default, runs as an unprivileged user, and restarts after reboot. For bare metal:

```bash
dotnet publish src/HumanGateway.Edge -c Release -o out
ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet out/HumanGateway.Edge.dll
```

For a production LAN deployment, place the Edge behind the site’s access controls and restrict its listening interface/firewall to the intended LAN. The Edge must not be directly exposed to the public Internet.

## Configuration

Important environment/configuration keys include:

| Key | Purpose | Default/example |
|---|---|---|
| `Edge__GatewayId` / `HG_GATEWAY_ID` | Durable gateway identity | `edge:compose` |
| `Edge__DataDirectory` | Edge durable state root | `/var/lib/humangateway` |
| `ConnectionStrings__Edge` | SQLite database location | deployment-specific |
| `Artifacts__RootPath` | Edge artifact root | deployment-specific |
| `Edge__RelayBaseUrl` / `Relay__BaseUrl` | Relay address | HTTPS in production |
| `Relay__AllowInsecureHttp` | Development-only plain HTTP opt-in | unset/false in production |
| `Sync__BatchSize` | Sync batch size | `100` |
| `Sync__PollIntervalSeconds` | Sync polling interval | `30` |
| `Sync__Backoff__MaxAttempts` | Retry attempt cap | `8` |
| `Artifacts__MaxArtifactSizeBytes` | Maximum artifact size | `52428800` |
| `Artifacts__QuotaBytes` | Edge artifact quota | `1073741824` |
| `Artifacts__ChunkSizeBytes` | Transfer chunk size | `4194304` |
| `Auth__BootstrapUser__Username` | First local/remote user | empty by default |
| `Auth__BootstrapUser__Password` | Bootstrap password | empty by default |

Relay settings use the `Relay__RegistrationTokenTtlDays`, `Relay__Rendezvous__OnlineWindowMinutes`, `Relay__Artifacts__MaxArtifactSizeBytes`, and `Relay__Artifacts__QuotaBytes` keys. Set production values through a secret manager or protected environment, not committed files.

## Identity, Secrets, and TLS

The Relay registration handshake issues a one-time gateway token. Store it securely, complete registration, and rotate it when staff or infrastructure changes require rotation. Only registered gateways may synchronize.

Use HTTPS for every Edge-to-Relay connection in production. The Compose file enables `Relay__AllowInsecureHttp=true` only for its internal development network; never carry that setting into a production deployment.

Bootstrap passwords must be supplied through deployment secrets. Do not put passwords, gateway tokens, connection-string passwords, or signing material in Git, logs, screenshots, or support tickets.

HumanGateway authorizes access to conversations, tasks, and artifacts. It does not replace a consuming workflow system’s role authorization or audit trail; correlation tokens must remain available to that consumer.

## Storage and Backups

Back up the complete Edge data directory, including the SQLite database and artifact files. Do not copy only the database. Preserve the Relay PostgreSQL volume/database and include its artifact blob data.

Before restoring, stop the relevant service, restore the complete data set, verify ownership and permissions, and start the service. Test restoration on a separate instance. A backup is not complete until a sample message and artifact can be read after restore.

## Health Checks and Monitoring

Useful endpoints are:

- `GET /healthz`: service and store health.
- `GET /sync/status`: Edge queue, sequence, delivery, and artifact-limit snapshot.
- `GET /relay`: Relay service identity probe.
- `GET /rendezvous/gateways`: registered gateway view, subject to authorization.

Monitor service restarts, disk capacity, database capacity, repeated sync failures, artifact quota usage, and messages stuck in waiting or failed states. Logs use structured output on the Relay.

## Upgrades and Rollback

1. Read the release notes for compatibility and migration notes.
2. Back up Edge data and Relay PostgreSQL before upgrading.
3. Record current image, package, and configuration versions.
4. Deploy the new version and verify health endpoints.
5. Verify a local message, task response, artifact transfer, and sync status.
6. Roll back the application only if the release notes permit it; restore data only from a backup made before an incompatible schema migration.

Do not remove volumes as part of a normal upgrade. `docker compose down -v` deletes durable development data.

## Troubleshooting

**Edge is healthy but messages do not leave the site.** Check `/sync/status`, the Relay URL, TLS trust, gateway registration, outbound firewall rules, and Relay health. The local queue is expected to retain entries during an outage.

**Relay fails at startup.** Check PostgreSQL reachability, credentials, migrations, and the database health check. Preserve logs before restarting repeatedly.

**Users cannot sign in.** Confirm the configured bootstrap user, account status, clock accuracy, and whether the user is accessing the correct Edge or Relay address.

**Artifact uploads fail.** Compare the artifact size and quota snapshot with `Artifacts` or `Relay:Artifacts` settings. Check free disk space and PostgreSQL capacity.

**Data appears missing after reboot.** Verify the durable volume or bind mount is still attached and that the service user can read it. Never recreate the container with a new unnamed volume until the old data is located.

## Security and Privacy Checklist

- [ ] Edge is reachable only from the intended LAN.
- [ ] Edge-to-Relay traffic uses HTTPS and certificate validation.
- [ ] Development insecure HTTP settings are absent in production.
- [ ] Bootstrap credentials and gateway tokens are held in a secret store.
- [ ] Gateway identities are unique and registered.
- [ ] Database and artifact backups are encrypted and access-controlled.
- [ ] Artifact quotas and retention match site policy.
- [ ] Logs do not contain passwords, tokens, or unnecessary message content.
- [ ] Recovery and restore procedures have been tested.
- [ ] Site privacy, retention, and applicable child-data obligations have an accountable owner.

## Recovery and Support

When escalating, include deployment version, gateway ID, service health output, sync status, timestamps in UTC, relevant correlation/message/task IDs, and sanitized logs. Exclude credentials, bearer tokens, and attachment contents unless an approved incident process requires them.
