# ADR-004: Cursor-Based Synchronization with Idempotent Application

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

Intermittent links cause retries, duplicates, partial transfers, and out-of-order arrival. Full-state synchronization would waste bandwidth and complicate recovery.

## Decision

Use durable message IDs, per-gateway sequence numbers, push/pull cursors, delivery acknowledgements, content hashes, and idempotent apply operations. Retry transient failures with capped jittered exponential backoff.

## Alternatives Considered

- Best-effort fire-and-forget delivery: rejected because it loses work.
- Full database replication: rejected because it does not match the protocol boundary or low-bandwidth constraint.

## Consequences

- Retries can safely replay requests and produce one user-visible effect.
- Operators should interpret “delivered” and “acknowledged” as separate states.
- Cursor state and sequence allocation are durable operational data.

## Implementation References

- [Synchronization feature](../features/synchronisation.md)
- [Relay sync endpoints](../../src/HumanGateway.Relay/README.md#endpoints)
- [User delivery states](../user-guide.md#statuses-and-notifications)
