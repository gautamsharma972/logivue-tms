import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, Row, Statistic, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { opsApi } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'

function Tile({ title, value, to, hint, warn }: { title: string; value: number | string; to: string; hint?: string; warn?: boolean }) {
  return (
    <Col xs={12} md={8} xl={6}>
      <Card size="small" style={warn ? { borderColor: '#ff4d4f' } : undefined}>
        <Statistic title={title} value={value} styles={{ content: warn ? { color: '#cf1322' } : undefined }} />
        {hint && <Typography.Text type="secondary">{hint}</Typography.Text>}
        <div><Link to={to}>Open</Link></div>
      </Card>
    </Col>
  )
}

export function OpsDashboardPage() {
  const q = useQuery({ queryKey: ['ops', 'dashboard'], queryFn: opsApi.dashboard, refetchInterval: 60_000 })
  const d = q.data
  return (
    <>
      <PageHeader title="Operations dashboard" description="What needs doing, from dispatch to payment." />
      {q.isError && <Alert type="error" showIcon title="The dashboard could not be loaded." />}
      {d && (
        <>
          <Typography.Title level={5}>Dispatch</Typography.Title>
          <Row gutter={[12, 12]}>
            <Tile title="LRs not on a trip" value={d.lrsNotOnTrip} to="/ops/lrs" />
            <Tile title="Active trips" value={d.tripsActive} to="/ops/trips" />
            <Tile title="In transit" value={d.lrsInTransit} to="/ops/lrs" hint={`${d.arrivedAwaitingUnloading} arrived, unloading`} />
          </Row>
          <Typography.Title level={5} style={{ marginTop: 20 }}>Proof of delivery</Typography.Title>
          <Row gutter={[12, 12]}>
            <Tile title="POD pending" value={d.podPending} to="/ops/pod" hint={d.podPending ? `oldest ${d.oldestPendingPodDays} days` : undefined} warn={d.oldestPendingPodDays >= 7} />
            <Tile title="POD to review" value={d.podToReview} to="/ops/pod" />
            <Tile title="POD rejected" value={d.podRejected} to="/ops/pod" warn={d.podRejected > 0} />
          </Row>
          {d.outstanding !== null && (
            <>
          <Typography.Title level={5} style={{ marginTop: 20 }}>Billing</Typography.Title>
          <Row gutter={[12, 12]}>
            <Tile title="Billing on hold" value={d.billingOnHold ?? 0} to="/ops/billing" warn={(d.billingOnHold ?? 0) > 0} />
            <Tile title="Ready to invoice" value={d.readyToInvoice ?? 0} to="/ops/billing" />
            <Tile title="Invoices to submit" value={d.invoicesToSubmit ?? 0} to="/ops/invoices" hint={`${d.submissionsRejected ?? 0} rejected by customer`} warn={(d.submissionsRejected ?? 0) > 0} />
            <Tile title="Detention this month" value={formatInrExact(d.detentionThisMonth ?? 0)} to="/ops/billing" />
            <Tile title="Outstanding" value={formatInrExact(d.outstanding ?? 0)} to="/ops/invoices" hint={`${formatInrExact(d.overdue ?? 0)} overdue`} warn={(d.overdue ?? 0) > 0} />
          </Row>
            </>
          )}
        </>
      )}
    </>
  )
}
