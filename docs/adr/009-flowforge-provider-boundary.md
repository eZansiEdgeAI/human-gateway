# ADR-009: Provider Boundary for Workflow Integrations

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

HumanGateway must support human-input and human-approval interactions without becoming a workflow engine or pinning its implementation to an internal FlowForge runtime.

## Decision

Expose a `HumanInteractionProvider` abstraction. Keep a console provider for comparison and provide a HumanGateway provider that translates interaction requests, responses, artifacts, expiry, and pending-task correlation. Validate against the published interface shape using an in-repository contract stub; live FlowForge runtime E2E remains outside this release scope.

## Alternatives Considered

- Embed workflow execution in HumanGateway: rejected because workflow state and authorization belong to the consumer.
- Pin to a private FlowForge commit: rejected because it would make the communication fabric unnecessarily coupled.

## Consequences

- Other workflow systems can implement the provider boundary.
- HumanGateway can test deterministic translation and round trips without a live external runtime.
- Consumers must perform their own workflow authorization, audit, and state management.

## Implementation References

- [Workflow feature](../features/flowforge-integration.md)
- [Workflow package](../../src/HumanGateway.Workflow/package.json)
- [Release limitations](../releases/0.1.0.md#known-limitations)
