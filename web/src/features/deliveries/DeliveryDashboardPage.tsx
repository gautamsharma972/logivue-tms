import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, DatePicker, Drawer, Flex, Row, Skeleton, Statistic, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { deliveriesApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AgeingStage, ComplianceMetricsDto, ProofAgeingDto } from '@/lib/api/types'

const rate = (v: number | null | undefined) => (v == null ? 'Not applicable' : `${Math.round(v * 1000) / 10}%`)
const hours = (v: number | null | undefined) => (v == null ? 'Not applicable' : `${v} h`)

function Tile({ title, value, to, warn }: { title: string; value: number | string; to?: string; warn?: boolean }) {
  const body = <Statistic title={title} value={value} styles={{ content: warn && Number(value) > 0 ? { color: '#cf1322' } : undefined }} />
  return <Card size="small">{to ? <Link to={to} style={{ color: 'inherit' }}>{body}</Link> : body}</Card>
}

function AgeingWidget({ ageing, onDrill }: { ageing: ProofAgeingDto; onDrill: (stage?: AgeingStage, bucket?: number) => void }) {
  const total = Math.max(1, ...ageing.bucketTotals)
  return (
    <Card title="Proof ageing" extra={<Typography.Text type="secondary">Everything still waiting, whatever its date. Click a card or a bar to see the deliveries.</Typography.Text>}>
      <Row gutter={[12, 12]}>
        {ageing.stages.map((s) => (
          <Col xs={12} md={6} key={s.stage}>
            <Card size="small" hoverable onClick={() => onDrill(s.stage)} aria-label={s.label}>
              <Statistic title={s.label} value={s.count} />
              <Typography.Text type={s.overdue > 0 ? 'danger' : 'secondary'}>{s.overdue} past the {s.targetHours} h target</Typography.Text>
            </Card>
          </Col>
        ))}
      </Row>
      <Flex vertical gap={6} style={{ marginTop: 16 }}>
        {ageing.bucketLabels.map((label, i) => (
          <Flex key={label} align="center" gap={12} style={{ cursor: 'pointer' }} onClick={() => onDrill(undefined, i)}>
            <span style={{ width: 110 }}>{label}</span>
            <div style={{ flex: 1, background: 'rgba(127,127,127,0.15)', borderRadius: 4, height: 14 }}>
              <div style={{ width: `${(ageing.bucketTotals[i]! / total) * 100}%`, background: i >= 3 ? '#cf1322' : i >= 2 ? '#fa8c16' : '#2f5bea', height: 14, borderRadius: 4 }} />
            </div>
            <strong style={{ width: 40, textAlign: 'right' }}>{ageing.bucketTotals[i]}</strong>
          </Flex>
        ))}
      </Flex>
      <Row gutter={16} style={{ marginTop: 16 }}>
        {([['Top overdue transporters', ageing.topTransporters], ['Top overdue customers', ageing.topCustomers], ['Top overdue locations', ageing.topLocations]] as const).map(([title, rows]) => (
          <Col xs={24} md={8} key={title}>
            <Typography.Text strong>{title}</Typography.Text>
            {rows.length === 0 ? <div><Typography.Text type="secondary">Nothing is overdue.</Typography.Text></div> : rows.map((r) => (
              <div key={r.name}>{r.name} <Tag color="red">{r.overdue} overdue</Tag> <Typography.Text type="secondary">of {r.total}</Typography.Text></div>
            ))}
          </Col>
        ))}
      </Row>
    </Card>
  )
}

function Performance({ m }: { m: ComplianceMetricsDto }) {
  const items: [string, string][] = [
    ['Proof submitted within target', rate(m.submissionCompliance)], ['Proof acceptance rate', rate(m.acceptanceRate)], ['Proof rejection rate', rate(m.rejectionRate)],
    ['Delivered on time', rate(m.onTimeRate)], ['Average time to submit', hours(m.averageSubmissionHours)], ['Average time to review', hours(m.averageReviewHours)],
    ['Average time to correct', hours(m.averageResubmissionHours)],
  ]
  return (
    <Card title="Performance">
      <Row gutter={[12, 12]}>
        {items.map(([title, value]) => <Col xs={12} md={8} xl={6} key={title}><Statistic title={title} value={value} styles={{ content: { fontSize: 20 } }} /></Col>)}
      </Row>
      <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>
        &quot;Not applicable&quot; means there was nothing to measure (no deliveries needing a proof yet): it is not a zero.
      </Typography.Paragraph>
    </Card>
  )
}

export function DeliveryDashboardPage() {
  const [range, setRange] = useState<[string, string] | null>(null)
  const [drill, setDrill] = useState<{ stage?: AgeingStage; bucket?: number } | null>(null)
  const params = { from: range?.[0], to: range?.[1] }
  const summary = useQuery({ queryKey: queryKeys.deliveries.dashboard(params), queryFn: () => deliveriesApi.dashboard(params) })
  const compliance = useQuery({ queryKey: queryKeys.deliveries.compliance(params), queryFn: () => deliveriesApi.compliance(params) })
  const ageing = useQuery({ queryKey: queryKeys.deliveries.ageing(), queryFn: () => deliveriesApi.ageing() })
  const items = useQuery({
    queryKey: queryKeys.deliveries.ageingItems(drill ?? {}), queryFn: () => deliveriesApi.ageingItems({ stage: drill?.stage, bucket: drill?.bucket, pageSize: 50 }), enabled: drill !== null,
  })
  const s = summary.data
  const stageLabel = (stage: AgeingStage) => ageing.data?.stages.find((x) => x.stage === stage)?.label ?? stage
  const drillTitle = [drill?.stage ? stageLabel(drill.stage) : null, drill?.bucket != null ? ageing.data?.bucketLabels[drill.bucket] : null].filter(Boolean).join(' · ') || 'Deliveries waiting on a proof'

  return (
    <>
      <PageHeader
        title="Delivery & proof dashboard"
        description="Where deliveries and their proofs stand, how long things are waiting, and who is behind."
        actions={<DatePicker.RangePicker aria-label="Period" onChange={(v) => setRange(v?.[0] && v[1] ? [dayjs(v[0]).format('YYYY-MM-DD'), dayjs(v[1]).format('YYYY-MM-DD')] : null)} />}
      />
      {summary.isError && <Alert type="error" showIcon title={summary.error.message} style={{ marginBottom: 16 }} />}
      {summary.isLoading || !s ? <Skeleton active /> : (
        <Flex vertical gap={16}>
          <Row gutter={[12, 12]}>
            <Col xs={12} md={6} xl={4}><Tile title="Deliveries today" value={s.deliveriesToday} to="/delivery" /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Delivered" value={s.delivered + s.closed} to="/delivery?status=Delivered" /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Partially delivered" value={s.partiallyDelivered} to="/delivery?status=PartiallyDelivered" /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Failed" value={s.failed} to="/delivery?status=Failed" warn /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Customer refusals" value={s.refused} to="/delivery?status=Refused" warn /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Open exceptions" value={s.openExceptions} to="/delivery/exceptions" warn /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Proof pending" value={s.podPending + s.podInPreparation} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Proof submitted" value={s.podSubmitted} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Under review" value={s.podUnderReview} to="/delivery/pods" /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Proof rejected" value={s.podRejected + s.podResubmissionRequired} warn /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Proof accepted" value={s.podAccepted} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Open claims" value={s.openClaims} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Shortage cases" value={s.shortageCases} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Damage cases" value={s.damageCases} /></Col>
            <Col xs={12} md={6} xl={4}><Tile title="Exceptions overdue" value={s.overdueExceptions} warn /></Col>
          </Row>
          {compliance.data && <Performance m={compliance.data.overall} />}
          {ageing.data && <AgeingWidget ageing={ageing.data} onDrill={(stage, bucket) => setDrill({ stage, bucket })} />}
        </Flex>
      )}

      <Drawer open={drill !== null} onClose={() => setDrill(null)} size={940} destroyOnHidden title={drillTitle}>
        <Table
          size="small" rowKey="deliveryId" loading={items.isLoading} pagination={false} dataSource={items.data?.items} scroll={{ x: 'max-content' }}
          columns={[
            { title: 'Delivery', key: 'd', render: (_, i) => <Link to={i.podId ? `/delivery/pods/${i.podId}` : `/delivery/${i.deliveryId}`}>{i.deliveryNumber}</Link> },
            { title: 'Customer', dataIndex: 'customerName' }, { title: 'Transporter', dataIndex: 'transporterReference', render: (v: string | null) => v ?? '—' },
            { title: 'Waiting for', dataIndex: 'stage', render: (v: AgeingStage) => stageLabel(v) }, { title: 'Age', key: 'a', render: (_, i) => `${i.bucket} · ${i.ageHours} h` },
            { title: '', dataIndex: 'overdue', render: (v: boolean, i) => (v ? <Tag color="red">Past {i.targetHours} h</Tag> : null) },
          ]}
        />
      </Drawer>
    </>
  )
}
