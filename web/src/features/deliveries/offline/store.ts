import type { MobileBundleDto, SyncOperation, SyncStatus } from '@/lib/api/types'

export type RecordKind = 'bundle' | 'command' | 'upload' | 'submit'

/** What the driver did, kept until the server has it. The id is the device's own key, so sending it twice changes nothing. */
export interface QueuedCommand {
  id: string
  type: SyncOperation
  deliveryId: string
  payload: unknown
  createdAt: string
  status: SyncStatus
  attempts: number
  error: string | null
  errorCode: string | null
  podId: string | null
}

/** A photo or signature waiting to be uploaded to the proof (which only exists once the delivery has been completed on the server). */
export interface QueuedUpload {
  id: string
  deliveryId: string
  kind: 'evidence' | 'signature'
  evidenceType: string | null
  signerName: string | null
  blob: Blob
  fix: { latitude: number | null; longitude: number | null; accuracyM: number | null } | null
  createdAt: string
  attempts: number
  error: string | null
}

/** "Send this proof for checking" once its evidence is up. */
export interface QueuedSubmit {
  id: string
  deliveryId: string
  createdAt: string
  attempts: number
  error: string | null
}

export interface StoredBundle {
  id: 'bundle'
  bundle: MobileBundleDto
}

type Stored = StoredBundle | QueuedCommand | QueuedUpload | QueuedSubmit

/** Where a device keeps its work between connections. IndexedDB in the browser; memory when that is unavailable (and in tests). */
export interface OfflineStore {
  list<T extends Stored>(kind: RecordKind): Promise<T[]>
  put(kind: RecordKind, record: Stored): Promise<void>
  remove(kind: RecordKind, id: string): Promise<void>
  clear(): Promise<void>
}

export function memoryStore(): OfflineStore {
  const data = new Map<RecordKind, Map<string, Stored>>()
  const bucket = (kind: RecordKind) => {
    if (!data.has(kind)) data.set(kind, new Map())
    return data.get(kind)!
  }
  return {
    list: async <T extends Stored>(kind: RecordKind) => [...bucket(kind).values()] as T[],
    put: async (kind, record) => void bucket(kind).set(record.id, record),
    remove: async (kind, id) => void bucket(kind).delete(id),
    clear: async () => data.clear(),
  }
}

const KINDS: RecordKind[] = ['bundle', 'command', 'upload', 'submit']

function request<T>(r: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    r.onsuccess = () => resolve(r.result)
    r.onerror = () => reject(r.error ?? new Error('IndexedDB request failed'))
  })
}

/** Durable storage that survives a closed tab or a dead battery: captured deliveries must not be lost because the signal was. */
export function indexedDbStore(name = 'tms-deliveries'): OfflineStore {
  const opened = new Promise<IDBDatabase>((resolve, reject) => {
    const open = indexedDB.open(name, 1)
    open.onupgradeneeded = () => {
      for (const kind of KINDS) open.result.createObjectStore(kind, { keyPath: 'id' })
    }
    open.onsuccess = () => resolve(open.result)
    open.onerror = () => reject(open.error ?? new Error('Could not open the offline database'))
  })
  const store = async (kind: RecordKind, mode: IDBTransactionMode) => (await opened).transaction(kind, mode).objectStore(kind)

  return {
    list: async <T extends Stored>(kind: RecordKind) => (await request((await store(kind, 'readonly')).getAll())) as T[],
    put: async (kind, record) => void (await request((await store(kind, 'readwrite')).put(record))),
    remove: async (kind, id) => void (await request((await store(kind, 'readwrite')).delete(id))),
    clear: async () => {
      for (const kind of KINDS) await request((await store(kind, 'readwrite')).clear())
    },
  }
}

export function defaultStore(): OfflineStore {
  try {
    return typeof indexedDB === 'undefined' ? memoryStore() : indexedDbStore()
  } catch {
    return memoryStore()
  }
}

export function deviceId(): string {
  try {
    const existing = localStorage.getItem('tms.deviceId')
    if (existing) return existing
    const created = `web-${crypto.randomUUID()}`
    localStorage.setItem('tms.deviceId', created)
    return created
  } catch {
    return 'web-unknown'
  }
}

export const newKey = () => crypto.randomUUID().replaceAll('-', '')
