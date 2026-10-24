import { beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionChangedError, refreshSession } from './client'
import { session } from './session'

const post = vi.hoisted(() => vi.fn())
vi.mock('axios', async (importOriginal) => {
  const actual = await importOriginal<typeof import('axios')>()
  const create = () => ({ post, interceptors: { request: { use: vi.fn() }, response: { use: vi.fn() } }, defaults: { headers: { common: {} } } })
  return { ...actual, default: { ...actual.default, create } }
})

const auth = (id: string, token: string) => ({ data: { accessToken: token, expiresAt: '', user: { id, email: `${id}@x`, fullName: id, permissions: [] } } })

describe('refreshing the session', () => {
  beforeEach(() => {
    session.clear()
    post.mockReset()
  })

  it('keeps the tab signed in as the same user', async () => {
    session.set({ accessToken: 'a', user: auth('admin', 'a').data.user } as never)
    post.mockResolvedValue(auth('admin', 'b'))

    await refreshSession()

    expect(session.getAccessToken()).toBe('b')
  })

  it('refuses to hand the tab to another user when the refresh cookie now belongs to someone else', async () => {
    session.set({ accessToken: 'a', user: auth('admin', 'a').data.user } as never)
    post.mockResolvedValue(auth('vendor', 'v'))

    await expect(refreshSession()).rejects.toBeInstanceOf(SessionChangedError)

    expect(session.getAccessToken()).toBe('a') // the vendor's token was never applied
  })

  it('accepts whoever the cookie belongs to when the page has no user yet (reload)', async () => {
    post.mockResolvedValue(auth('admin', 'a'))

    await refreshSession()

    expect(session.getUserId()).toBe('admin')
  })
})
