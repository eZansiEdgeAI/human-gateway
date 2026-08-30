import { beforeEach, describe, expect, it } from 'vitest'
import { resetDatabaseForTests } from './database'
import { deleteConversation, getConversation, listConversations, putConversation } from './conversations'
import { deleteMessage, getMessage, listAllMessages, listMessagesForConversation, putMessage } from './messages'
import { deleteTask, getTask, listTasks, listTasksByStatus, putTask } from './tasks'
import { makeConversationView, makeHumanTask, makeMessageView } from '../test/fixtures'

describe('offline repositories', () => {
  beforeEach(async () => {
    await resetDatabaseForTests()
  })

  describe('conversations', () => {
    it('round-trips a conversation view', async () => {
      const conversation = makeConversationView({ id: 'conv-1' })
      await putConversation(conversation)
      expect(await getConversation('conv-1')).toEqual(conversation)
    })

    it('lists newest first', async () => {
      const older = makeConversationView({ id: 'c1', createdAt: '2026-01-01T00:00:00.000Z' })
      const newer = makeConversationView({ id: 'c2', createdAt: '2026-02-01T00:00:00.000Z' })
      await putConversation(older)
      await putConversation(newer)

      expect((await listConversations()).map((c) => c.id)).toEqual(['c2', 'c1'])
    })

    it('deletes a conversation', async () => {
      await putConversation(makeConversationView({ id: 'conv-1' }))
      await deleteConversation('conv-1')
      expect(await getConversation('conv-1')).toBeUndefined()
    })
  })

  describe('messages', () => {
    it('round-trips a message view with its deliveries', async () => {
      const view = makeMessageView({ message: { ...makeMessageView().message, id: 'msg-1' } })
      await putMessage(view)
      expect(await getMessage('msg-1')).toEqual(view)
    })

    it('lists a conversation in chronological order via the index', async () => {
      const older = makeMessageView({
        message: {
          ...makeMessageView().message,
          id: 'm1',
          conversationId: 'conv-1',
          createdAt: '2026-01-01T00:00:00.000Z',
        },
      })
      const newer = makeMessageView({
        message: {
          ...makeMessageView().message,
          id: 'm2',
          conversationId: 'conv-1',
          createdAt: '2026-02-01T00:00:00.000Z',
        },
      })
      const other = makeMessageView({
        message: {
          ...makeMessageView().message,
          id: 'm3',
          conversationId: 'conv-2',
          createdAt: '2026-03-01T00:00:00.000Z',
        },
      })

      await putMessage(older)
      await putMessage(newer)
      await putMessage(other)

      expect((await listMessagesForConversation('conv-1')).map((v) => v.message.id)).toEqual([
        'm1',
        'm2',
      ])
      expect(await listAllMessages()).toHaveLength(3)
    })

    it('deletes a message', async () => {
      await putMessage(makeMessageView({ message: { ...makeMessageView().message, id: 'msg-1' } }))
      await deleteMessage('msg-1')
      expect(await getMessage('msg-1')).toBeUndefined()
    })
  })

  describe('tasks', () => {
    it('round-trips a task', async () => {
      const task = makeHumanTask({ id: 'task-1' })
      await putTask(task)
      expect(await getTask('task-1')).toEqual(task)
    })

    it('filters tasks by status via the index', async () => {
      await putTask(makeHumanTask({ id: 't1', status: 'REQUESTED' }))
      await putTask(makeHumanTask({ id: 't2', status: 'REQUESTED' }))
      await putTask(makeHumanTask({ id: 't3', status: 'COMPLETED' }))

      expect((await listTasksByStatus('REQUESTED')).map((t) => t.id).sort()).toEqual([
        't1',
        't2',
      ])
      expect(await listTasks()).toHaveLength(3)
    })

    it('deletes a task', async () => {
      await putTask(makeHumanTask({ id: 'task-1' }))
      await deleteTask('task-1')
      expect(await getTask('task-1')).toBeUndefined()
    })
  })
})
