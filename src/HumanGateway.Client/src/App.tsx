import { useState, type ReactNode } from 'react'
import { AppShell } from './components/AppShell'
import { AppStoreProvider } from './store/AppStore'
import { useAppStore } from './store/context'
import { ConversationList } from './components/ConversationList'
import { MessageThread } from './components/MessageThread'
import { ComposeMessage } from './components/ComposeMessage'
import { TaskList } from './components/TaskList'
import { TaskView } from './components/TaskView'

type View =
  | { name: 'inbox' }
  | { name: 'thread'; conversationId: string }
  | { name: 'compose'; conversationId?: string }
  | { name: 'tasks' }
  | { name: 'task'; taskId: string }

export default function App() {
  return (
    <AppStoreProvider>
      <Workspace />
    </AppStoreProvider>
  )
}

/**
 * The composition root: simple state-based navigation between Inbox, Thread,
 * Compose, Tasks, and Task detail (no router dependency — offline-pwa Open
 * Q #1). Data and actions come from the app store.
 */
function Workspace() {
  const store = useAppStore()
  const [view, setView] = useState<View>({ name: 'inbox' })

  const openThread = async (conversationId: string) => {
    await store.openConversation(conversationId)
    setView({ name: 'thread', conversationId })
  }

  const inMessages = view.name !== 'tasks' && view.name !== 'task'

  let content: ReactNode

  if (view.name === 'thread') {
    content = (
      <MessageThread
        messages={store.threads[view.conversationId] ?? []}
        onReply={() => setView({ name: 'compose', conversationId: view.conversationId })}
        onBack={() => setView({ name: 'inbox' })}
      />
    )
  } else if (view.name === 'compose') {
    const conversation = view.conversationId
      ? store.conversations.find((candidate) => candidate.id === view.conversationId)
      : undefined
    content = (
      <ComposeMessage
        conversation={conversation}
        online={store.online}
        sendMessage={store.sendMessage}
        createConversation={store.createConversation}
        onSent={(outcome) => setView({ name: 'thread', conversationId: outcome.conversationId })}
        onCancel={() =>
          setView(view.conversationId ? { name: 'thread', conversationId: view.conversationId } : { name: 'inbox' })
        }
      />
    )
  } else if (view.name === 'tasks') {
    content = (
      <TaskList tasks={store.tasks} onSelectTask={(taskId) => setView({ name: 'task', taskId })} />
    )
  } else if (view.name === 'task') {
    const task = store.tasks.find((candidate) => candidate.id === view.taskId)
    content = task ? (
      <TaskView
        task={task}
        online={store.online}
        answerTask={store.answerTask}
        onAnswered={() => setView({ name: 'tasks' })}
        onBack={() => setView({ name: 'tasks' })}
      />
    ) : (
      <p className="app-error" role="alert">
        Task not found.
      </p>
    )
  } else {
    content = (
      <ConversationList
        conversations={store.conversations}
        latestDeliveryByConversation={store.latestDeliveryByConversation}
        onSelectConversation={(id) => void openThread(id)}
        onComposeNew={() => setView({ name: 'compose' })}
      />
    )
  }

  return (
    <AppShell>
      <nav className="app-nav" aria-label="Primary">
        <button
          type="button"
          className="app-nav__tab"
          aria-current={inMessages ? 'page' : undefined}
          onClick={() => setView({ name: 'inbox' })}
        >
          Inbox
        </button>
        <button
          type="button"
          className="app-nav__tab"
          aria-current={!inMessages ? 'page' : undefined}
          onClick={() => setView({ name: 'tasks' })}
        >
          Tasks
        </button>
      </nav>

      {store.loading && store.conversations.length === 0 && (
        <p className="app-status" role="status">
          Loading…
        </p>
      )}
      {store.error && (
        <p className="app-error" role="alert">
          {store.error.message}
        </p>
      )}
      {content}
    </AppShell>
  )
}
