/**
 * Artifact attachment picker (PWA-FR-04, offline-pwa §4 Compose view).
 *
 * Captures files (photo, PDF, document, audio) from the device — via the OS
 * file picker or, on mobile, the camera — and hashes them into protocol
 * `ArtifactReference`s (id + sha256 hash, never bytes; PROTO-FR-04). The
 * parent owns the selected list so the Compose view can attach the references
 * to the message envelope. Byte upload to the Edge artifact store lands with
 * the artifact-engineer task; the reference itself is all the envelope needs.
 */

import { useRef } from 'react'
import type { ArtifactReference } from '../types/protocol'
import { buildArtifactReference } from '../lib/artifacts'

/** A selected file plus its computed protocol reference. */
export interface PendingAttachment {
  ref: ArtifactReference
  name: string
  sizeBytes: number
}

export interface ArtifactPickerProps {
  attachments: PendingAttachment[]
  onAttachmentsChange: (attachments: PendingAttachment[]) => void
}

export function ArtifactPicker({ attachments, onAttachmentsChange }: ArtifactPickerProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const cameraInputRef = useRef<HTMLInputElement>(null)

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return
    const added: PendingAttachment[] = []
    for (const file of Array.from(files)) {
      added.push({ ref: await buildArtifactReference(file), name: file.name, sizeBytes: file.size })
    }
    onAttachmentsChange([...attachments, ...added])
  }

  const removeAt = (index: number) => {
    onAttachmentsChange(attachments.filter((_, i) => i !== index))
  }

  return (
    <div className="artifact-picker">
      <div className="artifact-picker__actions">
        <button
          type="button"
          className="button button--secondary"
          onClick={() => fileInputRef.current?.click()}
        >
          Attach file
        </button>
        <button
          type="button"
          className="button button--secondary"
          onClick={() => cameraInputRef.current?.click()}
        >
          Take photo
        </button>
        {/* General file picker (photo/PDF/document/audio). */}
        <input
          ref={fileInputRef}
          type="file"
          multiple
          accept="image/*,application/pdf,audio/*,.doc,.docx,.txt"
          className="artifact-picker__input"
          onChange={(event) => {
            void handleFiles(event.target.files)
            event.target.value = ''
          }}
        />
        {/* Camera capture (mobile); ignored on desktop. */}
        <input
          ref={cameraInputRef}
          type="file"
          accept="image/*"
          capture="environment"
          className="artifact-picker__input"
          onChange={(event) => {
            void handleFiles(event.target.files)
            event.target.value = ''
          }}
        />
      </div>

      {attachments.length > 0 && (
        <ul className="artifact-picker__list" aria-label="Attached files">
          {attachments.map((attachment, index) => (
            <li key={attachment.ref.id} className="artifact-picker__item">
              <span className="artifact-picker__name">{attachment.name}</span>
              <span className="artifact-picker__size">{formatSize(attachment.sizeBytes)}</span>
              <button
                type="button"
                className="artifact-picker__remove"
                onClick={() => removeAt(index)}
                aria-label={`Remove ${attachment.name}`}
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
