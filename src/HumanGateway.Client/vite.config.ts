import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
// https://vite-pwa-org.netlify.app/guide/
export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      // Service worker is registered manually in src/main.tsx so the app can
      // surface update/offline state in the sync banner later (offline-pwa §4).
      registerType: 'autoUpdate',
      injectRegister: null,
      includeAssets: ['favicon.svg', 'icons/icon-192.png', 'icons/icon-512.png'],
      manifest: {
        id: '/',
        name: 'HumanGateway',
        short_name: 'HumanGateway',
        description:
          'Offline-first human messaging and task gateway for low-connectivity sites.',
        theme_color: '#1d4ed8',
        background_color: '#0b1220',
        display: 'standalone',
        orientation: 'any',
        start_url: '/',
        scope: '/',
        icons: [
          {
            src: 'icons/icon-192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: 'icons/icon-512.png',
            sizes: '512x512',
            type: 'image/png',
          },
          {
            src: 'icons/icon-maskable-192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'maskable',
          },
          {
            src: 'icons/icon-maskable-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        // Precaches the app shell (HTML/CSS/JS/assets) so the PWA loads with no
        // network (PWA-FR-01). Versioned caches come from three mechanisms:
        //  1. Vite content-hashes every filename, so each release produces a
        //     fresh revisioned precache manifest.
        //  2. `cleanupOutdatedCaches` deletes superseded precache entries on
        //     activation, so a stale cache can never strand the app offline
        //     (product vision §12 cache-busting risk).
        //  3. `cacheId` namespaces all caches under `humangateway-`, so they are
        //     clearly identifiable and never collide with another app's caches
        //     on the same origin.
        cacheId: 'humangateway',
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        navigateFallback: 'index.html',
        // Safety: never fall back to the app shell for same-origin API routes.
        // The Edge REST API lives on a different origin, but the Relay may serve
        // both the PWA and a same-origin proxy in a later task (WEBX-FR-01).
        navigateFallbackDenylist: [/^\/api\//],
        cleanupOutdatedCaches: true,
        clientsClaim: true,
        skipWaiting: true,
      },
    }),
  ],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
