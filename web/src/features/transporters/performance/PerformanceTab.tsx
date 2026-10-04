import { CalculatorOutlined, TrophyOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Empty, Flex, Progress, Row, Skeleton, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Can } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { KpiDto, KpiType, ScorecardDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { BenchmarkPanel } from './BenchmarkPanel'
import { kpiLabels, lowerIsBetter, scoredKpis } from './constants'
import { OperationsPanel } from './OperationsPanel'

const pct = (v: number | null | undefined) => (v == null ? '—' : `${v.toFixed(v % 1 === 0 ? 0 : 1)}%`)

function KpiCard({ kpi, row }: { kpi: KpiType; row: KpiDto | undefined }) {
  const low = lowerIsBetter.includes(kpi)
  const value = row?.value ?? null
  const good = value === null ? undefined : low ? value <= 3 : value >= 90
  return (
    <Card size="small" style={{ height: '100%' }}>
      <Typography.Text type="secondary">{kpiLabels[kpi]}{low ? ' (lower is better)' : ''}</Typography.Text>
      <Typography.Title level={3} style={{ margin: '4px 0' }}>{value === null ? 'Not measurable' : pct(value)}</Typography.Title>
      {value !== null && !low && <Progress percent={Math.min(100, value)} showInfo={false} size="small" status={good ? 'success' : value < 75 ? 'exception' : 'normal'} />}
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>{row && row.denominator > 0 ? `${row.numerator} of ${row.denominator}` : 'No records in this period'}</Typography.Text>
    </Card>
  )
}

function Scorecards({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const cards = useQuery({ queryKey: queryKeys.performance.scorecards(transporterId), queryFn: () => performanceApi.scorecards(transporterId) })
  const generate = useMutation({
    mutationFn: () => performanceApi.generateScorecard(transporterId, from, to),
    onSuccess: async (card) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.performance.scorecards(transporterId) })
      void message.success(card.overallScore === null ? 'Scorecard saved: not enough loads yet to give a score' : `Scorecard saved: ${card.overallScore}`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  return (
    <Card
      title="Scorecards"
      extra={<Can permission="transporters.performance.manage"><Button icon={<TrophyOutlined />} loading={generate.isPending} onClick={() => generate.mutate()}>Generate for this period</Button></Can>}
    >
      <Typography.Paragraph type="secondary">
        A scorecard pools the stored months of the period. A KPI with too few loads is left out and the other weights are scaled up, so missing data neither helps nor hurts. Each one is kept with the weights it used.
      </Typography.Paragraph>
      <Table<ScorecardDto>
        size="small"
        rowKey="id"
        loading={cards.isLoading}
        pagination={false}
        dataSource={cards.data ?? []}
        locale={{ emptyText: 'No scorecards yet.' }}
        expandable={{
          expandedRowRender: (c) => (
            <Table
              size="small"
              rowKey="kpi"
              pagination={false}
              dataSource={c.kpis}
              columns={[
                { title: 'KPI', dataIndex: 'kpi', render: (k: KpiType) => kpiLabels[k] },
                { title: 'Value', dataIndex: 'value', align: 'right', render: (v: number | null) => (v === null ? 'Too few loads' : pct(v)) },
                { title: 'Loads', key: 'n', align: 'right', render: (_, k) => `${k.numerator} / ${k.denominator}` },
                { title: 'Weight', dataIndex: 'weight', align: 'right', render: (w: number) => `${w}%` },
                { title: 'Contribution', dataIndex: 'weightedScore', align: 'right', render: (w: number | null) => w ?? '—' },
              ]}
            />
          ),
        }}
        columns={[
          { title: 'Period', key: 'p', render: (_, c) => `${c.periodStart} → ${c.periodEnd}` },
          { title: 'Score', dataIndex: 'overallScore', align: 'right', render: (s: number | null) => (s === null ? <Tag>Not enough data</Tag> : <Typography.Text strong>{s}</Typography.Text>) },
          { title: 'Generated', dataIndex: 'generatedAt', render: formatDateTime },
        ]}
      />
    </Card>
  )
}

/** KPIs for a period with the records behind them, scorecards, benchmarks, executions and lanes. */
export function PerformanceTab({ transporterId }: { transporterId: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().subtract(89, 'day'), dayjs()])
  const from = range[0].format('YYYY-MM-DD')
  const to = range[1].format('YYYY-MM-DD')
  const performance = useQuery({ queryKey: queryKeys.performance.detail(transporterId, from, to), queryFn: () => performanceApi.get(transporterId, from, to) })
  const recalc = useMutation({
    mutationFn: () => performanceApi.recalculate(transporterId, from, to),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
      void message.success('KPIs rebuilt from the operational records')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const byKpi = new Map((performance.data?.kpis ?? []).map((k) => [k.kpi, k]))
  const m = performance.data?.metrics

  return (
    <Flex vertical gap={16}>
      <Card size="small">
        <Flex gap={12} wrap align="center" justify="space-between">
          <Flex gap={12} wrap align="center">
            <Typography.Text strong>Period</Typography.Text>
            <DatePicker.RangePicker aria-label="Performance period" allowClear={false} value={range} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          </Flex>
          <Can permission="transporters.performance.manage">
            <Tooltip title="KPIs are updated as loads move. Rebuild them if you corrected history.">
              <Button icon={<CalculatorOutlined />} loading={recalc.isPending} onClick={() => recalc.mutate()}>Recalculate</Button>
            </Tooltip>
          </Can>
        </Flex>
      </Card>

      {performance.isError && <Alert type="error" showIcon title={performance.error.message} />}
      {performance.isLoading ? <Skeleton active /> : (
        <>
          <Row gutter={[12, 12]}>
            {scoredKpis.map((k) => <Col key={k} xs={24} sm={12} xl={6}><KpiCard kpi={k} row={byKpi.get(k)} /></Col>)}
          </Row>

          {m && (
            <Card title="What is behind the numbers" size="small">
              <Descriptions size="small" column={{ xs: 1, md: 2, xl: 3 }} layout="vertical" colon={false}>
                <Descriptions.Item label="Pickups measured">{m.measuredPickups}{m.notMeasurablePickups > 0 && <Typography.Text type="secondary"> (+{m.notMeasurablePickups} without a planned time)</Typography.Text>}</Descriptions.Item>
                <Descriptions.Item label="Late pickups">{m.latePickupsCarrier} carrier · {m.latePickupsNonCarrier} not the carrier · {m.latePickupsUnattributed} need a reason</Descriptions.Item>
                <Descriptions.Item label="Average pickup delay">{m.averagePickupDelayMinutes == null ? '—' : `${m.averagePickupDelayMinutes} min`}</Descriptions.Item>
                <Descriptions.Item label="Deliveries measured">{m.measuredDeliveries}{m.notMeasurableDeliveries > 0 && <Typography.Text type="secondary"> (+{m.notMeasurableDeliveries} without a planned time)</Typography.Text>}</Descriptions.Item>
                <Descriptions.Item label="Late deliveries">{m.lateDeliveriesCarrier} carrier · {m.lateDeliveriesNonCarrier} not the carrier · {m.lateDeliveriesUnattributed} need a reason</Descriptions.Item>
                <Descriptions.Item label="Average delivery delay">{m.averageDeliveryDelayMinutes == null ? '—' : `${m.averageDeliveryDelayMinutes} min`}</Descriptions.Item>
                <Descriptions.Item label="Proofs outstanding">{m.pendingPod}</Descriptions.Item>
                <Descriptions.Item label="Average time to upload proof">{m.averagePodSubmissionHours == null ? '—' : `${m.averagePodSubmissionHours} h`}</Descriptions.Item>
                <Descriptions.Item label="Placements due">{m.duePlacements} ({m.noShows} no-show, {m.replacements} replaced)</Descriptions.Item>
              </Descriptions>
              {(m.latePickupsUnattributed > 0 || m.lateDeliveriesUnattributed > 0) && (
                <Alert type="info" showIcon style={{ marginTop: 12 }} title="Some late loads have no reason yet" description="Only delays caused by the carrier count against it. Give each late load a reason under Loads below, and the KPI is updated." />
              )}
            </Card>
          )}

          <Card title="Month by month" size="small">
            {performance.data && performance.data.months.length > 0 ? (
              <Table
                size="small"
                rowKey="month"
                pagination={false}
                dataSource={performance.data.months}
                scroll={{ x: 'max-content' }}
                columns={[
                  { title: 'Month', dataIndex: 'month', render: (d: string) => dayjs(d).format('MMM YYYY') },
                  ...scoredKpis.map((k) => ({
                    title: kpiLabels[k],
                    key: k,
                    align: 'right' as const,
                    render: (_: unknown, row: { kpis: KpiDto[] }) => pct(row.kpis.find((x) => x.kpi === k)?.value),
                  })),
                ]}
              />
            ) : <Empty description="No stored months yet. They appear as loads are accepted, dispatched and delivered, or after Recalculate." />}
          </Card>

          <Scorecards transporterId={transporterId} from={from} to={to} />
          <BenchmarkPanel transporterId={transporterId} from={from} to={to} />
          <OperationsPanel transporterId={transporterId} />
        </>
      )}
    </Flex>
  )
}
