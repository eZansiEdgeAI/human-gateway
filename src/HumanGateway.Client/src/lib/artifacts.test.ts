import { describe, expect, it } from 'vitest'
import { buildArtifactReference, hashFile } from './artifacts'

// SHA-256 of the ASCII bytes "hello".
const SHA256_HELLO = 'sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824'

describe('hashFile', () => {
  it('computes a sha256:<hex> content hash over the file bytes', async () => {
    const file = new File(['hello'], 'hello.txt', { type: 'text/plain' })
    await expect(hashFile(file)).resolves.toBe(SHA256_HELLO)
  })
})

describe('buildArtifactReference', () => {
  it('builds a reference with id, hash, filename, mime type, and size', async () => {
    const file = new File(['hello'], 'photo.jpg', { type: 'image/jpeg' })
    const ref = await buildArtifactReference(file)

    expect(ref.hash).toBe(SHA256_HELLO)
    expect(ref.filename).toBe('photo.jpg')
    expect(ref.mimeType).toBe('image/jpeg')
    expect(ref.sizeBytes).toBe(5)
    expect(ref.id).toBeTruthy()
  })

  it('falls back to application/octet-stream for an unknown type', async () => {
    const file = new File(['x'], 'noext', {})
    const ref = await buildArtifactReference(file)
    expect(ref.mimeType).toBe('application/octet-stream')
  })
})
