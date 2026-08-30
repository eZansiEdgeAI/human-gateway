import { describe, expect, it } from 'vitest'
import { makeOptimisticMessageView, PLACEHOLDER_CONTENT_HASH } from './optimistic'
import { makeSendMessageRequest } from '../test/fixtures'

describe('makeOptimisticMessageView', () => {
  it('keys the draft by the local id and copies the request fields', () => {
    const request = makeSendMessageRequest()
    const view = makeOptimisticMessageView(request, 'draft-1')

    expect(view.message.id).toBe('draft-1')
    expect(view.message.sender).toEqual(request.sender)
    expect(view.message.recipients).toEqual(request.recipients)
    expect(view.message.conversationId).toBe(request.conversationId)
    expect(view.message.payload).toEqual(request.payload)
    expect(view.message.contentHash).toBe(PLACEHOLDER_CONTENT_HASH)
  })

  it('creates a QUEUED delivery per recipient', () => {
    const request = makeSendMessageRequest()
    const view = makeOptimisticMessageView(request, 'draft-1')

    expect(view.deliveries).toHaveLength(request.recipients.length)
    expect(view.deliveries.every((delivery) => delivery.state === 'QUEUED')).toBe(true)
    expect(view.deliveries.every((delivery) => delivery.messageId === 'draft-1')).toBe(true)
  })
})
