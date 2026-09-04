import { describe, expect, it } from 'vitest'
import {
  translateArtifactReceived,
  translateHumanInteractionCompleted,
  translateHumanInteractionExpired,
  translateHumanInteractionRequested,
  translateHumanResponseReceived,
  type ArtifactReference,
  type HumanInteractionRequest,
  type HumanInteractionResponse,
} from '../src/index.js'

const request: HumanInteractionRequest = {
  task: {
    id: 'task-1', workflowRef: 'run-1', nodeId: 'node-1', kind: 'input',
    role: 'teacher', prompt: 'Provide evidence', subject: 'Lesson',
    correlationToken: 'opaque-token',
  },
}
const response: HumanInteractionResponse = { text: 'done', respondedBy: 'person-1' }
const artifact: ArtifactReference = { id: 'artifact-1', hash: 'sha256:abc', filename: 'evidence.txt' }

describe('human interaction concept translation', () => {
  it('maps all five lifecycle concepts without changing correlation data', () => {
    expect(translateHumanInteractionRequested(request)).toEqual({
      type: 'HumanInteractionRequested', request,
    })
    expect(translateHumanResponseReceived(request, response)).toEqual({
      type: 'HumanResponseReceived', request, response,
    })
    expect(translateHumanInteractionCompleted(request, response)).toEqual({
      type: 'HumanInteractionCompleted', request, response,
    })
    expect(translateArtifactReceived(request, artifact)).toEqual({
      type: 'ArtifactReceived', request, artifact,
    })
    expect(translateHumanInteractionExpired(request, '2026-01-02T00:00:00.000Z')).toEqual({
      type: 'HumanInteractionExpired', request, expiredAt: '2026-01-02T00:00:00.000Z',
    })
  })

  it('preserves an omitted expiry timestamp as undefined', () => {
    expect(translateHumanInteractionExpired(request)).toEqual({
      type: 'HumanInteractionExpired', request, expiredAt: undefined,
    })
  })
})
