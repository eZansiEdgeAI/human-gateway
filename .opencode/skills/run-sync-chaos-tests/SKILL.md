---
name: run-sync-chaos-tests
description: "Designs and runs chaos tests for HumanGateway sync and durability: network disconnect, duplicate-in-transit, out-of-order arrival, multi-day outage, and process kill / power loss. Use this skill when validating exactly-once delivery and convergence (product vision §11 metrics) on the Edge, Relay, or the Edge↔Relay loop."
---

# Skill: Run Sync / Durability Chaos Tests

Designs and executes chaos scenarios that prove HumanGateway's store-and-forward guarantees: no lost / no duplicate messages and convergence within one sync cycle after reconnect (product vision §11, synchronisation §6).

---

## Process

### Step 1: Pick the Scenario Set

| Scenario | Asserts | Source |
|---|---|---|
| Internet disappears mid-session | Messages queue as WAITING_FOR_SYNC (not FAILED) | synchronisation §6 #1 |
| Internet returns | Convergence within one sync cycle, exactly-once | synchronisation §6 #2 |
| Message duplicated in transit | Deduplicated; user sees exactly one | synchronisation §6 #3 |
| Out-of-order arrival | Reordered deterministically by sequence number | synchronisation §6 #4, SYNC-FR-07 |
| Multi-day offline | Long-disconnect convergence | synchronisation §6 #5, SYNC-FR-06 |
| Process kill during write (`kill -9`) | Restart → message present exactly once | local-edge §6, EDGE-FR-07 |
| Edge killed mid-sync | Restart → reconcile; no loss/duplication | local-edge §6, product vision §10 |

### Step 2: Build the Scenario Harness

- Scriptable, repeatable chaos scripts under `tests/chaos/` (product vision §6.2)
- Use controlled interruption (proxy that can drop/delay/reorder traffic) for transport chaos
- For crash-consistency, kill the Edge with `kill -9` during a write, restart it, and assert state (graceful shutdown is NOT a substitute - local-edge §6)

### Step 3: Assert the Success Metrics

- 0 lost / 0 duplicate messages (NF-05, product vision §11)
- Convergence within one sync cycle after reconnect (product vision §11)
- Delivery states follow the allowed lifecycle; WAITING_FOR_SYNC is deferred, not failed (product vision §10)

### Step 4: Report

- Report failures with the failing requirement ID and a repro script
- Escalate regressions to the owning agent (sync-engineer for convergence/ordering, edge-engineer for crash-consistency)

---

## Output Format

- A `tests/chaos/` scenario script per scenario (repeatable, deterministic where possible)
- A summary asserting the success metrics (lost/duplicate counts, convergence time)
- Regression tickets with repro steps when a scenario fails

---

## Validation

- [ ] `dotnet test` / chaos runner executes every scenario
- [ ] 0 lost / 0 duplicate messages across the chaos suite (product vision §11)
- [ ] Convergence within one sync cycle after reconnect (product vision §11)
- [ ] Crash-restart leaves state exactly-once (local-edge §6)
- [ ] Scenarios are repeatable (rerun passes deterministically where applicable)

If a scenario fails, fix and re-run the full suite before committing.

---

## Gotchas

- **A graceful shutdown is not a power-loss test** - crash-consistency must use `kill -9` mid-write (local-edge §6).
- **Assert metrics, not just "no crash"** - the suite's value is 0 lost / 0 duplicate and convergence time, not merely "the service restarted".
- **Mocking "offline" misses real bugs** - use a real traffic proxy that drops/delays/reorders, and DevTools offline for the PWA (offline-pwa §6).
- **WAITING_FOR_SYNC is expected behaviour** - the suite must not treat offline queuing as an error state.
- **Testcontainers for PostgreSQL-backed scenarios** - don't mock the DB for Edge↔Relay chaos (cloud-relay §6).
- **`description:` must be double-quoted YAML** in generated files (forge frontmatter gate).

---

## Reference

See [docs/features/synchronisation.md](../../docs/features/synchronisation.md) §6 for sync chaos scenarios, [docs/features/local-edge.md](../../docs/features/local-edge.md) §6 for crash-consistency, and [docs/product-vision.md](../../docs/product-vision.md) §11 for the success metrics. The delivery state machine is in product vision §10.
