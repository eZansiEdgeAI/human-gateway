import { afterEach, describe, expect, it, vi } from 'vitest'
import { dispatchOperation } from './dispatcher'
import { makeSendMessageRequest } from '../test/fixtures'

describe('dispatchOperation', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('posts a sendMessage operation to POST /messages', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ message: { id: 'm1' }, deliveries: [] }), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    const request = makeSendMessageRequest()
    await dispatchOperation({ type: 'sendMessage', request })

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/messages$/),
      expect.objectContaining({ method: 'POST', body: JSON.stringify(request) }),
    )
  })

  it('posts an answerTask operation to POST /tasks/{id}/response with the id URL-encoded', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify({ id: 'task-1' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const request = {
      respondedBy: makeSendMessageRequest().sender,
      text: 'done',
    }
    await dispatchOperation({ type: 'answerTask', taskId: 'task/with space', request })

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/tasks\/task%2Fwith%20space\/response$/),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('posts createConversation, createTask, and registerArtifact to their endpoints', async () => {
    const fetchMock = vi
      .fn()
      .mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ id: 'x' }), { status: 201 })))
    vi.stubGlobal('fetch', fetchMock)

    await dispatchOperation({ type: 'createConversation', request: { participants: [] } })
    await dispatchOperation({
      type: 'createTask',
      request: {
        workflowRef: 'wf',
        nodeId: 'n1',
        prompt: 'go',
        requester: makeSendMessageRequest().sender,
        assignees: [],
      },
    })
    await dispatchOperation({
      type: 'registerArtifact',
      request: { hash: 'sha256:' + '0'.repeat(64), sizeBytes: 1, mimeType: 'image/png', filename: 'a.png' },
    })

    const urls = fetchMock.mock.calls.map((call) => String(call[0]))
    expect(urls[0]).toMatch(/\/conversations$/)
    expect(urls[1]).toMatch(/\/tasks$/)
    expect(urls[2]).toMatch(/\/artifacts$/)
  })
})
