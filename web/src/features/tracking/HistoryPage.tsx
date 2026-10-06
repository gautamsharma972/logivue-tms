import { useQuery } from '@tanstack/react-query'
import { Alert, DatePicker, Flex, Input, Segmented, Select, Skeleton } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ReplayDto } from '@/lib/api/types'
import { TrackingHistoryPlayer } from './TrackingHistoryPlayer'

/** Replays what a vehicle did: pick a trip, or a vehicle and a day. For disputes, deviation and delay investigations, and customer complaints. */
export function TrackingHistoryPage() {
  const [params, setParams] = useSearchParams()
  const [mode, setMode] = useState<'trip' | 'vehicle'>('trip')
  const trip = params.get('trip')
  const [search, setSearch] = useState('')
  const [vehicle, setVehicle] = useState('')
  const [day, setDay] = useState<Dayjs | null>(dayjs())

  const trips = useQuery({ queryKey: queryKeys.tracking.list({ search, pageSize: 20 }), queryFn: () => trackingApi.list({ search: search || undefined, pageSize: 20 }) })
  const detail = useQuery({ queryKey: queryKeys.tracking.detail(trip ?? ''), queryFn: () => trackingApi.get(trip!), enabled: !!trip })
  const tripReplay = useQuery({ queryKey: queryKeys.tracking.replay(trip ?? '', {}), queryFn: () => trackingApi.replay(trip!, { maxPoints: 2000 }), enabled: mode === 'trip' && !!trip })
  const timeline = useQuery({ queryKey: queryKeys.tracking.part(trip ?? '', 'timeline'), queryFn: () => trackingApi.timeline(trip!), enabled: mode === 'trip' && !!trip })
  const from = day?.startOf('day').toISOString()
  const to = day?.endOf('day').toISOString()
  const vehicleHistory = useQuery({
    queryKey: ['tracking', 'vehicle-history', vehicle, from],
    queryFn: () => trackingApi.vehicleHistory(vehicle, { from, to, maxPoints: 2000 }),
    enabled: mode === 'vehicle' && vehicle.trim().length > 1 && !!day,
  })
  const vehicleReplay: ReplayDto | null = useMemo(() => {
    const rows = [...(vehicleHistory.data ?? [])].filter((l) => l.validation === 'Valid').sort((a, b) => a.capturedAt.localeCompare(b.capturedAt))
    return rows.length === 0 ? null : { points: rows.map((l) => [l.latitude, l.longitude, dayjs(l.capturedAt).valueOf(), l.speedKph ?? 0]), source: 'Raw', from: rows[0]!.capturedAt, to: rows.at(-1)!.capturedAt, storedPoints: rows.length }
  }, [vehicleHistory.data])

  const replay = mode === 'trip' ? tripReplay.data : vehicleReplay
  const loading = mode === 'trip' ? tripReplay.isLoading : vehicleHistory.isLoading

  return (
    <>
      <PageHeader title="Trip replay" description="Play a recorded trip back. Gaps in the recording are shown as gaps." />
      <Flex gap={12} wrap style={{ marginBottom: 16 }}>
        <Segmented value={mode} onChange={setMode} options={[{ value: 'trip', label: 'A trip' }, { value: 'vehicle', label: 'A vehicle on a day' }]} />
        {mode === 'trip' ? (
          <Select
            showSearch
            filterOption={false}
            style={{ width: 360 }}
            placeholder="Search a trip, vehicle or driver"
            value={trip ?? undefined}
            onSearch={setSearch}
            onChange={(id: string) => setParams({ trip: id })}
            loading={trips.isLoading}
            options={(trips.data?.items ?? []).map((t) => ({ value: t.id, label: `${t.tripReference} · ${t.vehicleReference ?? 'no vehicle'} · ${t.origin ?? '—'} → ${t.destination ?? '—'}` }))}
            notFoundContent="No matching trips"
          />
        ) : (
          <>
            <Input style={{ width: 200 }} placeholder="Vehicle number" value={vehicle} onChange={(e) => setVehicle(e.target.value.toUpperCase())} />
            <DatePicker value={day} onChange={setDay} allowClear={false} />
          </>
        )}
      </Flex>
      {mode === 'trip' && !trip && <Alert type="info" showIcon message="Choose a trip to replay." />}
      {loading ? <Skeleton active /> : replay && (mode === 'vehicle' || trip) ? (
        <TrackingHistoryPlayer key={`${replay.from}-${replay.to}-${replay.points.length}`} replay={replay} stops={mode === 'trip' ? detail.data?.stops : undefined} events={mode === 'trip' ? timeline.data : undefined} />
      ) : mode === 'vehicle' && vehicle.trim().length > 1 ? <Alert type="info" showIcon message="No locations were recorded for that vehicle on that day." /> : null}
    </>
  )
}
