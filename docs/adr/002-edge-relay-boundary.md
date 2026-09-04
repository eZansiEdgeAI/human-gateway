# ADR-002: Separate Edge and Relay with Outbound-Only Edge Connectivity

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** HumanGateway maintainers

## Context

Site networks should not require public IP addresses, inbound firewall rules, or port forwarding. The cloud still needs to rendezvous messages and remote users across sites.

## Decision

Run a local Edge Gateway as the site trust and durability boundary and a Cloud Relay as the cross-site rendezvous and persistence service. The Edge initiates outbound authenticated HTTPS synchronization; the Relay never requires inbound access to the site.

## Alternatives Considered

- Expose the Edge directly to the Internet: rejected due to site-network risk and operational complexity.
- Use the Relay as the only local API: rejected because local operation must not depend on cloud availability.

## Consequences

- Deployment is compatible with ordinary NAT and school networks.
- Remote delivery waits for the Edge's next outbound sync.
- Production deployments require TLS and registered gateway identity.

## Implementation References

- [Deployment guide](../admin-guide.md#identity-secrets-and-tls)
- [Relay README](../../src/HumanGateway.Relay/README.md)
- [Compose topology](../../docker-compose.yml)
