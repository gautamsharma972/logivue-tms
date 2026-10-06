import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Descriptions, Flex, Form, Input, Modal, Select, Skeleton, Space, Table, Tabs, Tag, Timeline, Typography } from 'antd'
import dayjs from 'dayjs'
import { lazy, Suspense, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DelayReason, TrackedDetailDto } from '@/lib/api/types'
import { DwellIndicator, EtaCard, ExceptionPanel, RouteDeviationBanner, RouteProgress, TrackingGapIndicator } from './components'
import { Age, alertTypeLabel, clock, Delay, executionLabel, HealthTag, km, RiskTag, SeverityTag } from './shared'
import { useTrackingLive } from './useTrackingLive'

const TrackingMap = lazy(() => import('./TrackingMap'))

export const DELAY_REASONS: DelayReason[] = ['Traffic', 'VehicleBreakdown', 'WarehouseDelay', 'CustomerDelay', 'LoadingDelay', 'UnloadingDelay', 'Weather', 'RoadClosure', 'RouteDeviation', 'Documentation', 'BorderCheckpost', 'Accident', 'Other', 'Unknown']
export const reasonOptions = DELAY_REASONS.map((r) => ({ value: r, label: r.replace(/([a-z])([A-Z])/g, '$1 $2') }))

export function ShipmentTrackingPage() {
  const { id = '' } = useParams()
  const live = useTrackingLive()
  const { can } = useAuth()
  const detail = useQuery({ queryKey: queryKeys.tracking.detail(id), queryFn: () => trackingApi.get(id), refetchInterval: live.pollMs })

  if (detail.isLoading) return <Skeleton active />
  if (detail.isError || !detail.data) return <Alert type="error" showIcon message="This trip could not be found." action={<Link to="/tracking">Back to the control tower</Link>} />
  const d = detail.data
  const s = d.summary

  return (
    <>
      <PageHeader
        title={`${s.tripReference} · ${s.vehicleReference ?? 'No vehicle yet'}`}
        description={`${s.origin ?? '—'} → ${s.destination ?? '—'} · ${s.transporterReference ?? 'No carrier'} · ${s.driverName ?? 'No driver'}`}
        actions={<Space><Link to={`/tracking/history?trip=${s.id}`}><Button>Replay this trip</Button></Link><HealthTag health={s.tracking} /><RiskTag risk={s.risk} />{s.openExceptions > 0 && <Tag color="red">{s.openExceptions} open exception(s)</Tag>}</Space>}
      />
      <Tabs
        defaultActiveKey="overview"
        destroyOnHidden
        items={[
          { key: 'overview', label: 'Overview', children: <Overview d={d} canManage={can('tracking.manage')} canLinks={can('tracking.links.manage')} /> },
          { key: 'map', label: 'Live map', children: <LiveMap id={id} d={d} pollMs={live.pollMs} /> },
          { key: 'timeline', label: 'Timeline', children: <TimelineTab id={id} pollMs={live.pollMs} /> },
          { key: 'stops', label: 'Stops', children: <StopsTab d={d} /> },
          { key: 'eta', label: 'ETA', children: <EtaTab id={id} /> },
          { key: 'history', label: 'Location history', children: <HistoryTab id={id} /> },
          { key: 'exceptions', label: 'Exceptions & alerts', children: <ExceptionsTab id={id} /> },
          { key: 'health', label: 'Tracking health', children: <HealthTab id={id} /> },
        ]}
      />
    </>
  )
}

function Overview({ d, canManage, canLinks }: { d: TrackedDetailDto; canManage: boolean; canLinks: boolean }) {
  const s = d.summary
  const { message } = App.useApp()
  const client = useQueryClient()
  const [etaOpen, setEtaOpen] = useState(false)
  const [linkOpen, setLinkOpen] = useState(false)
  const [created, setCreated] = useState<string | null>(null)
  const refresh = () => void client.invalidateQueries({ queryKey: queryKeys.tracking.all })
  const reason = useMutation({ mutationFn: (v: { reason: DelayReason; note: string | null }) => trackingApi.setDelayReason(s.id, v.reason, v.note), onSuccess: () => { void message.success('Delay reason saved'); refresh() }, onError: (e) => void message.error(toApiError(e).message) })
  const clear = useMutation({ mutationFn: () => trackingApi.clearEtaOverride(s.id), onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })
  const link = useMutation({
    mutationFn: (v: { customerName: string | null }) => trackingApi.createLink({ shipmentId: s.id, customerReference: null, customerName: v.customerName, validDays: null }),
    onSuccess: (r) => setCreated(`${window.location.origin}${r.path}`),
    onError: (e) => void message.error(toApiError(e).message),
  })
  const links = useQuery({ queryKey: queryKeys.tracking.links(s.id), queryFn: () => trackingApi.links(s.id), enabled: canLinks })
  const revoke = useMutation({ mutationFn: (linkId: string) => trackingApi.revokeLink(linkId), onSuccess: () => void links.refetch() })

  const route = useQuery({ queryKey: queryKeys.tracking.part(s.id, 'route'), queryFn: () => trackingApi.route(s.id) })
  const analytics = useQuery({ queryKey: queryKeys.tracking.part(s.id, 'analytics'), queryFn: () => trackingApi.analytics(s.id) })
  const health = useQuery({ queryKey: queryKeys.tracking.part(s.id, 'health'), queryFn: () => trackingApi.health(s.id) })
  const eta = useQuery({ queryKey: queryKeys.tracking.part(s.id, 'eta'), queryFn: () => trackingApi.eta(s.id) })
  const exceptions = useQuery({ queryKey: queryKeys.tracking.part(s.id, 'exceptions'), queryFn: () => trackingApi.shipmentExceptions(s.id) })

  return (
    <Flex vertical gap={16}>
      <TrackingGapIndicator health={health.data} />
      <RouteDeviationBanner route={route.data} />
      <DwellIndicator analytics={analytics.data} />
      <ExceptionPanel exceptions={(exceptions.data ?? []).filter((e) => e.status !== 'Resolved' && e.status !== 'Closed')} />
      {eta.data && <EtaCard eta={eta.data} />}
      <Card>
        <Descriptions column={{ xs: 1, md: 2, xl: 3 }} size="small">
          <Descriptions.Item label="Shipment">{s.shipmentReference}</Descriptions.Item>
          <Descriptions.Item label="Stage">{executionLabel(s.execution)}</Descriptions.Item>
          <Descriptions.Item label="Last location"><Age minutes={s.minutesSinceLastLocation} /></Descriptions.Item>
          <Descriptions.Item label="Planned arrival">{clock(s.plannedArrivalAt)}</Descriptions.Item>
          <Descriptions.Item label="Expected arrival">{clock(s.etaAt)}{s.etaOverridden && <Tag style={{ marginLeft: 6 }}>operator</Tag>}</Descriptions.Item>
          <Descriptions.Item label="Delay"><Delay minutes={s.delayMinutes} /></Descriptions.Item>
          <Descriptions.Item label="Confidence">{s.etaConfidence == null ? 'Not available' : `${Math.round(s.etaConfidence * 100)}%`}</Descriptions.Item>
          <Descriptions.Item label="Distance">{km(d.travelledKm)} travelled · {km(s.remainingKm)} to go · plan {km(d.plannedDistanceKm)}</Descriptions.Item>
          <Descriptions.Item label="Route source">{d.routeSource === 'Osrm' ? 'Road geometry' : 'Straight-line estimate'}</Descriptions.Item>
          <Descriptions.Item label="Delay reason">{d.delayReason ? `${d.delayReason}${d.delayNote ? `: ${d.delayNote}` : ''}` : 'Not given'}</Descriptions.Item>
        </Descriptions>
        <RouteProgress progressPct={s.progressPct} travelledKm={d.travelledKm} remainingKm={s.remainingKm} plannedKm={d.plannedDistanceKm} risk={s.risk} tracking={s.tracking} />
        {d.etaOverrideReason && <Typography.Text type="secondary">Operator ETA: {d.etaOverrideReason}</Typography.Text>}
      </Card>
      {canManage && (
        <Card title="Operator actions" size="small">
          <Space wrap>
            <Button onClick={() => setEtaOpen(true)}>Set expected arrival</Button>
            {s.etaOverridden && <Button onClick={() => clear.mutate()}>Go back to the calculated ETA</Button>}
            <Select placeholder="Record a delay reason" style={{ width: 240 }} options={reasonOptions} onChange={(r: DelayReason) => reason.mutate({ reason: r, note: null })} />
          </Space>
        </Card>
      )}
      {canLinks && (
        <Card title="Customer tracking link" size="small" extra={<Button onClick={() => { setCreated(null); setLinkOpen(true) }}>Create link</Button>}>
          <Table size="small" rowKey="id" pagination={false} dataSource={links.data ?? []} locale={{ emptyText: 'No links yet.' }} columns={[
            { title: 'Customer', dataIndex: 'customerName', render: (v: string | null) => v ?? '—' },
            { title: 'Expires', dataIndex: 'expiresAt', render: clock },
            { title: 'Views', dataIndex: 'viewCount' },
            { title: 'Status', dataIndex: 'status' },
            { title: '', render: (_, r) => r.status === 'Active' && <Button size="small" danger onClick={() => revoke.mutate(r.id)}>Revoke</Button> },
          ]} />
        </Card>
      )}
      <EtaModal open={etaOpen} onClose={() => setEtaOpen(false)} id={s.id} onDone={refresh} />
      <Modal open={linkOpen} title="Create a customer link" onCancel={() => { setLinkOpen(false); void links.refetch() }} footer={null} destroyOnHidden>
        {created ? (
          <>
            <Alert type="success" showIcon message="Copy this link now: it cannot be shown again." style={{ marginBottom: 8 }} />
            <Typography.Paragraph copyable code>{created}</Typography.Paragraph>
          </>
        ) : (
          <Form layout="vertical" onFinish={(v: { customerName?: string }) => link.mutate({ customerName: v.customerName ?? s.customerName })}>
            <Form.Item name="customerName" label="Customer name" initialValue={s.customerName}><Input /></Form.Item>
            <Button type="primary" htmlType="submit" loading={link.isPending}>Create</Button>
          </Form>
        )}
      </Modal>
    </Flex>
  )
}

function EtaModal({ open, onClose, id, onDone }: { open: boolean; onClose: () => void; id: string; onDone: () => void }) {
  const { message } = App.useApp()
  const m = useMutation({
    mutationFn: (v: { eta: dayjs.Dayjs; reason: string }) => trackingApi.overrideEta(id, v.eta.toISOString(), v.reason),
    onSuccess: () => { onDone(); onClose() },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Modal open={open} title="Set expected arrival" onCancel={onClose} footer={null} destroyOnHidden>
      <Form layout="vertical" onFinish={(v: { eta: dayjs.Dayjs; reason: string }) => m.mutate(v)}>
        <Form.Item name="eta" label="Expected arrival" rules={[{ required: true }]}><DatePicker showTime style={{ width: '100%' }} /></Form.Item>
        <Form.Item name="reason" label="Why" rules={[{ required: true, message: 'Say why the calculated time is being replaced.' }]}><Input.TextArea rows={2} /></Form.Item>
        <Button type="primary" htmlType="submit" loading={m.isPending}>Save</Button>
      </Form>
    </Modal>
  )
}

function LiveMap({ id, d, pollMs }: { id: string; d: TrackedDetailDto; pollMs: number | false }) {
  const route = useQuery({ queryKey: queryKeys.tracking.part(id, 'route'), queryFn: () => trackingApi.route(id), refetchInterval: pollMs })
  const trail = useQuery({ queryKey: queryKeys.tracking.part(id, 'trail'), queryFn: () => trackingApi.locations(id, { maxPoints: 500, pageSize: 500 }), refetchInterval: pollMs })
  const points = useMemo(() => [...(trail.data?.items ?? [])].filter((l) => l.validation === 'Valid').sort((a, b) => a.capturedAt.localeCompare(b.capturedAt)).map((l) => [l.latitude, l.longitude] as [number, number]), [trail.data])
  const s = d.summary
  return (
    <>
      {s.tracking === 'Lost' && <Alert type="error" showIcon message="Tracking is lost. The pin shows where the vehicle was last seen, not where it is now." style={{ marginBottom: 8 }} />}
      <Suspense fallback={<Skeleton active />}>
        <TrackingMap route={route.data} stops={d.stops} trail={points} position={s.latitude != null && s.longitude != null ? { latitude: s.latitude, longitude: s.longitude } : null} height={520} />
      </Suspense>
    </>
  )
}

function TimelineTab({ id, pollMs }: { id: string; pollMs: number | false }) {
  const q = useQuery({ queryKey: queryKeys.tracking.part(id, 'timeline'), queryFn: () => trackingApi.timeline(id), refetchInterval: pollMs })
  const colour = (k: string) => (k === 'Actual' ? 'green' : k === 'Estimated' ? 'blue' : 'gray')
  return (
    <Timeline
      items={(q.data ?? []).map((e) => ({
        color: colour(e.kind),
        content: (
          <div>
            <strong>{e.label}</strong> <Tag>{e.kind}</Tag>{e.source && <Tag>{e.source}</Tag>}
            <div style={{ fontSize: 12 }}>{clock(e.at)}{e.detail ? ` · ${e.detail}` : ''}{e.reason ? ` · ${e.reason}` : ''}</div>
          </div>
        ),
      }))}
    />
  )
}

function StopsTab({ d }: { d: TrackedDetailDto }) {
  return (
    <Table size="small" rowKey="id" pagination={false} dataSource={d.stops} columns={[
      { title: '#', dataIndex: 'sequence', width: 50 },
      { title: 'Stop', render: (_, r) => <><strong>{r.name}</strong><div style={{ fontSize: 12 }}>{r.kind} · {r.city ?? ''}</div></> },
      { title: 'Planned', dataIndex: 'plannedArrival', render: clock },
      { title: 'Expected', render: (_, r) => clock(r.etaAt) },
      { title: 'Arrived', dataIndex: 'arrivedAt', render: clock },
      { title: 'Departed', dataIndex: 'departedAt', render: clock },
      { title: 'Status', dataIndex: 'status' },
      { title: 'Risk', render: (_, r) => <RiskTag risk={r.risk} /> },
    ]} />
  )
}

function EtaTab({ id }: { id: string }) {
  const q = useQuery({ queryKey: queryKeys.tracking.part(id, 'eta'), queryFn: () => trackingApi.eta(id) })
  if (!q.data) return <Skeleton active />
  const e = q.data
  return (
    <Flex vertical gap={16}>
      <Descriptions bordered size="small" column={{ xs: 1, md: 2 }}>
        <Descriptions.Item label="Planned">{clock(e.plannedAt)}</Descriptions.Item>
        <Descriptions.Item label="Calculated">{clock(e.systemEtaAt)}</Descriptions.Item>
        <Descriptions.Item label="Shown">{clock(e.etaAt)}{e.overridden ? ' (operator)' : ''}</Descriptions.Item>
        <Descriptions.Item label="Confidence">{e.confidence == null ? 'Not available' : `${Math.round(e.confidence * 100)}%`}</Descriptions.Item>
        <Descriptions.Item label="Risk"><RiskTag risk={e.risk} /> {e.level} · <Delay minutes={e.delayMinutes} /></Descriptions.Item>
        <Descriptions.Item label="Method">{e.calculationVersion} (rule-based, not a learned model)</Descriptions.Item>
      </Descriptions>
      <Table size="small" rowKey="stopId" pagination={false} dataSource={e.stops} columns={[
        { title: 'Stop', dataIndex: 'name' }, { title: 'Planned', dataIndex: 'plannedAt', render: clock }, { title: 'Expected', dataIndex: 'etaAt', render: clock },
        { title: 'Delay', dataIndex: 'delayMinutes', render: (v: number) => <Delay minutes={v} /> }, { title: 'Risk', render: (_, r) => <RiskTag risk={r.risk} /> },
      ]} />
      <Card size="small" title="How the estimate has moved">
        <Table size="small" rowKey={(r) => r.predictedAt + r.eta} pagination={{ pageSize: 8 }} dataSource={e.history.filter((h) => h.isFinalDestination)} columns={[
          { title: 'Calculated', dataIndex: 'predictedAt', render: clock }, { title: 'Arrival', dataIndex: 'eta', render: clock }, { title: 'To go', dataIndex: 'remainingKm', render: (v: number) => km(v) },
          { title: 'Confidence', dataIndex: 'confidence', render: (v: number) => `${Math.round(v * 100)}%` }, { title: 'Level', dataIndex: 'riskLevel' },
        ]} />
      </Card>
    </Flex>
  )
}

function HistoryTab({ id }: { id: string }) {
  const [suspicious, setSuspicious] = useState(false)
  const q = useQuery({ queryKey: queryKeys.tracking.part(id, 'locations', suspicious), queryFn: () => trackingApi.locations(id, { includeSuspicious: suspicious, pageSize: 200 }) })
  return (
    <>
      <Space style={{ marginBottom: 8 }}><Button type={suspicious ? 'primary' : 'default'} onClick={() => setSuspicious(!suspicious)}>{suspicious ? 'Showing suspicious points too' : 'Show suspicious points'}</Button></Space>
      <Table size="small" rowKey="id" loading={q.isLoading} dataSource={q.data?.items ?? []} pagination={{ pageSize: 20 }} columns={[
        { title: 'Captured', dataIndex: 'capturedAt', render: (v: string) => dayjs(v).format('DD MMM HH:mm:ss') },
        { title: 'Position', render: (_, r) => `${r.latitude.toFixed(5)}, ${r.longitude.toFixed(5)}` },
        { title: 'Speed', dataIndex: 'speedKph', render: (v: number | null) => (v == null ? '—' : `${Math.round(v)} km/h`) },
        { title: 'Accuracy', dataIndex: 'accuracyMeters', render: (v: number | null) => (v == null ? '—' : `${Math.round(v)} m`) },
        { title: 'Quality', render: (_, r) => <>{r.validation === 'Valid' ? <Tag color="green">Valid</Tag> : <Tag color="orange">{r.validation}</Tag>}{r.isLate && <Tag>Late</Tag>}{r.reasons && <span style={{ fontSize: 12 }}>{r.reasons}</span>}</> },
      ]} />
    </>
  )
}

function ExceptionsTab({ id }: { id: string }) {
  const ex = useQuery({ queryKey: queryKeys.tracking.part(id, 'exceptions'), queryFn: () => trackingApi.shipmentExceptions(id) })
  const alerts = useQuery({ queryKey: queryKeys.tracking.alerts({ shipmentId: id }), queryFn: () => trackingApi.alerts({ shipmentId: id, pageSize: 50 }) })
  return (
    <Flex vertical gap={16}>
      <Card size="small" title="Exceptions (need a person)">
        <Table size="small" rowKey="id" pagination={false} dataSource={ex.data ?? []} locale={{ emptyText: 'None.' }} columns={[
          { title: 'Number', dataIndex: 'number', render: (v: string, r) => <Link to={`/tracking/exceptions?open=${r.id}`}>{v}</Link> },
          { title: 'Type', dataIndex: 'type', render: alertTypeLabel }, { title: 'Severity', render: (_, r) => <SeverityTag severity={r.severity} /> },
          { title: 'Status', dataIndex: 'status' }, { title: 'Raised', dataIndex: 'raisedAt', render: clock },
        ]} />
      </Card>
      <Card size="small" title="Alerts (the system noticed)">
        <Table size="small" rowKey="id" pagination={false} dataSource={alerts.data?.items ?? []} locale={{ emptyText: 'None.' }} columns={[
          { title: 'Type', dataIndex: 'type', render: alertTypeLabel }, { title: 'Severity', render: (_, r) => <SeverityTag severity={r.severity} /> }, { title: 'Message', dataIndex: 'message' },
          { title: 'Status', dataIndex: 'status' }, { title: 'Raised', dataIndex: 'raisedAt', render: clock },
        ]} />
      </Card>
    </Flex>
  )
}

function HealthTab({ id }: { id: string }) {
  const q = useQuery({ queryKey: queryKeys.tracking.part(id, 'health'), queryFn: () => trackingApi.health(id) })
  const a = useQuery({ queryKey: queryKeys.tracking.part(id, 'analytics'), queryFn: () => trackingApi.analytics(id) })
  if (!q.data) return <Skeleton active />
  const h = q.data
  return (
    <Flex vertical gap={16}>
      <Descriptions bordered size="small" column={{ xs: 1, md: 2 }}>
        <Descriptions.Item label="Health"><HealthTag health={h.health} /></Descriptions.Item>
        <Descriptions.Item label="Last seen">{clock(h.lastSeenAt)}</Descriptions.Item>
        <Descriptions.Item label="Device">{h.deviceId ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="Battery">{h.batteryPercentage == null ? 'Not reported' : `${h.batteryPercentage}%`}</Descriptions.Item>
        <Descriptions.Item label="Network">{h.networkType ?? 'Not reported'}</Descriptions.Item>
        <Descriptions.Item label="Location permission">{h.locationPermission ?? 'Not reported'}</Descriptions.Item>
        <Descriptions.Item label="Points">{h.locationCount} stored · {h.suspiciousCount} suspicious · {h.lateCount} late</Descriptions.Item>
      </Descriptions>
      <Card size="small" title="Gaps in tracking">
        <Table size="small" rowKey="id" pagination={false} dataSource={h.gaps} locale={{ emptyText: 'No gaps.' }} columns={[
          { title: 'From', dataIndex: 'gapStart', render: clock }, { title: 'To', dataIndex: 'gapEnd', render: (v: string | null) => (v ? clock(v) : 'Still out') },
          { title: 'Minutes', dataIndex: 'durationMinutes' }, { title: 'Severity', render: (_, r) => <SeverityTag severity={r.severity} /> },
        ]} />
      </Card>
      {a.data && (
        <Card size="small" title="Planned against actual">
          <Descriptions size="small" column={{ xs: 1, md: 2 }}>
            <Descriptions.Item label="Distance">{km(a.data.actualKm)} against {km(a.data.plannedKm)}{a.data.kmVariance != null ? ` (${a.data.kmVariance > 0 ? '+' : ''}${Math.round(a.data.kmVariance * 10) / 10} km)` : ''}</Descriptions.Item>
            <Descriptions.Item label="Time">{a.data.actualMinutes ?? '—'} min against {a.data.plannedMinutes ?? '—'} min</Descriptions.Item>
            <Descriptions.Item label="Stops">{a.data.stopsReached} of {a.data.plannedStops} reached · {a.data.unplannedStops} unplanned</Descriptions.Item>
            <Descriptions.Item label="Standing time">{a.data.totalDwellMinutes} min</Descriptions.Item>
            <Descriptions.Item label="Off route">{a.data.deviationCount} time(s), {a.data.deviationMinutes} min</Descriptions.Item>
          </Descriptions>
        </Card>
      )}
    </Flex>
  )
}
