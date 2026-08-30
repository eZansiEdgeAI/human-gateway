/**
 * Client-side artifact helpers (PWA-FR-04, PROTO-FR-04).
 *
 * A message references an artifact by id + hash — never by bytes — so a teacher
 * can attach a photo/PDF/document/audio while composing and the reference is
 * what travels in the envelope. The content hash is SHA-256 over the file
 * bytes (`sha256:<hex>`, matching the protocol), so the Edge can verify bytes
 * on download (identity-security phase). WebCrypto `subtle.digest` requires a
 * secure context — the PWA already requires HTTPS/localhost for its service
 * worker, so this is always available where the app runs.
 */

import type { ArtifactReference } from '../types/protocol'
import { newId } from './id'

/** Computes a `sha256:<hex>` content hash over the file bytes. */
export async function hashFile(file: Blob): Promise<string> {
  const subtle = globalThis.crypto?.subtle
  if (!subtle) {
    throw new Error('SHA-256 hashing requires a secure context (HTTPS or localhost).')
  }
  const digest = await subtle.digest('SHA-256', await file.arrayBuffer())
  return `sha256:${toHex(new Uint8Array(digest))}`
}

/** Builds a protocol `ArtifactReference` for an attached file. */
export async function buildArtifactReference(file: File): Promise<ArtifactReference> {
  return {
    id: newId(),
    hash: await hashFile(file),
    filename: file.name,
    mimeType: file.type || 'application/octet-stream',
    sizeBytes: file.size,
  }
}

function toHex(bytes: Uint8Array): string {
  let hex = ''
  for (const byte of bytes) hex += byte.toString(16).padStart(2, '0')
  return hex
}
