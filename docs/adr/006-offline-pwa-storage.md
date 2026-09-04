# ADR-006: Service Worker and IndexedDB for the Offline PWA

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

Users may lose connectivity after loading the client and still need to read cached data and compose responses. The browser client must be installable without introducing a heavy state-management dependency.

## Decision

Use a Vite-built Progressive Web App with a Workbox Service Worker for app-shell caching and IndexedDB for local domain data and the client outbox. Flush queued mutations when connectivity returns.

## Alternatives Considered

- Require a network for every screen: rejected because it violates offline-first operation.
- Use localStorage as the database: rejected because it is not appropriate for structured, growing records and binary metadata.

## Consequences

- The PWA remains usable on inexpensive mobile and desktop browsers.
- Users must understand that local queued work is not yet remotely acknowledged.
- Service-worker and IndexedDB schema changes require careful versioning and migration.

## Implementation References

- [Client README](../../src/HumanGateway.Client/README.md)
- [User offline guidance](../user-guide.md#offline-and-reconnect-behavior)
