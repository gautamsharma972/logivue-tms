import { useQuery } from '@tanstack/react-query'
import { Flex, Input, Select, Table } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { TrackingHealth, VehicleTrackingDto } from '@/lib/api/types'
import { Age, healthLabel, HealthTag, km, RiskTag } from './shared'
import { useTrackingLive } from './useTrackingLive'

export function VehicleTrackingPage() {
  const live = useTrackingLive()
  const [search, setSearch] = useState<string>()
  const [health, setHealth] = useState<TrackingHealth>()
  const params = { search, health, pageSize: 100 }
  const q = useQuery({ queryKey: queryKeys.tracking.vehicles(params), queryFn: () => trackingApi.vehicles(params), refetchInterval: live.pollMs })
  return (
    <>
      <PageHeader title="Vehicles" description="Where every vehicle was last seen, and how long ago. A vehicle that stopped reporting is shown as such, not as parked." />
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search allowClear placeholder="Vehicle, driver or trip" style={{ width: 260 }} onSearch={(v) => setSearch(v || undefined)} />
        <Select<TrackingHealth> allowClear placeholder="Tracking" style={{ width: 160 }} onChange={setHealth} options={(Object.keys(healthLabel) as TrackingHealth[]).map((v) => ({ value: v, label: healthLabel[v] }))} />
      </Flex>
      <Table<VehicleTrackingDto> size="small" rowKey={(r) => r.position.vehicleReference ?? r.position.tripReference ?? ''} loading={q.isLoading} dataSource={q.data?.items ?? []} pagination={{ pageSize: 20 }} columns={[
        { title: 'Vehicle', render: (_, r) => <strong>{r.position.vehicleReference ?? '—'}</strong> },
        { title: 'Driver', render: (_, r) => `${r.position.driverName ?? '—'}${r.driverPhone ? ` · ${r.driverPhone}` : ''}` },
        { title: 'Trip', render: (_, r) => (r.shipmentId ? <Link to={`/tracking/shipments/${r.shipmentId}`}>{r.tripReference}</Link> : '—') },
        { title: 'Lane', render: (_, r) => `${r.origin ?? '—'} → ${r.destination ?? '—'}` },
        { title: 'Tracking', render: (_, r) => <HealthTag health={r.position.health} /> },
        { title: 'Risk', render: (_, r) => (r.risk ? <RiskTag risk={r.risk} /> : '—') },
        { title: 'Last seen', render: (_, r) => <Age minutes={r.position.ageMinutes} /> },
        { title: 'Speed', render: (_, r) => (r.position.speedKph == null ? '—' : `${Math.round(r.position.speedKph)} km/h`) },
        { title: 'Today', render: (_, r) => km(r.todayKm) },
        { title: 'Battery', render: (_, r) => (r.position.batteryPercentage == null ? '—' : `${r.position.batteryPercentage}%`) },
      ]} />
    </>
  )
}
