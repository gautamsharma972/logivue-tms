import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, DatePicker, Flex, Row, Segmented, Skeleton, Statistic, Table, Tooltip, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, Cell, ResponsiveContainer, XAxis, YAxis, Tooltip as ChartTip } from 'recharts'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ComplianceRowDto } from '@/lib/api/types'

const pct = (v: number | null | undefined) => (v == null ? 'Not measurable' : `${v}%`)

export function TrackingDashboardPage() {
  const [groupBy, setGroupBy] = useState<'transporter' | 'driver' | 'vehicle'>('transporter')
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const params = { groupBy, from: range?.[0].format('YYYY-MM-DD'), to: range?.[1].format('YYYY-MM-DD') }
  const summary = useQuery({ queryKey: queryKeys.tracking.summary(), queryFn: () => trackingApi.summary() })
  const compliance = useQuery({ queryKey: queryKeys.tracking.compliance(params), queryFn: () => trackingApi.compliance(params) })
  const s = summary.data
  const c = compliance.data

  const bars = s
    ? [
        { name: 'On time', value: s.onTime, colour: '#389e0d' }, { name: 'At risk', value: s.atRisk, colour: '#d4b106' }, { name: 'Delayed', value: s.delayed, colour: '#fa8c16' },
        { name: 'Stale', value: s.trackingStale, colour: '#d48806' }, { name: 'Lost', value: s.trackingLost, colour: '#cf1322' }, { name: 'Not started', value: s.notStarted, colour: '#bfbfbf' },
      ]
    : []

  return (
    <>
      <PageHeader title="Tracking overview" description="How the fleet is doing right now, and how well tracking itself has worked." actions={<Link to="/tracking">Open the control tower</Link>} />
      <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
        <Col xs={24} lg={12}>
          <Card title="Right now" size="small">
            {summary.isLoading ? <Skeleton active /> : (
              <div style={{ height: 220 }} role="img" aria-label="Trips by condition">
                <ResponsiveContainer>
                  <BarChart data={bars}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="name" />
                    <YAxis allowDecimals={false} />
                    <ChartTip />
                    <Bar dataKey="value">{bars.map((b) => <Cell key={b.name} fill={b.colour} />)}</Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Tracking coverage" size="small" loading={compliance.isLoading}>
            {c && (
              <Row gutter={[12, 12]}>
                <Col span={8}><Statistic title="Coverage" value={pct(c.overall.coveragePct)} /></Col>
                <Col span={8}><Statistic title="Started on time" value={pct(c.overall.startedOnTime)} /></Col>
                <Col span={8}><Statistic title="Stopped properly" value={pct(c.overall.stoppedProperly)} /></Col>
                <Col span={8}><Statistic title="Kept tracking on" value={pct(c.overall.keptActive)} /></Col>
                <Col span={8}><Statistic title="Trips with a lost gap" value={c.overall.lostTrips} /></Col>
                <Col span={8}><Statistic title="Trips with repeated gaps" value={c.overall.tripsWithRepeatedGaps} /></Col>
              </Row>
            )}
          </Card>
        </Col>
      </Row>

      <Card title="Tracking compliance" size="small" extra={<Flex gap={8} wrap>
        <Segmented value={groupBy} onChange={setGroupBy} options={[{ value: 'transporter', label: 'By transporter' }, { value: 'driver', label: 'By driver' }, { value: 'vehicle', label: 'By vehicle' }]} />
        <DatePicker.RangePicker onChange={(v) => setRange(v as [Dayjs, Dayjs] | null)} />
      </Flex>}>
        {c && <Alert type="info" showIcon style={{ marginBottom: 12 }} message={c.note} />}
        <Table<ComplianceRowDto> size="small" rowKey="key" loading={compliance.isLoading} dataSource={c?.rows ?? []} pagination={{ pageSize: 10 }} locale={{ emptyText: 'No tracked trips in this period.' }} columns={[
          { title: groupBy === 'driver' ? 'Driver' : groupBy === 'vehicle' ? 'Vehicle' : 'Transporter', dataIndex: 'name' },
          { title: 'Trips', dataIndex: 'trips' },
          { title: <Tooltip title="Time with a location over the time the trip ran">Coverage</Tooltip>, dataIndex: 'coveragePct', render: pct },
          { title: 'Gaps', dataIndex: 'gaps' },
          { title: 'Stale', dataIndex: 'staleTrips' },
          { title: 'Lost', dataIndex: 'lostTrips' },
          { title: <Tooltip title={`Within ${c?.rules.startToleranceMinutes ?? 30} min of the planned start`}>Started on time</Tooltip>, dataIndex: 'startedOnTime', render: pct },
          { title: <Tooltip title={`At least ${c?.rules.minCoveragePct ?? 90}% coverage`}>Kept on</Tooltip>, dataIndex: 'keptActive', render: pct },
          { title: 'Stopped properly', dataIndex: 'stoppedProperly', render: pct },
          { title: <Tooltip title={`${c?.rules.repeatedGapCount ?? 3} or more gaps on one trip`}>Repeated gaps</Tooltip>, dataIndex: 'tripsWithRepeatedGaps' },
        ]} />
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>The rules behind these rates are on the Tracking rules page (tracking.compliance).</Typography.Text>
      </Card>
    </>
  )
}
