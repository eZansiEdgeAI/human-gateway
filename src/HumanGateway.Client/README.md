# HumanGateway.Client

Offline-first Progressive Web App for HumanGateway (product vision §6.2). React + TypeScript + Vite,
installable and fully usable offline via a Workbox Service Worker + IndexedDB (PWA-FR-01).

> **Status:** implemented for the `0.1.0` release. The client includes Service Worker app-shell caching,
> IndexedDB repositories and outbox, offline-first Edge API access, conversations, compose, tasks,
> attachments, authentication, and delivery status.

## Stack

| Component | Version | Note |
|-----------|---------|------|
| React / React DOM | 19.2.8 | |
| Vite | 8.2.2 | rolldown-based |
| @vitejs/plugin-react | 6.1.1 | |
| TypeScript | ~6.0.3 | **JS-based line** (see note below) |
| vite-plugin-pwa (Workbox) | 1.3.0 | `generateSW` app-shell precache |
| Vitest + React Testing Library | 4.1.11 / 16.3.3 | jsdom environment |
| ESLint (typescript-eslint) | 10.9.1 / 8.68.0 | flat config |

## Why TypeScript ~6.0.3 (not 7.x)

The product vision pins **TypeScript 7.x (native compiler)** with a documented fallback to the JS
line if ecosystem compatibility issues arise (Open Q #1). `typescript-eslint` — required for the
`npm run lint` gate — declares `typescript: ">=4.8.4 <6.1.0"`, so the TS 7 native compiler is not
yet supported. We therefore pin the current JS-based line (`~6.0.3`, the same line the current
`create-vite` template ships) so build, lint, and test all pass. Revisit when typescript-eslint adds
TS 7 support.

## Scripts

| Script | Purpose |
|--------|---------|
| `npm run dev` | Vite dev server |
| `npm run build` | `tsc -b && vite build` — type-check then produce `dist/` + service worker |
| `npm run lint` | ESLint (zero errors required) |
| `npm test` | Vitest run (unit/component) |
| `npm run preview` | Serve the production build |
| `node scripts/generate-icons.mjs` | Regenerate PWA icons (zero-dependency PNG encoder) |

## Layout

```text
src/HumanGateway.Client/
├── index.html                 # app shell entry
├── vite.config.ts             # Vite + vite-plugin-pwa + Vitest config
├── eslint.config.js           # flat ESLint (typescript-eslint + react-hooks + react-refresh)
├── tsconfig*.json             # app + node project references
├── public/
│   ├── favicon.svg
│   └── icons/                 # 192/512 install icons (regular + maskable)
├── scripts/generate-icons.mjs # icon generator
└── src/
    ├── main.tsx               # entry; registers the service worker
    ├── App.tsx                # composition root
    ├── components/
    │   ├── AppShell.tsx       # persistent shell (header, main, footer)
    │   └── SyncBanner.tsx     # offline/online status banner
    ├── hooks/useOnlineStatus.ts
    ├── lib/
    │   ├── connectivity.ts    # offline detection source of truth (isOnline / subscribe)
    │   └── version.ts         # APP_VERSION surfaced in the footer
    └── test/setup.ts          # jest-dom matchers
```

## Service worker & offline

`vite-plugin-pwa` runs `generateSW` in `autoUpdate` mode: the app shell is precached with
content-hashed (revisioned) filenames, `cleanupOutdatedCaches` drops superseded precache entries on
activation, and `navigateFallback: 'index.html'` serves the shell for client-side routes. Caches are
namespaced under `cacheId: 'humangateway'`, and `navigateFallbackDenylist: [/^\/api\//]` guarantees
same-origin API routes never fall back to the shell (the Relay may serve both in a later task). The
service worker is registered manually in `main.tsx` so later tasks can surface update/offline state
in the sync banner.

Offline detection lives in `src/lib/connectivity.ts` — a framework-agnostic source of truth
(`isOnline()` + `subscribeConnectivity()`) backed by `navigator.onLine` and the `online`/`offline`
events. The `useOnlineStatus` hook and the offline-first Edge API client both consume it, so
the sync banner and the fetch wrapper never disagree about connectivity.

For user workflows, offline behavior, task responses, and attachment guidance, see the
[user guide](../../../docs/user-guide.md). For deployment and configuration, see the
[administrator guide](../../../docs/admin-guide.md).
