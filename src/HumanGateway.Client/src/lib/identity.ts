/**
 * Local (on-device) participant identity (offline-pwa §4 Compose view).
 *
 * The full identity model (gateway/user authentication) lands with the
 * identity-security feature; until then the PWA needs a *sender* to compose
 * messages, so it keeps a single local participant. A settings screen can
 * persist an override via `setLocalParticipant`; otherwise a documented
 * classroom default is used. The typed address (`human:`/`agent:`/`system:`
 * prefix) is the only identity carried in a message envelope (PROTO-FR-02).
 */

import type { Participant, ParticipantKind } from '../types/protocol'

const STORAGE_KEY = 'humangateway.localParticipant'

/** Default sender when no local participant has been configured. */
export const DEFAULT_LOCAL_PARTICIPANT: Participant = {
  address: 'human:teacher@school.example',
  kind: 'human',
  displayName: 'Teacher',
}

/** Reads the configured local participant (override or default). */
export function getLocalParticipant(): Participant {
  try {
    if (typeof localStorage !== 'undefined') {
      const raw = localStorage.getItem(STORAGE_KEY)
      if (raw) {
        const parsed = JSON.parse(raw) as Participant
        if (parsed && typeof parsed.address === 'string' && parsed.address) {
          return parsed
        }
      }
    }
  } catch {
    // Fall through to the default on malformed/unavailable storage.
  }
  return DEFAULT_LOCAL_PARTICIPANT
}

/** Persists a local-participant override for the current origin. */
export function setLocalParticipant(participant: Participant): void {
  try {
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(participant))
    }
  } catch {
    // Non-fatal: the default remains in effect.
  }
}

/**
 * Infers the participant `kind` from a typed address prefix (PROTO-FR-02:
 * prefix must agree with `kind`).
 */
export function inferKind(address: string): ParticipantKind {
  if (address.startsWith('agent:')) return 'agent'
  if (address.startsWith('system:')) return 'system'
  return 'human'
}

/**
 * Parses a free-text recipient list (comma/space separated typed addresses)
 * into `Participant`s. Empty tokens are ignored; display names default to the
 * address until the Edge's participant directory resolves richer metadata.
 */
export function parseParticipantAddresses(text: string): Participant[] {
  const tokens = text
    .split(/[,\s]+/)
    .map((token) => token.trim())
    .filter((token) => token.length > 0)

  const seen = new Set<string>()
  const participants: Participant[] = []
  for (const address of tokens) {
    if (seen.has(address)) continue
    seen.add(address)
    participants.push({ address, kind: inferKind(address), displayName: address })
  }
  return participants
}
