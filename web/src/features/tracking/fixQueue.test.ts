import { describe, expect, it } from 'vitest'
import { flushFixes, memoryFixStore, type PendingFix } from './fixQueue'

const fix = (n: number, trip = 'SH-1'): PendingFix => ({
  id: `f${n}`, clientLocationId: `c${n}`, tripReference: trip, deviceId: 'd1', latitude: 19, longitude: 73, accuracyMeters: 10, speedKph: 30, heading: 0, capturedAtUtc: `2026-10-06T10:${String(n).padStart(2, '0')}:00Z`, mockLocation: false,
})
const ok = { accepted: 1, duplicates: 0, suspicious: 0, late: 0, rejected: [] }

describe('flushFixes', () => {
  it('sends oldest first, a batch per trip, and empties the queue', async () => {
    const store = memoryFixStore()
    for (const f of [fix(3), fix(1), fix(2), fix(5, 'SH-2')]) await store.put(f)
    const calls: [string, string[]][] = []
    const result = await flushFixes(store, async (trip, _d, fixes) => { calls.push([trip, fixes.map((x) => x.clientLocationId)]); return ok })
    expect(result).toEqual({ sent: 4, failed: false })
    expect(calls).toEqual([['SH-1', ['c1', 'c2', 'c3']], ['SH-2', ['c5']]])
    expect(await store.all()).toEqual([])
  })

  it('keeps everything when the network fails, so the next attempt resends it', async () => {
    const store = memoryFixStore()
    for (const f of [fix(1), fix(2)]) await store.put(f)
    const result = await flushFixes(store, async () => { throw new Error('offline') })
    expect(result).toEqual({ sent: 0, failed: true })
    expect(await store.all()).toHaveLength(2)
  })

  it('removes only the batches already acknowledged when a later one fails', async () => {
    const store = memoryFixStore()
    for (let n = 1; n <= 5; n++) await store.put(fix(n))
    let call = 0
    const result = await flushFixes(store, async () => { if (++call === 2) throw new Error('drop'); return ok }, 2)
    expect(result).toEqual({ sent: 2, failed: true })
    expect((await store.all()).map((f) => f.id).sort()).toEqual(['f3', 'f4', 'f5'])
  })

  it('does not send the queue bookkeeping fields to the server', async () => {
    const store = memoryFixStore()
    await store.put(fix(1))
    let sentKeys: string[] = []
    await flushFixes(store, async (_t, _d, fixes) => { sentKeys = Object.keys(fixes[0]!); return ok })
    expect(sentKeys).not.toContain('id')
    expect(sentKeys).not.toContain('tripReference')
    expect(sentKeys).toContain('clientLocationId')
  })
})
