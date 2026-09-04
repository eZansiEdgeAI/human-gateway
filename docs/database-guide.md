# Database Inspection Guide

This guide shows read-only ways to inspect messages and pending delivery records in a local Podman Compose deployment.
Run the commands from the repository root. Do not use volume deletion commands while inspecting data.

## Running Containers

Check the stack:

```bash
podman-compose ps
```

The default containers are:

- `human-gateway_edge_1` — local SQLite database and artifacts
- `human-gateway_postgres_1` — Relay PostgreSQL database
- `human-gateway_relay_1` — Relay API

## Relay PostgreSQL

Open PostgreSQL with its built-in client:

```bash
podman exec -it human-gateway_postgres_1 \
  psql -U humangateway -d humangateway_relay
```

List stored messages:

```sql
SELECT id, conversation_id, sender_address, created_at, content_hash
FROM messages
ORDER BY created_at DESC;
```

View the complete canonical message envelope:

```sql
SELECT id, json
FROM messages
ORDER BY created_at DESC;
```

Inspect messages waiting in the Relay pull queues:

```sql
SELECT id, gateway_id, sequence, message_id, created_at_utc, delivered_at_utc
FROM relay_outbox
ORDER BY created_at_utc DESC;
```

Useful PostgreSQL commands:

```text
\dt
\d messages
\q
```

## Edge SQLite

The Edge database is stored in the named volume at `/data/edge.db`. The Edge image does not include the SQLite CLI,
so start a temporary Alpine container attached to the volume:

```bash
podman run --rm -it \
  -v human-gateway_edge-data:/data:Z \
  docker.io/library/alpine:latest sh
```

Inside the temporary container:

```bash
apk add --no-cache sqlite
sqlite3 /data/edge.db
```

List locally stored messages:

```sql
SELECT id, conversation_id, sender_address, created_at, content_hash
FROM messages
ORDER BY created_at DESC;
```

View the complete message envelope:

```sql
SELECT id, json
FROM messages
ORDER BY created_at DESC;
```

Inspect the Edge outbox. These are durable local sync entries waiting for a usable Relay transport:

```sql
SELECT id, gateway_id, sequence, message_id, created_at_utc, delivered_at_utc
FROM outbox
ORDER BY created_at_utc DESC;
```

Exit SQLite and the temporary container:

```text
.quit
exit
```

## Data Safety

These commands are read-only. Avoid:

```bash
podman-compose down -v
```

That removes the `human-gateway_edge-data` and `human-gateway_relay-pgdata` volumes and deletes durable local data.
