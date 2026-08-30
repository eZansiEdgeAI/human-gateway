import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { registerSW } from 'virtual:pwa-register'
import App from './App'
import './index.css'

// Register the service worker for offline app-shell caching (PWA-FR-01).
// `autoUpdate` + `immediate` installs new precaches and activates them on the
// next load so stale caches never strand the app offline (product vision §12).
registerSW({ immediate: true })

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
