import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Result, Skeleton, Steps, Typography } from 'antd'
import { lazy, Suspense } from 'react'
import { useParams } from 'react-router-dom'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { clock } from './shared'

const TrackingMap = lazy(() => import('./TrackingMap'))

/** The customer's view, opened from a link with no sign-in. It shows where the shipment is and when it should arrive, and nothing else. */
export function CustomerTrackingPage() {
  const { token = '' } = useParams()
  const q = useQuery({ queryKey: queryKeys.tracking.customer(token), queryFn: () => trackingApi.customerView(token), refetchInterval: 30_000, retry: false })
  if (q.isLoading) return <div style={{ maxWidth: 720, margin: '24px auto', padding: 16 }}><Skeleton active /></div>
  if (q.isError || !q.data) return <Result status="404" title="This link is not available" subTitle="It may have expired or been withdrawn. Ask the sender for a new one." />
  const d = q.data
  const current = Math.max(0, d.steps.findIndex((s) => s.state === 'current'))
  return (
    <div style={{ maxWidth: 720, margin: '0 auto', padding: 16 }}>
      <Typography.Title level={3} style={{ marginBottom: 0 }}>Shipment {d.shipmentReference}</Typography.Title>
      <Typography.Text type="secondary">{d.origin ?? '—'} → {d.destination ?? '—'}</Typography.Text>
      <Alert style={{ margin: '16px 0' }} type={d.delivered ? 'success' : d.riskLabel === 'On time' ? 'info' : 'warning'} showIcon message={d.statusLabel}
        description={d.delivered ? `Delivered ${clock(d.deliveredAt)}` : d.etaAt ? `Expected ${clock(d.etaAt)} (${d.riskLabel})` : d.riskLabel} />
      {!d.delivered && d.latitude != null && d.longitude != null && (
        <Card size="small" style={{ marginBottom: 16 }} title={d.locationLabel ?? 'Last known position'} extra={<span>{clock(d.locationAsOf)}</span>}>
          <Suspense fallback={<Skeleton active />}>
            <TrackingMap position={{ latitude: d.latitude, longitude: d.longitude }} route={{ points: d.route, lengthKm: 0, source: 'Estimate', plannedDistanceKm: null, plannedDurationMinutes: null, travelledKm: 0, remainingKm: null, progressPct: null, stops: [], deviations: [] }} height={300} />
          </Suspense>
        </Card>
      )}
      <Steps direction="vertical" current={current} items={d.steps.map((s) => ({ title: s.label, description: s.at ? clock(s.at) : undefined, status: s.state === 'done' ? 'finish' : s.state === 'current' ? 'process' : 'wait' }))} />
      <Typography.Paragraph type="secondary" style={{ marginTop: 16 }}>Last updated {clock(d.asOf)}. The page refreshes by itself.</Typography.Paragraph>
    </div>
  )
}
