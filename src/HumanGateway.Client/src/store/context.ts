/**
 * App store context, types, and hook (offline-pwa Open Q #1).
 *
 * The context + `useAppStore` live separately from the provider component so
 * `AppStore.tsx` exports only a component (keeps fast refresh happy). Types are
 * shared here too; they are erased at build time and carry no runtime cost.
 */

import { createContext, useContext } from 'react'
import type {
  AnswerTaskRequest,
  ConversationView,
  CreateConversationRequest,
  DeliveryState,
  HumanTask,
  MessageView,
  ProtocolError,
  SendMessageRequest,
} from '../types/protocol'

/** Result of composing a message through the store. */
export interface SendOutcome {
  disposition: 'sent' | 'queued'
  /** The conversation the message was sent into (for navigation). */
  conversationId: string
  /** The optimistic draft shown immediately (its delivery state is QUEUED). */
  message: MessageView
}

/** Result of answering a task through the store. */
export interface TaskAnswerOutcome {
  disposition: 'sent' | 'queued'
  /** The task that was answered (for navigation back to the task list). */
  taskId: string
}

/** The store's public surface, provided by {@link AppStoreProvider}. */
export interface AppStoreValue {
  online: boolean
  loading: boolean
  error: ProtocolError | null
  conversations: ConversationView[]
  threads: Readonly<Record<string, MessageView[]>>
  tasks: HumanTask[]
  /** Summary delivery status of each conversation's newest message (Inbox). */
  latestDeliveryByConversation: Readonly<Record<string, DeliveryState>>
  refresh: () => Promise<void>
  openConversation: (conversationId: string) => Promise<void>
  sendMessage: (request: SendMessageRequest) => Promise<SendOutcome>
  createConversation: (request: CreateConversationRequest) => Promise<ConversationView>
  loadTasks: () => Promise<void>
  answerTask: (taskId: string, request: AnswerTaskRequest) => Promise<TaskAnswerOutcome>
}

export const AppStoreContext = createContext<AppStoreValue | null>(null)

/** Reads the app store; throws when used outside the provider. */
export function useAppStore(): AppStoreValue {
  const store = useContext(AppStoreContext)
  if (!store) {
    throw new Error('useAppStore must be used within an AppStoreProvider.')
  }
  return store
}
