# ADR-008: Versioned JSON Schemas as the Interoperability Contract

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

Edge, Relay, Client, and external workflow consumers must interoperate without sharing an implementation language or transport-specific object model.

## Decision

Define protocol entities as versioned JSON Schemas under `schemas/`, validate them in the shared Protocol project and matching client code, and use canonical wire JSON at persistence boundaries.

## Alternatives Considered

- Treat C# classes as the sole contract: rejected because TypeScript and other consumers need an independent wire contract.
- Version only the implementation packages: rejected because released schema meanings must remain immutable.

## Consequences

- Schema changes require compatibility review and conformance tests.
- Protocol v1 schema IDs are stable after release.
- Transport adapters can be added without changing entity semantics.

## Implementation References

- [Schema documentation](../../schemas/README.md)
- [Protocol README](../../src/HumanGateway.Protocol/README.md)
- [Protocol feature](../features/protocol.md)
