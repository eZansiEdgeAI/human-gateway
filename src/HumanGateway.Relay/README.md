# HumanGateway.Relay

The Cloud Relay: an ASP.NET Core service backed by PostgreSQL (message metadata + artifact bytes via BYTEA)
that acts as the rendezvous point for Edge Gateway sync and cross-school message exchange (product vision §6.3,
RELAY-FR-01..05).

- **Target framework:** .NET 10 (LTS)
- **Depends on:** `HumanGateway.Protocol` (entity model + schemas + validation) and `HumanGateway.Core`
  (shared sync engine contract)
- **Storage:** PostgreSQL via Npgsql + EF Core (10.0.3 / 10.0.11)

## Status: schema complete (CLOUD-RELAY-4.1 + CLOUD-RELAY-4.2)

This project is the cloud side. It carries the ASP.NET Core minimal API host wired to a PostgreSQL store and
the EF Core entity model for the Relay's durable schema — gateways, conversations, messages, deliveries,
artifacts (metadata + content-addressed BYTEA blobs), participants, and the sync model (inbox, idempotency,
per-gateway sync cursors). Protocol envelopes are stored as canonical wire JSON in `jsonb` columns with
denormalised scalar columns for indexed querying.

The schema (initial migration `InitialCreate`, applied automatically on startup) was validated against a live
PostgreSQL instance: all 12 tables create, `artifact_blobs.data` is `bytea`, envelope columns are `jsonb`,
dedup/content-addressing works (multiple artifact IDs share one blob row), the unique indexes enforce
one-delivery-per-recipient / one-inbox-row-per-message / one-token-fingerprint-per-gateway, and re-running the
migration on restart is a no-op.

The full API surface is built by the following tasks:

| Task | What lands here |
|------|-----------------|
| CLOUD-RELAY-4.1 | **Done** — Scaffold: ASP.NET Core minimal API host + EF Core/PostgreSQL store + entity model + initial migration + health probe |
| CLOUD-RELAY-4.2 | **Done** — PostgreSQL schema (gateways, conversations, messages, deliveries, artifacts + BYTEA blobs, cursors); validated against live PostgreSQL |
| CLOUD-RELAY-4.3 | Gateway registration + rendezvous endpoints (`Endpoints/`, `RELAY-FR-03`) |
| CLOUD-RELAY-4.4 | Sync endpoint: push/pull cursors + delivery ack (`SYNC-FR-03/05`, consumes the synchronisation protocol) |
| CLOUD-RELAY-4.5 | `ArtifactStore` BYTEA implementation (streaming reads) |
| CLOUD-RELAY-4.6 | Docker Compose environment: Relay + PostgreSQL + Edge |
| CLOUD-RELAY-4.7 | Structured logging + health endpoint |

### Endpoints (current scaffold)

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/healthz` | Liveness/readiness probe with a PostgreSQL round-trip |
| `GET` | `/relay` | Service identity probe (name + assembly version) |

## Run

Start a development PostgreSQL and run the Relay:

```bash
# Development PostgreSQL (Postgres 16+, user/pass = humangateway/humangateway)
docker run -d --name humangateway-relay-db \
  -e POSTGRES_USER=humangateway \
  -e POSTGRES_PASSWORD=humangateway \
  -e POSTGRES_DB=humangateway_relay \
  -p 5432:5432 \
  postgres:16

# Apply the initial migration and run
dotnet run --project src/HumanGateway.Relay
# http://localhost:5275/healthz   -> {"status":"ok","store":"postgres"}
```

The connection string is read from `ConnectionStrings:Relay` (override via environment
`ConnectionStrings__Relay` in deployment). In the `Development` environment the default targets the local
PostgreSQL above.

To regenerate or add migrations:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/HumanGateway.Relay --startup-project src/HumanGateway.Relay
```
