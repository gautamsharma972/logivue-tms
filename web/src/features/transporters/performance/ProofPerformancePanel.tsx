import { useQuery } from '@tanstack/react-query'
import { Card, Col, Row, Skeleton, Statistic, Typography } from 'antd'
import { proofPerformanceApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'

const rate = (v: number | null) => (v == null ? 'Not applicable' : `${Math.round(v * 1000) / 10}%`)

/** How this transporter's deliveries and proofs of delivery have gone. A rate with nothing to measure reads "Not applicable", never zero. */
export function ProofPerformancePanel({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const q = useQuery({
    queryKey: [...queryKeys.deliveries.proofPerformance(transporterId), from, to],
    queryFn: () => proofPerformanceApi.get(transporterId, from, to),
    retry: false,
  })
  if (q.isLoading) return <Skeleton active />
  if (q.isError || !q.data) return null
  const d = q.data
  const items: [string, string][] = [
    ['Delivered on time', rate(d.onTimeRate)], ['Proof sent in time', rate(d.proofInTimeRate)], ['Accepted first time', rate(d.firstTimeAcceptanceRate)],
    ['Proofs rejected', rate(d.rejectionRate)], ['Shortage rate', rate(d.shortageRate)], ['Damage rate', rate(d.damageRate)],
    ['Customer refusals', String(d.refusals)], ['Failed deliveries', String(d.failures)],
  ]
  return (
    <Card title="Delivery and proof record" extra={<Typography.Text type="secondary">{d.delivered} of {d.deliveries} delivered</Typography.Text>}>
      <Row gutter={[12, 12]}>
        {items.map(([title, value]) => <Col xs={12} md={8} xl={6} key={title}><Statistic title={title} value={value} styles={{ content: { fontSize: 20 } }} /></Col>)}
      </Row>
    </Card>
  )
}
