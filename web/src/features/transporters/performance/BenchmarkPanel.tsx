import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Table, Tag, Typography } from 'antd'
import { Can } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { BenchmarkRowDto, KpiType } from '@/lib/api/types'
import { kpiLabels } from './constants'

const pct = (v: number | null) => (v === null ? '—' : `${v}%`)

function Gap({ value }: { value: number | null }) {
  if (value === null) return <>—</>
  return <Tag color={value >= 0 ? 'green' : 'red'}>{value > 0 ? '+' : ''}{value}</Tag>
}

/** How this transporter compares with the best, the lane average and its region. Negative gaps always mean behind, including for claims. */
function Benchmark({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const benchmark = useQuery({ queryKey: queryKeys.performance.benchmark(transporterId, from, to), queryFn: () => performanceApi.benchmark(transporterId, from, to) })
  return (
    <Card title="Benchmark" loading={benchmark.isLoading}>
      {benchmark.isError && <Alert type="error" showIcon title={benchmark.error.message} />}
      {benchmark.data && (
        <>
          <Typography.Paragraph type="secondary">Compared within: {benchmark.data.scopeLabel}. Only transporters with enough loads are compared.</Typography.Paragraph>
          <Table<BenchmarkRowDto>
            size="small"
            rowKey="kpi"
            pagination={false}
            dataSource={benchmark.data.rows}
            scroll={{ x: 'max-content' }}
            columns={[
              { title: 'KPI', dataIndex: 'kpi', render: (k: KpiType) => kpiLabels[k] },
              { title: 'This transporter', dataIndex: 'transporter', align: 'right', render: pct },
              { title: 'Lane average', dataIndex: 'laneAverage', align: 'right', render: pct },
              { title: 'Region average', dataIndex: 'regionAverage', align: 'right', render: pct },
              { title: 'Same services', dataIndex: 'modeAverage', align: 'right', render: pct },
              { title: 'Best', dataIndex: 'topPerformer', align: 'right', render: pct },
              { title: 'Gap to best', dataIndex: 'gapToTop', align: 'right', render: (v: number | null) => <Gap value={v} /> },
              { title: 'Gap to lane', dataIndex: 'gapToLaneAverage', align: 'right', render: (v: number | null) => <Gap value={v} /> },
            ]}
          />
        </>
      )}
    </Card>
  )
}

export function BenchmarkPanel(props: { transporterId: string; from: string; to: string }) {
  // Comparing transporters is for staff; a vendor sees only its own figures.
  return <Can permission="transporters.performance.read"><Benchmark {...props} /></Can>
}
