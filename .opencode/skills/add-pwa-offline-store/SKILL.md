---
name: add-pwa-offline-store
description: "Adds an offline-capable data domain to the HumanGateway PWA: an IndexedDB store, an offline-first fetch wrapper path, service-worker caching, and the UI that reads/writes it. Use this skill when adding any new offline feature to the client (e.g. a new conversation type, task response flow, or artifact list) that must work with the network disconnected."
---

# Skill: Add an Offline-Capable PWA Store / Flow

Adds a new offline-capable data domain to `src/HumanGateway.Client`: IndexedDB store, offline-first write path (outbox), service-worker caching, and the UI binding (PWA-FR-01/02).

---

## Process

### Step 1: Model the Data (IndexedDB)

- Create an IndexedDB store module for the domain (conversations, messages, tasks, outbox, ...)
- Key by the protocol entity's durable ID; store the full validated shape (PWA-FR-02)
- React Context + hooks + plain TS modules - no heavy store dependency (offline-pwa Open Q #1)

### Step 2: Add the Offline-First Write Path

- All mutations go through an offline-first fetch wrapper: if the Edge is reachable, call the Edge API; if offline, queue the operation to the IndexedDB outbox (PWA-FR-02)
- Persist to IndexedDB BEFORE attempting the network call - local-first ordering (EDGE-FR-04 mirrors this on the server)
- When connectivity returns, flush the outbox to the Edge (PWA lifecycle ONLINE → flush, product vision §10)

### Step 3: Service-Worker Caching

- Cache the app shell + the domain's read data via Workbox with **versioned caches** (PWA-FR-01)
- Cache-bust on version change; stale caches must never serve old shell data (product vision §12 risk)

### Step 4: Build the UI

- Wire the view to the store; use the shared protocol types (from **protocol-engineer**) for message/task shapes
- Surface sync state through the sync banner: "queued, will sync when connected" - not an error (offline-pwa §4)
- Delivery status per message uses icon + text, not colour alone (ACC-03)
- Touch targets ≥ 44×44 px; keyboard navigable (ACC-01/04)

---

## Output Format

- An IndexedDB store module under `src/HumanGateway.Client/src/`
- Offline-first wrapper path (or extension of the existing Edge API client)
- Service-worker cache rule (versioned)
- UI component(s) reading/writing the store

---

## Validation

- [ ] `npm run build` - zero errors
- [ ] `npm test` (Vitest + React Testing Library) - store logic, outbox queue, API client pass (offline-pwa §6)
- [ ] App shell loads from the Service Worker with no network (offline-pwa §6)
- [ ] Compose/act offline → queued in IndexedDB → appears after reconnect when Edge reachable (offline-pwa §6)
- [ ] Task answered offline with a photo attachment queues and delivers on reconnect (offline-pwa §6)
- [ ] a11y checks: keyboard nav, ARIA, contrast, touch target size (ACC-01..04)

If validation fails, fix and re-validate.

---

## Gotchas

- **Versioned service-worker caches are mandatory** - unversioned caches break offline updates and serve stale shell data (PWA-FR-01, product vision §12 risk).
- **Persist before network** - local-first ordering; if the app writes optimistically and the write is lost on kill, the offline guarantee breaks (PWA-FR-02).
- **No heavy state library** - Context + hooks + plain TS modules for v1 (offline-pwa Open Q #1).
- **Polling, not WebSockets, in v1** - HTTP polling with backoff for new messages (product vision Open Q #10).
- **Status not colour-only** - always pair colour with icon/text (ACC-03).
- **`description:` must be double-quoted YAML** in generated files (forge frontmatter gate).

---

## Reference

See [docs/features/offline-pwa.md](../../docs/features/offline-pwa.md) for the full specification:
- **Section 3** - PWA-FR-01..07 requirements
- **Section 4** - UI/Interaction Design (views, sync banner, install prompt)
- **Section 5** - Phase 2 tasks
- **Section 6** - Testing strategy
- **Section 8** - Open Questions (state management, polling)

Accessibility requirements are in [docs/product-vision.md](../../docs/product-vision.md) §9 (ACC-01..04).
