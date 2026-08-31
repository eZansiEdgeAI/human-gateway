# Feature: identity-security

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| AUTH-US-01 | US-06 (authentication part) | Remote Reviewer logs in securely and can only access their own tasks |
| AUTH-FR-01 | FR-33 | Gateway identity: each Edge Gateway has a unique identity + secret for Relay authentication |
| AUTH-FR-02 | FR-34 | User identity: local users authenticated at the Edge; remote users authenticated at the Relay |
| AUTH-FR-03 | FR-35 | Authorisation: participants are restricted to conversations/tasks they are permitted to access |
| AUTH-FR-04 | FR-36 | Signed requests/tokens for all Edge↔Relay traffic; encryption in transit (TLS) |
| AUTH-FR-05 | FR-37 | Secure artifact access: downloads authorised per participant/conversation |
| AUTH-FR-06 | FR-38 | HumanGateway does not duplicate FlowForge's role-checking/audit; it preserves workflow/task correlation tokens for consumers to enforce those |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** identity-security
**ID Prefix:** AUTH
**Summary:** Identity and security across intermittently connected edge nodes: gateway identity and registration, user authentication at Edge and Relay, per-conversation/task/artifact authorisation, signed/token traffic over TLS, and preservation of consumer correlation tokens (FlowForge's OIDC/role/audit responsibilities stay with FlowForge).
**Dependencies:** protocol, local-edge, cloud-relay
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| AUTH-US-01 | Remote Reviewer | Log in securely and see only my own tasks | My access is restricted and safe | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| AUTH-FR-01 | Gateway identity: each Edge Gateway has a unique identity + secret for Relay authentication | Must |
| AUTH-FR-02 | User identity: local users authenticated at the Edge; remote users authenticated at the Relay | Must |
| AUTH-FR-03 | Authorisation: participants are restricted to conversations/tasks they are permitted to access | Must |
| AUTH-FR-04 | Signed requests/tokens for all Edge↔Relay traffic; encryption in transit (TLS) | Must |
| AUTH-FR-05 | Secure artifact access: downloads authorised per participant/conversation | Must |
| AUTH-FR-06 | HumanGateway does not duplicate FlowForge's role-checking/audit; it preserves workflow/task correlation tokens for consumers to enforce those | Must |

## 4. UI / Interaction Design

- Local login screen at the Edge (username/password, v1).
- Remote login at the Relay for external users.
- No colour-only security states; clear, accessible forms (WCAG 2.1 AA per product vision §9).

---

## 5. Implementation Tasks

### Phase 5: Identity and Security
- [x] Gateway identity: unique ID + registration token; Relay rejects unregistered gateways
- [x] User identity + authentication at the Edge (local) and Relay (remote) with signed tokens/sessions
- [ ] Authorisation middleware: per-conversation/task/artifact access control
- [ ] TLS everywhere; signed request tokens for Edge↔Relay traffic
- [ ] Secure artifact access control; content-hash verification on download
- [ ] Secret management (env/secret store, no secrets in repo)
- [ ] Correlation-token passthrough so consumers (FlowForge) enforce role checks and audit

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Token signing, authz rules, hashes | xUnit |
| Integration | Edge↔Relay with registered gateway | Assert unregistered gateways rejected; tokens verified |
| Security | Negative tests | Cross-conversation access denied; tampered artifact rejected |

Key test scenarios:
1. Unregistered gateway is rejected by the Relay.
2. A user cannot read/write a conversation or artifact they are not authorised for.
3. Tampered artifact (hash mismatch) is rejected on download.
4. Correlation tokens survive the full sync path unchanged for consumer authorisation/audit.

---

## 7. Acceptance Criteria

1. Only authorised participants can read/write conversations, tasks, and artifacts.
2. Edge↔Relay traffic is authenticated (gateway identity) and encrypted in transit; unregistered gateways are rejected.
3. Secrets are not in code or repos.
4. FlowForge correlation tokens pass through unchanged; no role-checking/audit duplication (Phase 5 exit).

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Token format? | Signed opaque/session tokens (v1); JWT if consumer needs it (product vision Open Q #3) |
| 2 | Local Edge auth in the LAN-only PoC (Phases 1–2)? | Not required until this feature (product vision Open Q #6) |
