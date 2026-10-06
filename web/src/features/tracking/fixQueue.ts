import type { QueuedFix, TrackBatchResult } from '@/lib/api/types'

/** A GPS fix waiting for a signal, with the trip it belongs to. The client id makes sending it twice harmless. */
export interface PendingFix extends QueuedFix {
  id: string
  tripReference: string
  deviceId: string
}

export interface FixStore {
  all(): Promise<PendingFix[]>
  put(fix: PendingFix): Promise<void>
  remove(ids: string[]): Promise<void>
}

export function memoryFixStore(): FixStore {
  const data = new Map<string, PendingFix>()
  return {
    all: async () => [...data.values()],
    put: async (f) => void data.set(f.id, f),
    remove: async (ids) => void ids.forEach((i) => data.delete(i)),
  }
}

export function indexedDbFixStore(name = 'tms-tracking'): FixStore {
  const opened = new Promise<IDBDatabase>((resolve, reject) => {
    const open = indexedDB.open(name, 1)
    open.onupgradeneeded = () => open.result.createObjectStore('fixes', { keyPath: 'id' })
    open.onsuccess = () => resolve(open.result)
    open.onerror = () => reject(open.error ?? new Error('Could not open the tracking queue'))
  })
  const run = async <T,>(mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>) => {
    const store = (await opened).transaction('fixes', mode).objectStore('fixes')
    return new Promise<T>((resolve, reject) => {
      const r = fn(store)
      r.onsuccess = () => resolve(r.result)
      r.onerror = () => reject(r.error ?? new Error('Tracking queue failed'))
    })
  }
  return {
    all: () => run('readonly', (s) => s.getAll() as IDBRequest<PendingFix[]>),
    put: async (f) => void (await run('readwrite', (s) => s.put(f))),
    remove: async (ids) => {
      for (const id of ids) await run('readwrite', (s) => s.delete(id))
    },
  }
}

export function defaultFixStore(): FixStore {
  try {
    return typeof indexedDB === 'undefined' ? memoryFixStore() : indexedDbFixStore()
  } catch {
    return memoryFixStore()
  }
}

export type SendBatch = (tripReference: string, deviceId: string, fixes: QueuedFix[]) => Promise<TrackBatchResult>

export interface FlushResult {
  sent: number
  failed: boolean
}

/**
 * Sends what is waiting, oldest first, a batch per trip. A fix leaves the queue only once the server has answered for the whole batch: if the
 * request fails, everything stays and the next attempt resends it (the server drops duplicates by client id). A batch the server rejected
 * entirely as invalid is not retried forever: rejected points are reported in the result and removed with the rest, since resending cannot fix them.
 */
export async function flushFixes(store: FixStore, send: SendBatch, batchSize = 100): Promise<FlushResult> {
  const all = (await store.all()).sort((a, b) => a.capturedAtUtc.localeCompare(b.capturedAtUtc))
  let sent = 0
  const groups = new Map<string, PendingFix[]>()
  for (const f of all) groups.set(`${f.tripReference}|${f.deviceId}`, [...(groups.get(`${f.tripReference}|${f.deviceId}`) ?? []), f])
  for (const group of groups.values()) {
    for (let i = 0; i < group.length; i += batchSize) {
      const chunk = group.slice(i, i + batchSize)
      try {
        await send(chunk[0]!.tripReference, chunk[0]!.deviceId, chunk.map(({ id: _id, tripReference: _t, deviceId: _d, ...fix }) => fix))
      } catch {
        return { sent, failed: true }
      }
      await store.remove(chunk.map((c) => c.id))
      sent += chunk.length
    }
  }
  return { sent, failed: false }
}
