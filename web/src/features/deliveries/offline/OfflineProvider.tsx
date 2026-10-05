import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { deliveriesApi } from '@/lib/api/endpoints'
import type { MobileBundleDto } from '@/lib/api/types'
import { defaultStore, deviceId, type OfflineStore, type QueuedCommand, type StoredBundle } from './store'
import { SyncManager, isOffline, type FlushSummary, type SyncApi } from './sync'

const realApi: SyncApi = {
  sync: (device, commands) => deliveriesApi.sync(device, commands),
  addEvidence: (podId, file, type, fix, clientRecordId, device) => deliveriesApi.addEvidence(podId, file, type as never, fix, clientRecordId, device),
  addSignature: (podId, file, signer) => deliveriesApi.addSignature(podId, file, signer),
  submitPod: (podId) => deliveriesApi.submitPod(podId),
  download: () => deliveriesApi.mobileDeliveries(),
}

interface OfflineState {
  manager: SyncManager
  bundle: MobileBundleDto | null
  commands: QueuedCommand[]
  pending: number
  online: boolean
  busy: boolean
  lastSummary: FlushSummary | null
  deviceReference: string
  sync: () => Promise<void>
  refresh: () => Promise<void>
}

const Context = createContext<OfflineState | null>(null)

export function useOffline(): OfflineState {
  const value = useContext(Context)
  if (!value) throw new Error('useOffline must be used inside OfflineProvider')
  return value
}

/**
 * Holds the phone's copy of its deliveries and the queue of what the driver did. Everything the driver does is saved here first and sent when there is a
 * signal; this provider sends it on its own when the connection returns and every half minute while the app is open.
 */
export function OfflineProvider({ children, store, api }: { children: ReactNode; store?: OfflineStore; api?: SyncApi }) {
  const device = useMemo(() => deviceId(), [])
  const local = useMemo(() => store ?? defaultStore(), [store])
  const manager = useMemo(() => new SyncManager(local, api ?? realApi, device), [local, api, device])
  const [bundle, setBundle] = useState<MobileBundleDto | null>(null)
  const [commands, setCommands] = useState<QueuedCommand[]>([])
  const [pending, setPending] = useState(0)
  const [online, setOnline] = useState(typeof navigator === 'undefined' ? true : navigator.onLine)
  const [busy, setBusy] = useState(false)
  const [lastSummary, setLastSummary] = useState<FlushSummary | null>(null)
  const running = useRef(false)

  const refresh = useCallback(async () => {
    setCommands(await manager.commands())
    setPending(await manager.pendingCount())
    setBundle((await local.list<StoredBundle>('bundle'))[0]?.bundle ?? null)
  }, [manager, local])

  const sync = useCallback(async () => {
    if (running.current) return
    running.current = true
    setBusy(true)
    try {
      const summary = await manager.flush()
      setLastSummary(summary)
      setOnline(summary.reachable)
      // Refresh the phone's copy only when nothing is left to send, or the server's older picture would hide the driver's own work.
      if (summary.reachable && (await manager.pendingCount()) === 0) {
        try {
          const fresh = await (api ?? realApi).download()
          await local.put('bundle', { id: 'bundle', bundle: fresh })
        } catch (e) {
          if (isOffline(e)) setOnline(false)
        }
      }
    } finally {
      running.current = false
      setBusy(false)
      await refresh()
    }
  }, [manager, local, api, refresh])

  useEffect(() => {
    void refresh().then(() => sync())
    const onOnline = () => { setOnline(true); void sync() }
    const onOffline = () => setOnline(false)
    window.addEventListener('online', onOnline)
    window.addEventListener('offline', onOffline)
    const timer = window.setInterval(() => { void sync() }, 30_000)
    return () => {
      window.removeEventListener('online', onOnline)
      window.removeEventListener('offline', onOffline)
      window.clearInterval(timer)
    }
  }, [refresh, sync])

  const value = useMemo<OfflineState>(() => ({ manager, bundle, commands, pending, online, busy, lastSummary, deviceReference: device, sync, refresh }), [manager, bundle, commands, pending, online, busy, lastSummary, device, sync, refresh])
  return <Context.Provider value={value}>{children}</Context.Provider>
}
