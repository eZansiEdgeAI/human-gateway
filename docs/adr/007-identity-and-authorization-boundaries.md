# ADR-007: Gateway and User Identity with Explicit Authorization Boundaries

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

The system has site gateways, local users, remote users, conversations, tasks, artifacts, and workflow consumers. Authentication and resource access must not be conflated with workflow role decisions.

## Decision

Authenticate gateways for Edge-to-Relay operations and users with signed sessions at the Edge or Relay. Enforce access by conversation, task, and artifact participation. Preserve workflow correlation tokens, while leaving workflow role authorization and audit to the consuming workflow platform.

## Alternatives Considered

- Trust LAN location alone: rejected because it does not identify users or protect artifacts.
- Duplicate the workflow platform's roles and audit: rejected because it creates divergent authority.

## Consequences

- Secrets belong in environment or secret stores, never source control.
- A valid login does not imply access to every conversation or task.
- Deployers remain responsible for retention, privacy, and consumer-specific compliance.

## Implementation References

- [Identity and security feature](../features/identity-security.md)
- [Admin security checklist](../admin-guide.md#security-and-privacy-checklist)
