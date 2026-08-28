# Feature: artifacts

## Traceability

| Feature ID | Original PRD ID | Description |
|-----------|----------------|-------------|
| ARTF-US-01 | US-03 (artifact part) | Teacher attaches a photo or PDF to a response and it reaches the agent intact |
| ARTF-FR-01 | FR-45 | Artifact upload/download over sync with content hashing and deduplication |
| ARTF-FR-02 | FR-46 | Resumable uploads/downloads for large artifacts over low bandwidth |
| ARTF-FR-03 | FR-47 | Artifact size limits and storage quotas configurable per gateway |

**Product Vision:** [docs/product-vision.md](../product-vision.md)
**Original PRD:** [docs/PRD.md](../PRD.md)

---

## 1. Feature Overview

**Feature Name:** artifacts
**ID Prefix:** ARTF
**Summary:** First-class artifact handling across the whole stack: messages reference content by ID + hash, artifacts are stored on the Edge filesystem and in Relay PostgreSQL (BYTEA), transfer over sync is hashed and deduplicated, and large artifacts support resumable transfer over low bandwidth. Builds on every storage component.
**Dependencies:** protocol, local-edge, synchronisation, cloud-relay, offline-pwa
**Priority:** Must

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority |
|----|---------|-------------|-----------|----------|
| ARTF-US-01 | Teacher | Attach a photo or PDF to my response | The assessment agent gets the evidence it needs | Must |

---

## 3. Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| ARTF-FR-01 | Artifact upload/download over sync with content hashing and deduplication | Must |
| ARTF-FR-02 | Resumable uploads/downloads for large artifacts over low bandwidth | Should |
| ARTF-FR-03 | Artifact size limits and storage quotas configurable per gateway | Should |

## 4. UI / Interaction Design

Attachment pickers in the PWA Compose and Task views (photo via camera/file picker, PDF, document, audio); upload progress + resume affordance; size-limit messaging.

---

## 5. Implementation Tasks

- [ ] Artifact store on Edge (filesystem, content-hash named, deduplicated)
- [ ] Artifact store on Relay (PostgreSQL BYTEA via `ArtifactStore` interface; S3-compatible adapter optional later)
- [ ] Authenticated artifact-serving endpoints on Edge (`GET /artifacts/{id}`, filesystem) and Relay (BYTEA, streaming); artifact `filename`/`MIME type`/`hash` metadata travels with the message so the receiving app can render or interpret content (PWA renders by MIME; FlowForge consumes via `ArtifactReceived`)
- [ ] Artifact transfer in the sync protocol: hash verification on both sides, deduplication (skip if hash known)
- [ ] Resumable chunked upload/download for large artifacts
- [ ] Configurable size limits and per-gateway quotas (surfaced in PWA)
- [ ] Interrupted-transfer resume tests

---

## 6. Testing Strategy

| Level | Scope | Approach |
|-------|-------|----------|
| Unit | Hash naming, dedup, quota enforcement | xUnit / Vitest |
| Integration | Artifact end-to-end: PWA → Edge → Relay → (recipient) | Testcontainers; assert hash-intact |
| Chaos | Interrupted transfer | Kill transfer mid-way; resume; verify integrity |

Key test scenarios:
1. Artifact uploads PWA → Edge → Relay and downloads intact (hash verified).
2. Duplicate artifact upload is deduplicated (no re-transfer).
3. Transfer interrupted mid-way → resumes and completes; hash verified.
4. Artifact over the size limit or over quota → rejected with clear message.

---

## 7. Acceptance Criteria

1. Artifacts are referenced by ID + hash and transfer exactly-once with deduplication.
2. Resumable transfer completes interrupted uploads/downloads correctly.
3. Size limits and quotas are configurable and enforced.

---

## 8. Open Questions

| # | Question | Default Assumption |
|---|----------|--------------------|
| 1 | Chunk size for resumable transfer? | 4 MB (configurable) |
| 2 | Default max artifact size? | 50 MB per artifact, per gateway configurable (product vision Open Q #7) |
