import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Space, Tag, Typography } from 'antd'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { deviceId, newKey } from '@/features/deliveries/offline/store'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { MobileTripDto } from '@/lib/api/types'
import { clock, executionLabel } from './shared'
import { defaultFixStore, flushFixes, type FixStore } from './fixQueue'

/**
 * The driver's tracking screen in the browser. A web page can only report while it is open and the screen is awake, so this is a stand-in for a native
 * app: it asks the screen to stay on, keeps every fix in a local queue and uploads it in batches when there is a signal. Nothing is lost to a dead zone.
 */
export function DriverTrackingPage() {
  const { message } = App.useApp()
  const client = useQueryClient()
  const trips = useQuery({ queryKey: queryKeys.tracking.trips, queryFn: trackingApi.trips })
  const [active, setActive] = useState<string | null>(null)
  const [waiting, setWaiting] = useState(0)
  const [lastSent, setLastSent] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const store = useRef<FixStore | null>(null)
  const watch = useRef<number | null>(null)
  const wake = useRef<{ release: () => Promise<void> } | null>(null)
  const device = useRef(deviceId())
  const { can } = useAuth()

  const getStore = () => (store.current ??= defaultFixStore())
  const sync = useCallback(async () => {
    const result = await flushFixes(getStore(), async (trip, dev, fixes) =>
      trackingApi.sendBatch({ tripReference: trip, deviceId: dev, locations: fixes, sentAtUtc: new Date().toISOString(), networkType: navigator.onLine ? 'online' : 'offline', locationPermission: 'granted' }))
    setWaiting((await getStore().all()).length)
    if (result.sent > 0) setLastSent(new Date().toISOString())
  }, [])

  useEffect(() => {
    const timer = setInterval(() => { if (active && navigator.onLine) void sync() }, 15_000)
    const online = () => void sync()
    window.addEventListener('online', online)
    return () => {
      clearInterval(timer)
      window.removeEventListener('online', online)
    }
  }, [active, sync])

  useEffect(() => () => {
    if (watch.current != null) navigator.geolocation.clearWatch(watch.current)
    void wake.current?.release()
  }, [])

  const begin = useCallback(async (trip: MobileTripDto) => {
    if (!('geolocation' in navigator)) { setProblem('This browser cannot give a location.'); return }
    await trackingApi.start(trip.tripReference, device.current, newKey())
    setActive(trip.tripReference)
    setProblem(null)
    try { wake.current = await (navigator as Navigator & { wakeLock?: { request: (t: 'screen') => Promise<{ release: () => Promise<void> }> } }).wakeLock?.request('screen') ?? null } catch { /* the screen may sleep: said below */ }
    watch.current = navigator.geolocation.watchPosition(
      async (p) => {
        await getStore().put({
          id: newKey(), clientLocationId: newKey(), tripReference: trip.tripReference, deviceId: device.current, latitude: p.coords.latitude, longitude: p.coords.longitude,
          accuracyMeters: p.coords.accuracy ?? null, speedKph: p.coords.speed == null ? null : p.coords.speed * 3.6, heading: p.coords.heading ?? null, capturedAtUtc: new Date(p.timestamp).toISOString(), mockLocation: false,
        })
        setWaiting((await getStore().all()).length)
        if (navigator.onLine) void sync()
      },
      (e) => setProblem(e.code === e.PERMISSION_DENIED ? 'Location is switched off for this page. Allow it in the browser settings, or tracking cannot work.' : 'The phone could not get a location just now.'),
      { enableHighAccuracy: true, maximumAge: 5_000, timeout: 30_000 },
    )
  }, [sync])

  const start = useMutation({ mutationFn: begin, onError: (e) => void message.error(toApiError(e).message), onSuccess: () => void client.invalidateQueries({ queryKey: queryKeys.tracking.trips }) })
  const stop = useMutation({
    mutationFn: async (v: { trip: string; completed: boolean }) => {
      if (watch.current != null) navigator.geolocation.clearWatch(watch.current)
      watch.current = null
      await wake.current?.release()
      await sync()
      return trackingApi.stop(v.trip, device.current, v.completed, newKey())
    },
    onSuccess: () => { setActive(null); void client.invalidateQueries({ queryKey: queryKeys.tracking.trips }) },
    onError: (e) => void message.error(toApiError(e).message),
  })

  if (!can('tracking.execute')) return <Alert type="warning" showIcon message="Your account is not allowed to track trips." />

  return (
    <>
      <PageHeader title="Track my trip" description="Keep this page open and the screen on while you drive. Locations are saved on the phone and sent when there is signal." />
      {problem && <Alert type="error" showIcon message={problem} style={{ marginBottom: 12 }} />}
      {active && <Alert type="info" showIcon style={{ marginBottom: 12 }} message={`Tracking ${active}`} description={`${waiting} location(s) waiting to be sent${lastSent ? ` · last upload ${clock(lastSent)}` : ''}. This page cannot track once it is closed or the screen is locked.`} />}
      <Flex vertical gap={12}>
        {(trips.data ?? []).map((t) => (
          <Card key={t.tripReference} size="small" title={<>{t.tripReference} <Tag>{executionLabel(t.execution)}</Tag></>} extra={t.vehicleReference}>
            <Typography.Paragraph>{t.origin ?? '—'} → {t.destination ?? '—'}{t.etaAt ? ` · expected ${clock(t.etaAt)}` : ''}</Typography.Paragraph>
            <Space>
              {active === t.tripReference
                ? <><Button danger loading={stop.isPending} onClick={() => stop.mutate({ trip: t.tripReference, completed: true })}>Finish trip</Button><Button onClick={() => stop.mutate({ trip: t.tripReference, completed: false })}>Stop tracking</Button><Button onClick={() => void sync()}>Send now</Button></>
                : <Button type="primary" disabled={!t.canStart || !!active} loading={start.isPending} onClick={() => start.mutate(t)}>Start tracking</Button>}
            </Space>
          </Card>
        ))}
        {trips.data?.length === 0 && <Typography.Text type="secondary">No trips are assigned to you right now.</Typography.Text>}
      </Flex>
    </>
  )
}
