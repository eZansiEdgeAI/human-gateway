# ADR-001: Edge-First Store-and-Forward Communication

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

The primary deployment must continue to serve people when Internet access is unavailable for hours or days. A real-time, cloud-dependent design would make local work depend on the least reliable part of the system.

## Decision

Persist messages, human tasks, deliveries, and artifacts locally at the Edge before attempting network delivery. Treat connectivity loss as a waiting state and forward durable records when connectivity returns.

## Alternatives Considered

- Cloud-first synchronous delivery: rejected because local work would stop during outages.
- Browser-only local storage: rejected because durability and shared access must survive browser restarts and support multiple clients.

## Consequences

- Local users can work without Internet access.
- Delivery is eventual, not real-time; users must understand queued and syncing states.
- The system needs durable queues, retry policy, idempotency, and recovery procedures.

## Implementation References

- [Product vision lifecycle](../product-vision.md#10-system-states--lifecycle)
- [Edge service](../../src/HumanGateway.Edge/README.md)
- [User guide](../user-guide.md)
