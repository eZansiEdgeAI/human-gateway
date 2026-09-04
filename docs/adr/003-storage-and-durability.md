# ADR-003: SQLite at the Edge and PostgreSQL at the Relay

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

The Edge targets low-cost hardware and needs zero-admin local storage. The Relay is a shared cloud service requiring concurrent durable access and relational querying.

## Decision

Use SQLite with WAL and durability pragmas for Edge metadata, plus a local filesystem artifact store. Use PostgreSQL for Relay metadata and artifact bytes, with EF Core migrations applied during Relay startup.

## Alternatives Considered

- PostgreSQL on every Edge: rejected for operational and resource overhead.
- Object storage as a Relay requirement: deferred because PostgreSQL BYTEA provides a simple default deployment.

## Consequences

- Edge installation is lightweight and offline-capable.
- Operators must back up both Edge database/artifact data and Relay PostgreSQL volumes.
- Large artifact workloads may eventually require an object-storage adapter.

## Implementation References

- [Admin storage and backups](../admin-guide.md#storage-and-backups)
- [Edge README](../../src/HumanGateway.Edge/README.md)
- [Relay README](../../src/HumanGateway.Relay/README.md)
