import { useQuery } from '@tanstack/react-query'
import { Alert, Badge, Button, Card, Col, Drawer, Row, Statistic, Table, Tag, Typography } from 'antd'
import { lazy, Suspense, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListTrackedParams, TrackedSummaryDto } from '@/lib/api/types'
import { ControlTowerFilters, ShipmentTrackingPanel } from './components'
import { HealthTag, RiskTag } from './shared'
import { useTrackingLive } from './useTrackingLive'

const TrackingMap = lazy(() => import('./TrackingMap'))

type Tile = { key: string; title: string; value: number; filter: Partial<ListTrackedParams>; warn?: boolean }

export function ControlTowerPage() {
  const live = useTrackingLive()
  const [filters, setFilters] = useState<ListTrackedParams>({ activeOnly: true, pageSize: 100 })
  const [selected, setSelected] = useState<string | null>(null)

  const summary = useQuery({ queryKey: queryKeys.tracking.summary(), queryFn: () => trackingApi.summary(), refetchInterval: live.pollMs })
  const list = useQuery({ queryKey: queryKeys.tracking.list(filters), queryFn: () => trackingApi.list(filters), refetchInterval: live.pollMs })
  const items = useMemo(() => list.data?.items ?? [], [list.data])
  const chosen = items.find((i) => i.id === selected) ?? null
  const s = summary.data

  const tiles: Tile[] = s
    ? [
        { key: 'active', title: 'Active shipments', value: s.active, filter: { activeOnly: true } },
        { key: 'ontime', title: 'On time', value: s.onTime, filter: { risk: 'OnTime', activeOnly: true } },
        { key: 'risk', title: 'At risk', value: s.atRisk, filter: { risk: 'AtRisk', activeOnly: true }, warn: true },
        { key: 'delayed', title: 'Delayed', value: s.delayed, filter: { risk: 'Delayed', activeOnly: true }, warn: true },
        { key: 'stale', title: 'Tracking stale', value: s.trackingStale, filter: { tracking: 'Stale', activeOnly: true }, warn: true },
        { key: 'lost', title: 'Tracking lost', value: s.trackingLost, filter: { tracking: 'Lost', activeOnly: true }, warn: true },
        { key: 'exceptions', title: 'Open exceptions', value: s.openExceptions, filter: { hasException: true, activeOnly: true }, warn: true },
        { key: 'notstarted', title: 'Not started', value: s.notStarted, filter: { tracking: 'NotStarted', activeOnly: false } },
        { key: 'done', title: 'Completed today', value: s.completedToday, filter: { execution: 'Completed', activeOnly: false } },
      ]
    : []

  return (
    <>
      <PageHeader
        title="Control tower"
        description="Every active trip on one map. Tracking health (is the phone reporting?) and delivery risk (will it be late?) are shown separately."
        actions={<Tag color={live.state === 'live' ? 'green' : 'gold'}>{live.state === 'live' ? 'Live updates' : 'Refreshing every 20 s'}</Tag>}
      />
      {summary.isError && <Alert type="error" showIcon message="Could not load the summary." style={{ marginBottom: 12 }} />}
      <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
        {tiles.map((t) => (
          <Col xs={12} md={8} xl={4} key={t.key}>
            <Card size="small" hoverable onClick={() => setFilters({ pageSize: 100, ...t.filter })} aria-label={t.title}>
              <Statistic title={t.title} value={t.value} styles={{ content: t.warn && t.value > 0 ? { color: '#cf1322' } : undefined }} />
            </Card>
          </Col>
        ))}
      </Row>

      <ControlTowerFilters value={filters} onChange={setFilters} />

      <Row gutter={16}>
        <Col xs={24} xl={14}>
          <Card styles={{ body: { padding: 8 } }}>
            <Suspense fallback={<div style={{ height: 480 }} />}>
              <TrackingMap vehicles={items} selectedId={selected} onSelect={setSelected} height={480} />
            </Suspense>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Pin colour: green on time, amber at risk, orange delayed or stale, red severely delayed or lost, grey not started or completed. Nearby pins merge when zoomed out.
            </Typography.Text>
          </Card>
        </Col>
        <Col xs={24} xl={10}>
          <Table<TrackedSummaryDto>
            size="small"
            rowKey="id"
            loading={list.isLoading}
            dataSource={items}
            pagination={{ pageSize: 8, size: 'small' }}
            onRow={(r) => ({ onClick: () => setSelected(r.id), style: { cursor: 'pointer', background: r.id === selected ? 'rgba(22,119,255,0.08)' : undefined } })}
            columns={[
              { title: 'Trip', render: (_, r) => <><strong>{r.tripReference}</strong><div style={{ fontSize: 12 }}>{r.vehicleReference ?? 'No vehicle'}</div></> },
              { title: 'Lane', render: (_, r) => <span style={{ fontSize: 12 }}>{r.origin ?? '—'} → {r.destination ?? '—'}</span> },
              { title: 'Status', render: (_, r) => <><HealthTag health={r.tracking} /><RiskTag risk={r.risk} />{r.openExceptions > 0 && <Badge count={r.openExceptions} />}</> },
            ]}
            locale={{ emptyText: 'No trips match these filters.' }}
          />
        </Col>
      </Row>

      <Drawer title={chosen ? `${chosen.tripReference} · ${chosen.vehicleReference ?? 'No vehicle'}` : ''} open={!!chosen} onClose={() => setSelected(null)} size={420}
        extra={chosen && <Link to={`/tracking/shipments/${chosen.id}`}><Button type="primary">Open details</Button></Link>}>
        {chosen && <ShipmentTrackingPanel trip={chosen} />}
      </Drawer>
    </>
  )
}
