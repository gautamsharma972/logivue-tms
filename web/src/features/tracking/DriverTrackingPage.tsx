import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Flex, Tag, Typography } from 'antd'
import { Link, Outlet } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { DriverTrackingProvider, useDriverTracking } from './driverTracking'
import { clock, executionLabel } from './shared'

/** Everything under /tracking/drive shares one tracking session, so it carries on while the driver moves between the screens. */
export function DriverTrackingArea() {
  return (
    <DriverTrackingProvider>
      <Outlet />
    </DriverTrackingProvider>
  )
}

/** "My Trip": the trips this driver may track, and whether tracking is on. */
export function DriverTrackingPage() {
  const { can } = useAuth()
  const trips = useQuery({ queryKey: queryKeys.tracking.trips, queryFn: trackingApi.trips })
  const t = useDriverTracking()
  if (!can('tracking.execute')) return <Alert type="warning" showIcon message="Your account is not allowed to track trips." />
  return (
    <>
      <PageHeader title="My trips" description="Open a trip to start tracking. Keep the page open and the screen on while you drive." actions={<Link to="/tracking/drive/sync"><Button>Unsent locations ({t.waiting})</Button></Link>} />
      {t.active && <Alert type="info" showIcon style={{ marginBottom: 12 }} message={`Tracking ${t.active}`} action={<Link to={`/tracking/drive/${t.active}`}>Open</Link>} />}
      <Flex vertical gap={12}>
        {(trips.data ?? []).map((trip) => (
          <Card key={trip.tripReference} size="small" title={<>{trip.tripReference} <Tag>{executionLabel(trip.execution)}</Tag></>} extra={trip.vehicleReference}
            actions={[<Link key="open" to={`/tracking/drive/${trip.tripReference}`}>{t.active === trip.tripReference ? 'View trip' : 'Open trip'}</Link>]}>
            <Typography.Text>{trip.origin ?? '—'} → {trip.destination ?? '—'}</Typography.Text>
            {trip.etaAt && <div><Typography.Text type="secondary">Expected {clock(trip.etaAt)}</Typography.Text></div>}
          </Card>
        ))}
        {trips.data?.length === 0 && <Typography.Text type="secondary">No trips are assigned to you right now.</Typography.Text>}
      </Flex>
    </>
  )
}
