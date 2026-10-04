import { useQuery } from '@tanstack/react-query'
import { Alert, Table, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { planningApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { RunSummaryDto } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { PlanStatusTag, SolverStatusTag, percent } from './shared'

export function PlansTab() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const runs = useQuery({ queryKey: queryKeys.planning.runs(page), queryFn: () => planningApi.runs({ page, pageSize: 15 }), placeholderData: (p) => p })

  const columns: TableColumnsType<RunSummaryDto> = [
    { title: 'Plan', key: 'plan', render: (_, r) => <><Typography.Text strong>{r.number}</Typography.Text> <Typography.Text type="secondary">v{r.planVersion}</Typography.Text></> },
    { title: 'Date', dataIndex: 'planningDate' },
    { title: 'Orders', key: 'orders', align: 'right', render: (_, r) => `${r.summary.ordersPlanned}${r.summary.ordersUnplanned ? ` (+${r.summary.ordersUnplanned} unplanned)` : ''}` },
    { title: 'Vehicles', key: 'v', align: 'right', responsive: ['md'], render: (_, r) => r.summary.vehiclesUsed },
    { title: 'Freight', key: 'c', align: 'right', render: (_, r) => formatInrExact(r.summary.totalCost) },
    { title: 'Weight fill', key: 'w', align: 'right', responsive: ['lg'], render: (_, r) => percent(r.summary.averageWeightUtilisation) },
    { title: 'Created', dataIndex: 'createdAt', responsive: ['lg'], render: formatDateTime },
    { title: 'Solver', dataIndex: 'solverStatus', responsive: ['xl'], render: (s: RunSummaryDto['solverStatus']) => <SolverStatusTag status={s} /> },
    { title: 'Status', dataIndex: 'status', render: (s: RunSummaryDto['status']) => <PlanStatusTag status={s} /> },
  ]

  return (
    <>
      {runs.isError && <Alert type="error" showIcon title={runs.error.message} style={{ marginBottom: 16 }} />}
      <Table<RunSummaryDto>
        rowKey="id"
        columns={columns}
        dataSource={runs.data?.items}
        loading={runs.isFetching}
        scroll={{ x: 'max-content' }}
        onRow={(r) => ({ onClick: () => navigate(`/planning/runs/${r.id}`), style: { cursor: 'pointer' } })}
        locale={{ emptyText: 'No plans yet. Select orders in the workbench and run planning.' }}
        pagination={{ current: page, pageSize: 15, total: runs.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
      />
    </>
  )
}
