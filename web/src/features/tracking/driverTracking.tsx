import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { deviceId, newKey } from '@/features/deliveries/offline/store'
import { trackingApi } from '@/lib/api/endpoints'
import { defaultFixStore, flushFixes, type FixStore, type PendingFix } from './fixQueue'

type Connection = { effectiveType?: string; type?: string; saveData?: boolean }
type Nav = Navigator & {
  connection?: Connection
  getBattery?: () => Promise<{ level: number }>
  wakeLock?: { request: (t: 'screen') => Promise<{ release: () => Promise<void> }> }
}

/** Offline, Wi-Fi, Mobile data or Poor connection: what the phone can say about its network. */
export function networkLabel(online: boolean, c: Connection | undefined): string {
  if (!online) return 'Offline'
  if (c?.effectiveType === 'slow-2g' || c?.effectiveType === '2g') return 'Poor connection'
  if (c?.type === 'wifi') return 'Wi-Fi'
  if (c?.type === 'cellular') return 'Mobile data'
  return 'Connected'
}

export interface DriverTracking {
  active: string | null
  waiting: number
  pending: PendingFix[]
  lastFixAt: number | null
  lastSentAt: number | null
  problem: string | null
  network: string
  battery: number | null
  start: (tripReference: string) => Promise<void>
  stop: (tripReference: string, completed: boolean) => Promise<void>
  sync: () => Promise<{ sent: number; failed: boolean }>
}

const Context = createContext<DriverTracking | null>(null)

export function useDriverTracking(): DriverTracking {
  const value = useContext(Context)
  if (!value) throw new Error('useDriverTracking needs a DriverTrackingProvider')
  return value
}

/**
 * Tracking for the driver's trip, kept in one place above the pages so it carries on while the driver moves between them. A web page can only report while it is open
 * and the screen is awake: it asks for a wake lock, keeps every fix in a local queue and uploads in batches, and says plainly when it cannot do any of that.
 */
export function DriverTrackingProvider({ children, store: given }: { children: ReactNode; store?: FixStore }) {
  const [active, setActive] = useState<string | null>(null)
  const [pending, setPending] = useState<PendingFix[]>([])
  const [lastFixAt, setLastFixAt] = useState<number | null>(null)
  const [lastSentAt, setLastSentAt] = useState<number | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [online, setOnline] = useState(typeof navigator === 'undefined' ? true : navigator.onLine)
  const [battery, setBattery] = useState<number | null>(null)
  const store = useRef<FixStore | null>(given ?? null)
  const watch = useRef<number | null>(null)
  const wake = useRef<{ release: () => Promise<void> } | null>(null)
  const device = useRef(deviceId())
  const getStore = () => (store.current ??= defaultFixStore())
  const nav = typeof navigator === 'undefined' ? undefined : (navigator as Nav)

  const refresh = useCallback(async () => setPending(await getStore().all()), [])
  const sync = useCallback(async () => {
    const batteryNow = battery
    const result = await flushFixes(getStore(), (trip, dev, fixes) =>
      trackingApi.sendBatch({
        tripReference: trip, deviceId: dev, locations: fixes, sentAtUtc: new Date().toISOString(), batteryPercentage: batteryNow, networkType: networkLabel(navigator.onLine, (navigator as Nav).connection), locationPermission: 'granted',
      }))
    if (result.sent > 0) setLastSentAt(Date.now())
    await refresh()
    return result
  }, [battery, refresh])

  useEffect(() => {
    void refresh()
    const up = () => { setOnline(true); void sync() }
    const down = () => setOnline(false)
    window.addEventListener('online', up)
    window.addEventListener('offline', down)
    return () => {
      window.removeEventListener('online', up)
      window.removeEventListener('offline', down)
    }
  }, [refresh, sync])

  useEffect(() => {
    let live = true
    void nav?.getBattery?.().then((b) => live && setBattery(Math.round(b.level * 100))).catch(() => undefined)
    return () => { live = false }
  }, [nav])

  // upload every so often while a trip is being tracked, and the moment the network is back
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => { if (navigator.onLine) void sync() }, 15_000)
    return () => clearInterval(timer)
  }, [active, sync])

  useEffect(() => () => {
    if (watch.current != null) navigator.geolocation.clearWatch(watch.current)
    void wake.current?.release()
  }, [])

  const start = useCallback(async (tripReference: string) => {
    if (!('geolocation' in navigator)) { setProblem('This browser cannot give a location.'); return }
    await trackingApi.start(tripReference, device.current, newKey())
    setActive(tripReference)
    setProblem(null)
    try { wake.current = (await nav?.wakeLock?.request('screen')) ?? null } catch { setProblem('The screen may switch off, and tracking stops when it does. Keep the phone awake.') }
    watch.current = navigator.geolocation.watchPosition(
      async (p) => {
        setProblem((current) => (current?.startsWith('Location is switched off') || current?.startsWith('The phone could not') ? null : current))
        setLastFixAt(p.timestamp)
        await getStore().put({
          id: newKey(), clientLocationId: newKey(), tripReference, deviceId: device.current, latitude: p.coords.latitude, longitude: p.coords.longitude, accuracyMeters: p.coords.accuracy ?? null,
          speedKph: p.coords.speed == null ? null : p.coords.speed * 3.6, heading: p.coords.heading ?? null, capturedAtUtc: new Date(p.timestamp).toISOString(), mockLocation: false,
        })
        await refresh()
        if (navigator.onLine) void sync()
      },
      (e) => setProblem(e.code === e.PERMISSION_DENIED ? 'Location is switched off for this page. Allow it in the browser settings, or tracking cannot work.' : 'The phone could not get a location just now.'),
      { enableHighAccuracy: true, maximumAge: 5_000, timeout: 30_000 },
    )
  }, [nav, refresh, sync])

  const stop = useCallback(async (tripReference: string, completed: boolean) => {
    if (watch.current != null) navigator.geolocation.clearWatch(watch.current)
    watch.current = null
    await wake.current?.release()
    wake.current = null
    await sync()
    await trackingApi.stop(tripReference, device.current, completed, newKey())
    setActive(null)
  }, [sync])

  const value = useMemo<DriverTracking>(
    () => ({ active, waiting: pending.length, pending, lastFixAt, lastSentAt, problem, network: networkLabel(online, nav?.connection), battery, start, stop, sync }),
    [active, pending, lastFixAt, lastSentAt, problem, online, nav, battery, start, stop, sync],
  )
  return <Context.Provider value={value}>{children}</Context.Provider>
}
