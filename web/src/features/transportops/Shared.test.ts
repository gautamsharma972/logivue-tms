import { describe, expect, it } from 'vitest'
import { QrError, qrApi } from '@/lib/api/opsApi'
import { mins, when } from './fmt'

describe('formatting', () => {
  it('shows times in India time whatever the browser zone', () => {
    expect(when('2026-10-05T04:00:00Z')).toContain('9:30')
  })

  it('reads minutes as hours and minutes', () => {
    expect(mins(45)).toBe('45 min')
    expect(mins(135)).toBe('2 h 15 min')
    expect(mins(null)).toBe('—')
  })
})

describe('the driver api', () => {
  it('only the arrival call can carry a location', () => {
    expect(qrApi.arrive.length).toBeGreaterThanOrEqual(2) // (token, position, key)
    expect(qrApi.unload.length).toBeLessThanOrEqual(2) // (token, key): no position parameter
    expect(qrApi.podPending.length).toBeLessThanOrEqual(2)
    expect(qrApi.uploadPod.length).toBeLessThanOrEqual(3) // (token, files, key)
  })

  it('turns a 404 into the same plain message and keeps the status', async () => {
    const original = globalThis.fetch
    globalThis.fetch = (async () => new Response(JSON.stringify({ code: 'x' }), { status: 404 })) as typeof fetch
    try {
      const error = await qrApi.view('nope').catch((e) => e as QrError)
      expect(error).toBeInstanceOf(QrError)
      expect((error as QrError).status).toBe(404)
      expect((error as QrError).message).toContain('not valid')
    } finally { globalThis.fetch = original }
  })
})
