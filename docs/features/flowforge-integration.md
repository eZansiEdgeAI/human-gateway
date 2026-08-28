# Feature: flowforge-integration

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| FLOW-US-01 | US-05 | Workflow Developer routes a FlowForge `human-input` node through HumanGateway |
| FLOW-FR-01 | FR-39 | Provide a FlowForge `HumanInteractionProvider` abstraction |
| FLOW-FR-02 | FR-40 | `HumanGatewayInteractionProvider`: translate requests into messages and responses into workflow events |
| FLOW-FR-03 | FR-41 | `ConsoleHumanInteractionProvider` retained as a baseline for comparison |
| FLOW-FR-04 | FR-42 | Map concepts: HumanInteractionRequested, HumanResponseReceived, HumanInteractionCompleted, ArtifactReceived, HumanInteractionExpired |
| FLOW-FR-05 | FR-43 | Support both `human-input` and `human-approval` node kinds, including `PendingHumanTask` correlation |
| FLOW-FR-06 | FR-44 | CLI/test harness to drive the provider headlessly (`--answers`-style) for comparison with FlowForge's built-in headless mode |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** flowforge-integration
**ID Prefix:** FLOW
**Summary:** The first reference consumer. A `HumanInteractionProvider` abstraction in `src/HumanGateway.Workflow` lets FlowForge deliver `human-input` / `human-approval` nodes through HumanGateway and resume on the response. A `ConsoleHumanInteractionProvider` baseline enables headless comparison.
**Dependencies:** protocol, cloud-relay, offline-pwa, external-web-access
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| FLOW-US-01 | Workflow Developer | Route a FlowForge `human-input` node through HumanGateway | Workflows resume after a delayed, asynchronous human response | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| FLOW-FR-01 | Provide a FlowForge `HumanInteractionProvider` abstraction | Must |
| FLOW-FR-02 | `HumanGatewayInteractionProvider`: translate FlowForge human interaction requests into HumanGateway messages and responses back into workflow events | Must |
| FLOW-FR-03 | `ConsoleHumanInteractionProvider` retained as a baseline for comparison (synchronous/headless) | Must |
| FLOW-FR-04 | Map concepts: `HumanInteractionRequested`, `HumanResponseReceived`, `HumanInteractionCompleted`, `ArtifactReceived`, `HumanInteractionExpired` | Must |
| FLOW-FR-05 | Support both `human-input` and `human-approval` node kinds, including `PendingHumanTask` correlation (`nodeId`, `role`, `prompt`, `subject`) | Must |
| FLOW-FR-06 | CLI/test harness to drive the provider headlessly (`--answers`-style) for comparison with FlowForge's built-in headless mode | Should |

## 4. UI / Interaction Design

No new user-facing UI in HumanGateway; the PWA task view (offline-pwa) presents the delivered human task. FlowForge's own UI/desktop continues to show workflow state.

---

## 5. Implementation Tasks

### Phase 7: FlowForge Reference Integration
- [ ] Add `HumanInteractionProvider` abstraction in `src/HumanGateway.Workflow`
- [ ] `ConsoleHumanInteractionProvider` (baseline)
- [ ] `HumanGatewayInteractionProvider`: request → HumanGateway message; response + artifacts → workflow events
- [ ] Map `HumanInteractionRequested / HumanResponseReceived / HumanInteractionCompleted / ArtifactReceived / HumanInteractionExpired`
- [ ] Correlate `PendingHumanTask` (`nodeId`, `kind: input|approval`, `role`, `prompt`, `subject`) through the message envelope
- [ ] CLI/test harness driving the provider headlessly (comparable to FlowForge `--answers`)
- [ ] Add a stub `WorkflowRunner` / `PendingHumanTask` implementing the published interface shape for contract-based integration tests (no pinned FlowForge commit)

---

## 5.1 Scope Exclusions

Live end-to-end testing against a real FlowForge runtime is **out of scope** for HumanGateway. FlowForge integration is validated exclusively through the published interface contract (`WorkflowRunner`, `PendingHumanTask`) via an in-repo stub; no FlowForge commit is pinned. The `HumanGatewayInteractionProvider` and `ConsoleHumanInteractionProvider` are tested headlessly against this contract, not against a live FlowForge instance.

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Provider translation logic | Vitest; request/response mapping fixtures |
| Integration | HumanGateway ↔ stub `WorkflowRunner` | Contract-based: drive `HumanGatewayInteractionProvider` against a stub implementing the published `WorkflowRunner` / `PendingHumanTask` interface (`human-input` and `human-approval`); assert resume |
| Comparison | Console vs HumanGateway provider | Same workflow headlessly; assert identical workflow outcome |

Key test scenarios:
1. A `human-input` interaction round-trips through the provider contract; the workflow resumes with the response.
2. A `human-approval` interaction round-trips with approve/reject + reason.
3. An artifact attached to a human response arrives intact via `ArtifactReceived`.
4. Expired interactions surface `HumanInteractionExpired` and the workflow handles it.
5. Headless `--answers`-style run with the HumanGateway provider matches the Console provider outcome.

---

## 7. Acceptance Criteria

1. A `human-input` and `human-approval` interaction are delivered through HumanGateway and the workflow (provider contract) resumes with the human response and any artifacts (Phase 7 exit). Live FlowForge runtime E2E is out of scope.
2. Correlation to the pending node (`nodeId`, role, prompt, subject) is preserved.
3. Expiry is handled and surfaced to the workflow.
4. Headless comparison against the Console provider succeeds.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Does FlowForge need upstream changes? | Prefer consumer-only adapter; upstream only if unavoidable (product vision Open Q #12). Integration is validated via the published interface contract; no pinned commit |
| 2 | Provider packaged as a FlowForge package or monorepo project? | Monorepo project `src/HumanGateway.Workflow`; packaged later if FlowForge package format supports it |
