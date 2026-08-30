/**
 * Shared test fixtures (offline-pwa §6).
 *
 * Small factories that build valid protocol objects so store/outbox tests can
 * exercise the repositories without hand-writing a full envelope every time.
 * They mirror the shapes the Edge returns (verified against the JSON schemas).
 */

import type {
  ConversationView,
  HumanTask,
  Message,
  MessageView,
  Participant,
  SendMessageRequest,
} from '../types/protocol'

/** A 64-hex-character fake content hash (matches `sha256:` schema pattern). */
const FAKE_HASH = 'sha256:' + '0'.repeat(64)

let counter = 0

function nextId(prefix: string): string {
  counter += 1
  return `${prefix}-${String(counter).padStart(4, '0')}`
}

export function makeParticipant(overrides: Partial<Participant> = {}): Participant {
  return {
    address: `human:teacher${counter}@school.example`,
    kind: 'human',
    displayName: 'Teacher',
    ...overrides,
  }
}

export function makeMessage(overrides: Partial<Message> = {}): Message {
  const id = overrides.id ?? nextId('msg')
  return {
    id,
    sender: makeParticipant(),
    recipients: [makeParticipant({ displayName: 'Recipient' })],
    conversationId: nextId('conv'),
    payload: { body: 'Hello, is this thing on?', format: 'plaintext' },
    createdAt: new Date().toISOString(),
    contentHash: FAKE_HASH,
    ...overrides,
  }
}

export function makeMessageView(overrides: Partial<MessageView> = {}): MessageView {
  const message = overrides.message ?? makeMessage()
  const deliveries = overrides.deliveries ?? [
    {
      id: nextId('dlv'),
      messageId: message.id,
      recipient: message.recipients[0],
      state: 'QUEUED' as const,
      attempts: 0,
      maxAttempts: 5,
      queuedAt: message.createdAt,
      createdAt: message.createdAt,
      updatedAt: message.createdAt,
    },
  ]
  return { message, deliveries, ...overrides }
}

export function makeConversationView(
  overrides: Partial<ConversationView> = {},
): ConversationView {
  return {
    id: nextId('conv'),
    title: 'Sample conversation',
    participants: [makeParticipant()],
    messageCount: 0,
    createdAt: new Date().toISOString(),
    ...overrides,
  }
}

export function makeHumanTask(overrides: Partial<HumanTask> = {}): HumanTask {
  return {
    id: nextId('task'),
    kind: 'input',
    status: 'REQUESTED',
    workflowRef: 'wf-1',
    nodeId: 'node-1',
    prompt: 'Please upload your attendance photo.',
    requestMessageId: nextId('msg'),
    requestedAt: new Date().toISOString(),
    createdAt: new Date().toISOString(),
    ...overrides,
  }
}

export function makeSendMessageRequest(
  overrides: Partial<SendMessageRequest> = {},
): SendMessageRequest {
  return {
    sender: makeParticipant(),
    recipients: [makeParticipant({ displayName: 'Recipient' })],
    conversationId: nextId('conv'),
    payload: { body: 'Queued offline.' },
    ...overrides,
  }
}
