# Changelog

All notable changes to HumanGateway are documented here. The initial release is aligned with the application and workflow package version `0.1.0`.

## [Unreleased]

### Added

- Reserved for changes after `0.1.0`.

## [0.1.0] - 2026-09-04

### Added

- Versioned JSON protocol schemas and matching .NET/TypeScript validation.
- Durable Edge Gateway storage for conversations, messages, deliveries, tasks, participants, and artifacts.
- SQLite WAL durability, durable inbox/outbox, idempotency, and crash recovery.
- Cursor-based synchronization with sequence numbers, acknowledgements, retries, backoff, and convergence handling.
- Cloud Relay backed by PostgreSQL with gateway registration and rendezvous routing.
- Content-addressed, deduplicated, hash-verified, resumable artifact transfer with configurable limits and quotas.
- Local and remote authentication, session handling, and per-resource authorization.
- React/TypeScript offline PWA with Service Worker caching, IndexedDB storage, conversations, task views, compose, attachments, and delivery status.
- FlowForge `HumanInteractionProvider`, console baseline, HumanGateway adapter, task correlation, expiry mapping, and headless contract harness.
- Docker Compose development deployment, Edge container packaging, health probes, and structured Relay logging.

### Changed

- The project is now documented as an implemented `0.1.0` release rather than only an execution roadmap.

### Security

- Gateway registration tokens are fingerprinted rather than persisted in plaintext.
- Production deployment requires TLS for Edge-to-Relay traffic and externalized secrets.
- Artifact downloads and message/task resources are authorization-protected.

[Unreleased]: https://github.com/your-org/human-gateway/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/your-org/human-gateway/releases/tag/v0.1.0
