# Architecture Decision Records

These records document durable architectural decisions in HumanGateway. Requirements and feature documents describe intent; ADRs explain why the implemented structure was selected.

## Index

| ADR | Decision | Status |
|---|---|---|
| [001](001-edge-first-store-and-forward.md) | Edge-first store-and-forward communication | Accepted |
| [002](002-edge-relay-boundary.md) | Separate Edge and Relay with outbound-only Edge connectivity | Accepted |
| [003](003-storage-and-durability.md) | SQLite/WAL at the Edge and PostgreSQL at the Relay | Accepted |
| [004](004-cursor-sync-and-idempotency.md) | Cursor sync with idempotent application | Accepted |
| [005](005-content-addressed-artifacts.md) | Content-addressed, resumable artifact storage | Accepted |
| [006](006-offline-pwa-storage.md) | Service Worker and IndexedDB for the offline client | Accepted |
| [007](007-identity-and-authorization-boundaries.md) | Gateway/user identity and authorization boundaries | Accepted |
| [008](008-protocol-as-contract.md) | Versioned JSON schemas as the interoperability contract | Accepted |
| [009](009-flowforge-provider-boundary.md) | Provider boundary for workflow integrations | Accepted |

## Conventions

Use the next sequential number. ADRs are immutable historical records: supersede an old decision with a new ADR rather than rewriting its rationale. Link source, tests, configuration, and operational documentation from the implementation references section.
