---
name: define-protocol-model
description: "Defines a new schema-backed data model in HumanGateway: a JSON Schema under schemas/, the .NET entity model + validation in HumanGateway.Protocol, and matching TypeScript types/validators. Use this skill when adding any new protocol entity (e.g. a new message subtype, delivery field, or sync record) that must validate identically across .NET and TypeScript."
---

# Skill: Define a Schema-Backed Protocol Model

Adds a new protocol entity end-to-end: `schemas/` JSON Schema → `HumanGateway.Protocol` .NET model → TypeScript validators, so both runtimes accept identical fixtures (PROTO-FR-01, protocol §6 compatibility).

---

## Process

### Step 1: Author the JSON Schema (`schemas/`)

- Use JSON Schema **Draft 2020-12** (protocol Open Q #1): `$schema: "https://json-schema.org/draft/2020-12/schema"`
- Add `$id` and a semantic version to the document (protocol §7 #1)
- Reference existing schemas by `$ref` (e.g. a Message references Participant, Artifact) - never duplicate shapes
- Keep entities language- and transport-independent (PROTO-FR-06); the schema must not assume .NET or TS

### Step 2: Write Valid/Invalid Fixtures

- Create a fixtures file: one valid instance + one invalid instance per rejection rule
- Cover the entity's invariants: missing IDs rejected, unknown recipient types rejected, hash/size mismatch on Artifact rejected (protocol §6 scenarios)

### Step 3: Implement the .NET Model (`src/HumanGateway.Protocol/`)

- PascalCase types matching the schema property names
- Hand-written validation driven by the shared schema - do NOT generate code from schemas in v1 (protocol Open Q #2 default)
- Validation returns structured errors (see error model in protocol Phase 0)

### Step 4: Implement the TypeScript Types + Validator

- Mirror the schema in TS types and a validator (hand-written, shared-schema-driven)
- Place under `src/HumanGateway.Client/src/protocol/` (or a shared location consumed by Client + Workflow)

### Step 5: Conformance

- Run the same fixtures through BOTH validators; both must accept valid and reject invalid identically (protocol §6)

---

## Output Format

Per entity:

- `schemas/{entity}.schema.json` - Draft 2020-12, versioned, `$ref`-based
- `.NET` model + validator in `src/HumanGateway.Protocol/`
- TS types + validator in the client protocol module
- Fixtures file with valid/invalid cases

---

## Validation

- [ ] `dotnet build src/HumanGateway.Protocol` - zero errors
- [ ] `npm run build` (protocol types) - zero errors
- [ ] Valid fixtures accepted by BOTH validators; invalid fixtures rejected by BOTH (protocol §6)
- [ ] `$id` + version present; `$ref` used instead of duplication

If validation fails, fix the schema/model and re-validate.

---

## Gotchas

- **Draft 2020-12, not older drafts** - older-draft keywords (e.g. `dependencies`) differ; verify before using.
- **Artifacts are referenced, never embedded** - a Message carries artifact ID + hash references, not content (PROTO-FR-04).
- **WAITING_FOR_SYNC is a valid state** - offline queuing is expected, not an error; the schema must permit it.
- **Hand-written validators in v1** - codegen is a later decision (protocol Open Q #2); don't introduce a generator now.
- **`description:` must be double-quoted YAML** in generated files (forge frontmatter gate).

---

## Reference

See [docs/features/protocol.md](../../docs/features/protocol.md) for the full specification:
- **Section 3** - PROTO-FR-01..06 requirements
- **Section 5** - Phase 0 tasks (sync model, identity model, error model)
- **Section 6** - Testing strategy (unit + compatibility)
- **Section 8** - Open Questions (Draft 2020-12, hand-written validators)

For sync-model specifics (IDs, sequence numbers, cursors, idempotency, content hashes), coordinate with **sync-engineer**; the schema documents are owned by **protocol-engineer**.
