import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Descriptions, Flex, Popconfirm, Space, Tag, Timeline } from 'antd'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { useDriverTracking } from './driverTracking'
import { clock, executionLabel } from './shared'

const ago = (ms: number | null) => {
  if (ms == null) return 'No location yet'
  const m = Math.floor((Date.now() - ms) / 60_000)
  return m < 1 ? 'Just now' : `${m} min ago`
}

/** One trip, from the driver's side: its stops, whether tracking is on, when the last GPS fix was, the network and the battery. */
export function DriverTripPage() {
  const { trip = '' } = useParams()
  const { message } = App.useApp()
  const client = useQueryClient()
  const t = useDriverTracking()
  const trips = useQuery({ queryKey: queryKeys.tracking.trips, queryFn: trackingApi.trips })
  const found = trips.data?.find((x) => x.tripReference === trip)
  const refresh = () => void client.invalidateQueries({ queryKey: queryKeys.tracking.trips })
  const start = useMutation({ mutationFn: () => t.start(trip), onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })
  const stop = useMutation({ mutationFn: (completed: boolean) => t.stop(trip, completed), onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })
  const tracking = t.active === trip

  if (trips.isLoading) return null
  if (!found) return <Alert type="warning" showIcon message="This trip is not assigned to you." action={<Link to="/tracking/drive">Back</Link>} />
  return (
    <>
      <PageHeader title={`Trip ${found.tripReference}`} description={`${found.origin ?? '—'} → ${found.destination ?? '—'}`} actions={<Tag>{executionLabel(found.execution)}</Tag>} />
      {t.problem && <Alert type="error" showIcon message={t.problem} style={{ marginBottom: 12 }} />}
      {t.network === 'Offline' && <Alert type="warning" showIcon style={{ marginBottom: 12 }} message="No network. Locations are being saved on this phone and will be sent when it is back." />}
      <Card size="small" style={{ marginBottom: 12 }} title="Tracking">
        <Descriptions column={1} size="small">
          <Descriptions.Item label="Tracking">{tracking ? <Tag color="green">Active</Tag> : <Tag>Off</Tag>}</Descriptions.Item>
          <Descriptions.Item label="Last GPS">{ago(t.lastFixAt)}</Descriptions.Item>
          <Descriptions.Item label="Network">{t.network}</Descriptions.Item>
          <Descriptions.Item label="Battery">{t.battery == null ? 'Not available on this phone' : `${t.battery}%`}</Descriptions.Item>
          <Descriptions.Item label="Waiting to send">{t.waiting}{t.lastSentAt ? ` · last sent ${clock(new Date(t.lastSentAt).toISOString())}` : ''}</Descriptions.Item>
        </Descriptions>
        {tracking && <Alert style={{ marginTop: 8 }} type="info" showIcon message="This page cannot track once it is closed or the screen is locked." />}
        <Space style={{ marginTop: 12 }} wrap>
          {tracking ? (
            <>
              <Popconfirm title="Finish this trip?" description="Tracking will stop for good." onConfirm={() => stop.mutate(true)}><Button type="primary" danger loading={stop.isPending}>Finish trip</Button></Popconfirm>
              <Button onClick={() => stop.mutate(false)} loading={stop.isPending}>Stop tracking</Button>
            </>
          ) : (
            <Button type="primary" disabled={!found.canStart || !!t.active} loading={start.isPending} onClick={() => start.mutate()}>Start tracking</Button>
          )}
          <Link to="/tracking/drive/sync"><Button>Unsent locations</Button></Link>
        </Space>
        {!found.canStart && !tracking && <Alert style={{ marginTop: 8 }} type="warning" showIcon message="This trip cannot be tracked right now." />}
      </Card>
      <Card size="small" title="Stops">
        <Timeline items={found.stops.map((s) => ({ color: s.status === 'Departed' ? 'green' : s.status === 'Arrived' ? 'blue' : 'gray', content: <Flex vertical><strong>{s.name}</strong><span style={{ fontSize: 12 }}>{s.kind} · {s.status}{s.plannedArrival ? ` · planned ${clock(s.plannedArrival)}` : ''}</span></Flex> }))} />
      </Card>
    </>
  )
}
