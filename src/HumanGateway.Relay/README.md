# HumanGateway.Relay

The Cloud Relay: an ASP.NET Core service backed by PostgreSQL (message metadata + artifact bytes via BYTEA)
that acts as the rendezvous point for Edge Gateway sync and cross-school message exchange (product vision §6.3,
RELAY-FR-01..05).

- **Target framework:** .NET 10 (LTS)
- **Depends on:** `HumanGateway.Protocol` (entity model + schemas + validation) and `HumanGateway.Core`
  (shared sync engine contract)
- **Storage:** PostgreSQL via Npgsql + EF Core (10.0.3 / 10.0.11)

## Status: registration + rendezvous complete (CLOUD-RELAY-4.1..4.3)

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

The registration + rendezvous surface (CLOUD-RELAY-4.3) was exercised end-to-end against live PostgreSQL: the
two-step handshake issues a 48-char `hgrt_` token and moves the identity `PENDING → REGISTERED`; only the
`sha256:` fingerprint is persisted; rotation invalidates the old token; and unregistered / pending / suspended /
revoked identities are rejected with the SP-02 error codes. Rendezvous lists and resolves only registered
gateways, including the `system:<gatewayId>` form.

HTTP-level coverage of the full surface lives in `tests/HumanGateway.Relay.Tests/GatewayRegistrationTests.cs`:
it boots the real `Program` via `WebApplicationFactory` over a disposable Testcontainers PostgreSQL (cloud-relay
§6 integration tier) and asserts the wire contract — 201/200 status codes, exact camelCase keys, exact
`"REGISTERED"` enum token, `ProtocolError`-shaped rejections, and SP-02 acceptance criterion §7 #3 (unregistered
gateways rejected). Unit tests cover token generation/fingerprinting (SP-07), request validation, and the
record→protocol mapping. Run with a Docker-compatible socket available:

```bash
dotnet test tests/HumanGateway.Relay.Tests
```

The full API surface is built by the following tasks:

| Task | What lands here |
|------|-----------------|
| CLOUD-RELAY-4.1 | **Done** — Scaffold: ASP.NET Core minimal API host + EF Core/PostgreSQL store + entity model + initial migration + health probe |
| CLOUD-RELAY-4.2 | **Done** — PostgreSQL schema (gateways, conversations, messages, deliveries, artifacts + BYTEA blobs, cursors); validated against live PostgreSQL |
| CLOUD-RELAY-4.3 | **Done** — Gateway registration + rendezvous endpoints (`Endpoints/GatewayEndpoints.cs`, `Endpoints/RendezvousEndpoints.cs`, `Services/`, `Security/`; `RELAY-FR-03`, `WEBX-FR-02`); validated against live PostgreSQL |
| CLOUD-RELAY-4.4 | Sync endpoint: push/pull cursors + delivery ack (`SYNC-FR-03/05`, consumes the synchronisation protocol) |
| CLOUD-RELAY-4.5 | `ArtifactStore` BYTEA implementation (streaming reads) |
| CLOUD-RELAY-4.6 | Docker Compose environment: Relay + PostgreSQL + Edge |
| CLOUD-RELAY-4.7 | Structured logging + health endpoint |

### Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/healthz` | Liveness/readiness probe with a PostgreSQL round-trip |
| `GET` | `/relay` | Service identity probe (name + assembly version) |
| `POST` | `/gateways` | Request registration (201 `PENDING` + one-time `hgrt_` token) — `RELAY-FR-03`, `AUTH-FR-01` |
| `POST` | `/gateways/{gatewayId}/register` | Confirm registration by presenting the token (200 full `Gateway` record) |
| `POST` | `/gateways/{gatewayId}/rotate` | Rotate a registered gateway's token (200 fresh one-time token) |
| `GET` | `/rendezvous/gateways` | List registered gateways as rendezvous targets (`WEBX-FR-02`) |
| `GET` | `/rendezvous/gateways/{gatewayId}` | Rendezvous info for one registered gateway |
| `GET` | `/rendezvous/lookup?participant=` | Resolve a participant address to its serving gateway |

### Registration + rendezvous (RELAY-FR-03, WEBX-FR-02)

The two-step registration handshake keeps the plaintext token secret (SP-07): the Relay issues a 256-bit
`hgrt_` token on `POST /gateways`, stores only its `sha256:` fingerprint, and moves the identity
`PENDING → REGISTERED` when the Edge presents the token on `/register`. Unregistered, pending, suspended,
and revoked identities are rejected on every operation with the reserved SP-02 codes
(`GATEWAY_UNREGISTERED` / `GATEWAY_SUSPENDED` / `GATEWAY_REVOKED`); a wrong or expired token yields
`REGISTRATION_TOKEN_INVALID` / `REGISTRATION_TOKEN_EXPIRED`. Tokens are never logged and never appear in
error payloads.

Rendezvous routing is strictly outbound-only (SP-01): only `REGISTERED` gateways are listed/resolvable, and
the `system:<gatewayId>` form of the lookup maps directly to a gateway (its suffix *is* the durable ID).
Remote message delivery rides the gateway's next outbound sync pull.

Registration/rendezvous behaviour is configurable under the `Relay` section (`RegistrationTokenTtlDays`,
`Relay:Rendezvous:OnlineWindowMinutes`).

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
