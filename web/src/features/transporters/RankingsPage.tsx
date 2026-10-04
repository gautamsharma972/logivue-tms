import { useQuery } from '@tanstack/react-query'
import { Alert, Card, DatePicker, Flex, Select, Table, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { performanceApi, transportersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { RankedTransporterDto, RankingMetric } from '@/lib/api/types'
import { INDIAN_STATES } from '@/lib/indiaStates'
import { kpiLabels, scoredKpis } from './performance/constants'

const metricOptions: { value: RankingMetric; label: string }[] = [
  { value: 'OverallScore', label: 'Overall score' },
  ...scoredKpis.map((k) => ({ value: k as RankingMetric, label: kpiLabels[k] })),
]

/** Transporters ranked by score or by one KPI, for a period, a vehicle type or a region. Transporters with too few loads are listed but not ranked. */
export function RankingsPage() {
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().subtract(89, 'day').startOf('month'), dayjs().endOf('month')])
  const [metric, setMetric] = useState<RankingMetric>('OverallScore')
  const [region, setRegion] = useState<string>()
  const [vehicleTypeId, setVehicleTypeId] = useState<string>()
  const from = range[0].format('YYYY-MM-DD')
  const to = range[1].format('YYYY-MM-DD')
  const params = { from, to, metric, region, vehicleTypeId }
  const types = useQuery({ queryKey: queryKeys.transporters.vehicleTypes, queryFn: transportersApi.vehicleTypes })
  const ranking = useQuery({ queryKey: queryKeys.performance.rankings(params), queryFn: () => performanceApi.rankings(params) })
  const lower = metric === 'ClaimsRate'

  return (
    <>
      <PageHeader title="Transporter rankings" description="Who delivers best, on the lane or vehicle type you choose. Only whole months inside the period are used." />
      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap align="center">
          <DatePicker.RangePicker aria-label="Ranking period" picker="month" allowClear={false} value={range} onChange={(v) => v?.[0] && v[1] && setRange([v[0].startOf('month'), v[1].endOf('month')])} />
          <Select aria-label="Rank by" style={{ width: 200 }} value={metric} options={metricOptions} onChange={setMetric} virtual={false} />
          <Select aria-label="Region" style={{ width: 200 }} allowClear placeholder="All regions" showSearch virtual={false} value={region} onChange={setRegion} options={INDIAN_STATES.map((s) => ({ value: s.toUpperCase(), label: s }))} />
          <Select aria-label="Vehicle type" style={{ width: 220 }} allowClear placeholder="All vehicle types" showSearch optionFilterProp="label" virtual={false} value={vehicleTypeId} onChange={setVehicleTypeId} options={(types.data ?? []).map((t) => ({ value: t.id, label: t.name }))} />
        </Flex>
        {lower && <Typography.Text type="secondary">For claims, a lower rate ranks higher.</Typography.Text>}
      </Card>
      {ranking.isError && <Alert type="error" showIcon title={ranking.error.message} />}
      <Card styles={{ body: { padding: 0 } }}>
        <Table<RankedTransporterDto>
          rowKey="transporterId"
          loading={ranking.isLoading}
          dataSource={ranking.data?.rows ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'No KPIs stored for this period yet. They build up as loads are delivered.' }}
          columns={[
            { title: '#', dataIndex: 'rank', width: 60, render: (r: number | null) => (r === null ? <Tag>—</Tag> : <Typography.Text strong>{r}</Typography.Text>) },
            { title: 'Transporter', key: 't', render: (_, r) => <div><Link to={`/transporters/${r.transporterId}`}>{r.transporterName}</Link> <Tag variant="filled">{r.transporterCode}</Tag><br /><Typography.Text type="secondary">{r.region ?? ''}</Typography.Text></div> },
            { title: metricOptions.find((m) => m.value === metric)?.label ?? 'Value', dataIndex: 'metricValue', align: 'right', render: (v: number | null, r) => (v === null ? <Tooltip title={r.note}><Tag>Not ranked</Tag></Tooltip> : metric === 'OverallScore' ? v : `${v}%`) },
            { title: 'Overall score', dataIndex: 'overallScore', align: 'right', render: (v: number | null) => v ?? '—' },
            ...scoredKpis.map((k) => ({
              title: kpiLabels[k], key: k, align: 'right' as const, responsive: ['xl' as const],
              render: (_: unknown, r: RankedTransporterDto) => { const x = r.kpis.find((kpi) => kpi.kpi === k); return x?.value == null ? '—' : `${x.value}%` },
            })),
          ]}
        />
      </Card>
    </>
  )
}
