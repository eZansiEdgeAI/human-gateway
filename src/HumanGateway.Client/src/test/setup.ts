import '@testing-library/jest-dom/vitest'
// Provide a real IndexedDB implementation in the jsdom test environment
// (offline-pwa §6 — store/outbox logic is unit-tested against fake-indexeddb).
import 'fake-indexeddb/auto'
