# HumanGateway.Protocol

Schema-backed entity model and validation for the HumanGateway protocol (product vision §6.2:
"entity model + schemas + validation").

- **Target framework:** .NET 10 (LTS)
- **Protocol release:** v1.0.0 (2026-08-28) · **JSON Schema:** Draft 2020-12
- **Single source of truth:** `schemas/` at the repo root (NF-06). The schemas are **embedded** in this
  assembly and exposed via `ProtocolSchemas`; the hand-written validators mirror the schema rules and are
  proven against the same fixture set as the JSON Schema validator (protocol §6 conformance).

## Entities

| .NET type | Schema | Requirement |
|-----------|--------|-------------|
| `Models.ProtocolError` | `error.schema.json` | protocol §7 #3 |
| `Models.Gateway` | `gateway.schema.json` | AUTH-FR-01, SP-02 |
| `Models.User` | `user.schema.json` | AUTH-FR-02, SP-03 |
| `Models.Participant` | `participant.schema.json` | PROTO-FR-02 |
| `Models.Artifact`, `Models.ArtifactReference` | `artifact.schema.json` | PROTO-FR-04 |
| `Models.Message` | `message.schema.json` | PROTO-FR-03 |
| `Models.Delivery` | `delivery.schema.json` | PROTO-FR-05 |
| `Models.HumanTask` | `humantask.schema.json` | FLOW-FR-04/05 |
| `Models.SyncBatch`, `Models.SyncItem`, `Models.DeliveryAck` | `syncbatch.schema.json` | SYNC-FR-01..07 |

## Usage

```csharp
using HumanGateway.Protocol.Models;
using HumanGateway.Protocol.Validation;

var message = JsonSerializer.Deserialize<Message>(json, ProtocolJson.Options);
ProtocolValidationResult result = ProtocolValidator.Default.Message.Validate(message);

if (result.IsValid) { /* accept */ }
else { /* structured errors: result.Errors (code, path, message) */ }
// or: result.ThrowIfInvalid();  ->  ProtocolValidationException
```

- **`ProtocolJson.Options`** — strict wire contract: exact property names, enum wire tokens both ways,
  unknown properties rejected (`additionalProperties: false`), nulls omitted on write (byte-identical
  round-trips).
- **`ProtocolValidator.Default`** — composite facade with one validator per entity; nested entities are
  validated through the same shared validators (mirroring the `$ref` graph).
- **`ProtocolSchemas.Documents`** — the versioned schema documents embedded in the assembly, keyed by `$id`.
- **`ErrorCodes`** — the reserved error-code catalog (`error.schema.json#/$defs/errorCode`).

## Design notes

- **Hand-written validators, not codegen** (protocol Open Q #2 default). Each validator encodes the schema
  rules; the embedded schemas are the reference and packaging artifact, not the runtime engine.
- **Identity model** — Gateway / Participant / User map to `gateway.schema.json`, `participant.schema.json`,
  `user.schema.json`; participants are the only identity in envelopes (SP-09).
- **Error model** — `ProtocolError` mirrors `error.schema.json`; `ErrorCodes` exposes the reserved catalog.
- **Sync model** — `SyncBatch` encodes durable IDs, per-gateway sequences, opaque cursors, idempotency
  (batchId + idempotencyKey), and content hashes. Cross-field sequence-range checks are the sync engine's
  job (schemas/README.md), matching the schema's own boundary.
- **WAITING_FOR_SYNC** is a valid delivery state — offline deferral is expected, never an error.

## Validation

```bash
dotnet build src/HumanGateway.Protocol                       # library: zero errors
dotnet test tests/HumanGateway.Protocol.Tests                # conformance + round-trip + rule tests
```

The test project runs the **identical fixtures** as `tests/schemas` (valid.json / invalid/*.json): valid
fixtures must validate, invalid fixtures must be rejected, and every valid fixture must round-trip
deserialize→serialize→equal (protocol §6). Cross-implementation conformance with the TypeScript validators
is part of the protocol testing strategy (§6).
