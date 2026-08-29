# HumanGateway Protocol Schemas

**Release:** v1.0.0 (2026-08-28) · **JSON Schema:** Draft 2020-12

Transport-agnostic JSON schemas for the HumanGateway message protocol — the single
source of truth that every component validates against (NF-06). The protocol is
language- and transport-independent: JSON over HTTP v1 now, adapters later
(PROTO-FR-06).

## Files

| File | Entity | Requirement |
|------|--------|-------------|
| `common.schema.json` | Shared definitions (IDs, timestamps, content hashes, addresses, errors, correlation tokens) | — |
| `error.schema.json` | Error — protocol error model + reserved code catalog | protocol §7 #3 |
| `gateway.schema.json` | Gateway — Edge Gateway identity + registration record | AUTH-FR-01, SP-02 |
| `user.schema.json` | User — local Edge user account | AUTH-FR-02, SP-03 |
| `participant.schema.json` | Participant — typed `human:` / `agent:` / `system:` address | PROTO-FR-02 |
| `message.schema.json` | Message — durable envelope (ID, sender, recipients, conversation, workflow/task refs, payload, artifact refs, timestamps) | PROTO-FR-03, PROTO-FR-04 |
| `artifact.schema.json` | Artifact — content object referenced by ID + hash, never embedded | PROTO-FR-04 |
| `delivery.schema.json` | Delivery — per-recipient lifecycle record | PROTO-FR-05 |
| `humantask.schema.json` | HumanTask — workflow primitive (input / approval) | FLOW-FR-04, FLOW-FR-05 |
| `syncbatch.schema.json` | SyncBatch — cursor-based incremental sync batch | SYNC-FR-01..07 |

## Versioning

- Each document carries a versioned `$id`:
  `https://schemas.humangateway.dev/human-gateway/v1/{name}.schema.json`.
- A new release bumps the `/vN/` path segment; schemas are immutable once
  released. In-repo, the `v1` release corresponds to this directory.
- `$comment` records the release version and date per document.

## Cross-references

- `message.schema.json` → `participant.schema.json`, `artifact.schema.json`
- `delivery.schema.json` → `participant.schema.json`, `common.schema.json`
- `humantask.schema.json` → `participant.schema.json`, `artifact.schema.json`
- `syncbatch.schema.json` → `message.schema.json`, `delivery.schema.json`,
  `artifact.schema.json`, `participant.schema.json`
- `common.schema.json` → `error.schema.json`
- `participant.schema.json` → `common.schema.json` (`gatewayId` → `gateway.schema.json`,
  `userId` → `user.schema.json`)
- All entities → `common.schema.json`

References are relative to the document `$id` base URI, e.g.
`$ref: "common.schema.json#/$defs/id"`. Validators must register every schema
file under its `$id` before compiling an entity schema.

## Identity model (AUTH-FR-01, AUTH-FR-02, SP-02, SP-03)

Three kinds of identity are modelled. **Participants** are the only identity
carried inside message envelopes; **gateways** authenticate Edge↔Relay
transport; **users** are the local accounts that humans log in with.

| Concept | Where | Semantics |
|---------|-------|-----------|
| Gateway identity | `gateway.schema.json`, `syncbatch.gatewayId`, `participant.gatewayId` (system participants) | Unique durable `gatewayId` + high-entropy registration token (`$defs.registrationToken`). The Relay stores only a SHA-256 fingerprint of the token and rejects unregistered / suspended / revoked gateways (SP-02). The plaintext token lives only in the Edge secret store (SP-07) and is used to derive signed request tokens for every Edge↔Relay request over TLS (AUTH-FR-04, SP-01). Registration lifecycle: `UNREGISTERED → PENDING → REGISTERED → SUSPENDED \| REVOKED` |
| Participant | `participant.schema.json` | Typed address `human:` / `agent:` / `system:` (PROTO-FR-02). The only identity in envelopes. Optional `userId` (→ `user.schema.json`) on humans and optional `gatewayId` (→ `gateway.schema.json`) on system gateways tie it to the other identity records |
| User | `user.schema.json`, `participant.userId` | Local Edge account: `username` + `passwordVerifier` (PHC string). The verifier is a **local-store-only** field — never transmitted in any protocol payload and never logged (SP-07). Sessions are signed opaque tokens (v1; JWT only if a consumer needs it) |
| Correlation tokens | `common.$defs.correlationTokens`, `message.correlationTokens`, `humantask.correlationToken` | Opaque consumer (FlowForge) tokens passed through unchanged so consumers enforce role-checks and audit (SP-09, AUTH-FR-06) |

HumanGateway performs **no role-checking** (SP-09): authorisation is enforced by
the Edge/Relay per conversation/task/artifact (SP-04, AUTH-FR-03) and consumer
role/audit is delegated to FlowForge via correlation tokens.

## Error model (protocol §7 #3)

`error.schema.json` is the single source of truth. It is referenced by
`common.$defs.error` (used by `delivery.schema.json` `FAILED` records and by
sync/API error responses), so every component surfaces identical errors:

- `code` — stable machine-readable token (UPPER_SNAKE, ≤ 64 chars). The reserved
  catalog is enumerated in `$defs.errorCode`; codes are **added, never changed or
  removed**. Consumers/components may extend the catalog with the same token shape.
- `message` — human-readable description, safe to display.
- `details` — optional structured fields (e.g. expected vs received content hash).
- `retryable` — retry hint: true for transient conditions (timeout, rate limit),
  false for permanent rejections (auth, authorisation, validation, hash mismatch).

Security: error payloads must never carry secrets, registration/session tokens,
or password material (SP-07).

## Sync model (SYNC-FR-01..07)

Encoded in `syncbatch.schema.json` and the entities it carries:

| Concept | Where | Semantics |
|---------|-------|-----------|
| Durable message IDs | `message.id` (also `batchId`, `gatewayId`, `delivery.id`, `artifact.id`) | Never reused; receivers deduplicate on them (at-least-once → exactly-once effect) |
| Per-gateway sequence numbers | `syncItem.sequence`, `sequenceStart` / `sequenceEnd` | Monotonic per gateway; the deterministic ordering key `(gatewayId, sequence)` (gaps allowed, contiguity not required). A non-empty batch MUST declare its `sequenceStart..sequenceEnd` span |
| Cursors | `sinceCursor` / `cursor` (`$defs/cursor`) | Opaque, URL-safe, ≤ 1024 chars; issued by the receiving side and only echoed back, never interpreted. `null` = no position yet (first exchange). `cursor` covers all items in the batch; an empty batch is a valid keepalive that still advances it |
| Idempotency | `batchId` + `idempotencyKey` (both required) | Replayed batches have no duplicate effect. A retry MUST reuse the same `batchId` AND `idempotencyKey`; changing either creates a new logical batch |
| Content hashes | `message.contentHash`, `artifact.hash` | `sha256:<hex>`; computed over the canonical encoding (excluding `contentHash` itself) and verified by both peers (tamper/corruption detection) |

Batch-shape invariants enforced by the schema:

- Empty `items` (keepalive) ⇒ `sequenceStart`/`sequenceEnd` MUST be `null`.
- Non-empty `items` ⇒ `sequenceStart`/`sequenceEnd` MUST be integers
  (`sequenceStart <= sequenceEnd`; every item's `sequence` falls within the
  span — the ≤ / containment checks are enforced by the sync engine, since
  JSON Schema cannot compare two instance fields).
- `items` is capped at 1000 entries per batch (maxItems).

Artifact bytes travel over the separate chunked artifact-transfer channel;
`syncItem.artifact` carries metadata only (dedup + verification).

## State machines

### Message delivery (PROTO-FR-05, product vision §10)

```text
QUEUED ─▶ SYNCING ─▶ DELIVERED ─▶ ACKNOWLEDGED
   │          │
   ▼          ▼
WAITING_FOR_SYNC ─▶ (retry) ─▶ SYNCING ... ─▶ FAILED (after max retries, with alert)
```

- `WAITING_FOR_SYNC` is a **valid state**, not an error: offline-queued delivery
  is expected behaviour.
- `delivery.schema.json` enforces snapshot consistency (e.g. `FAILED` requires
  `error` + `failedAt`). Transition legality is enforced by the sync engine.

### Human task (product vision §10)

```text
REQUESTED → DELIVERED_TO_HUMAN → RESPONSE_RECEIVED → COMPLETED | EXPIRED
```

- `kind: input` answers carry `response.text` (+ optional artifacts).
- `kind: approval` answers carry `response.decision` (`approved` | `rejected`)
  plus an optional `reason`.

## Validation

Unit fixtures live under `tests/schemas/fixtures/`. Run:

```bash
cd tests/schemas
npm install          # ajv + ajv-formats (devDependencies)
npm test             # compiles all schemas, runs valid + invalid fixtures
```

Cross-implementation conformance (identical fixtures accepted by the .NET
`HumanGateway.Protocol` and TypeScript validators) is part of the protocol
testing strategy (§6).
