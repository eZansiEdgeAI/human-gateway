# ADR-005: Content-Addressed and Resumable Artifact Storage

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

Photos and documents can be much larger than message metadata, and low-bandwidth links may fail during transfer. Duplicate bytes should not consume storage repeatedly.

## Decision

Reference artifacts from messages by ID and SHA-256 content hash. Store bytes by content hash, deduplicate completed content, enforce size and quota limits, and transfer using explicit offsets with hash verification.

## Alternatives Considered

- Embed binary data in messages: rejected due to message size and retry costs.
- Restart every failed upload from zero: rejected for low-bandwidth environments.

## Consequences

- Uploads and downloads can resume after interruption.
- A configured per-artifact limit and per-gateway quota can reject otherwise valid uploads.
- Hashes must be preserved and verified across Edge and Relay.

## Implementation References

- [Artifact feature](../features/artifacts.md)
- [Admin artifact configuration](../admin-guide.md#configuration)
- [Edge artifact endpoints](../../src/HumanGateway.Edge/README.md#local-rest-api-endpoints)
