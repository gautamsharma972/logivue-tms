import { useQuery } from '@tanstack/react-query'
import { Alert, Card, DatePicker, Flex, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { auditApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AuditAction, AuditLogDto, ListAuditLogsParams } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'

const actionColor: Record<AuditAction, string> = { Created: 'green', Updated: 'blue', Deleted: 'red' }

// Entity names are the EF model names; extend as modules add auditable aggregates.
const entityOptions = ['User', 'Role', 'Tenant', 'user_roles'].map((value) => ({ value, label: value === 'user_roles' ? 'Role assignment' : value }))

function show(value: unknown): string {
  if (value === undefined || value === null) return '—'
  return typeof value === 'object' ? JSON.stringify(value) : String(value)
}

export function ChangeTable({ changes }: { changes: NonNullable<AuditLogDto['changes']> }) {
  const rows = Object.entries(changes).map(([field, change]) => ({ field, ...change }))
  return (
    <Table
      size="small"
      pagination={false}
      rowKey="field"
      dataSource={rows}
      columns={[
        { title: 'Field', dataIndex: 'field', width: 200 },
        { title: 'Before', dataIndex: 'old', render: show },
        { title: 'After', dataIndex: 'new', render: show },
      ]}
    />
  )
}

export function AuditLogPage() {
  const [entityType, setEntityType] = useState<string>()
  const [range, setRange] = useState<[Dayjs | null, Dayjs | null] | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)

  const params: ListAuditLogsParams = {
    entityType,
    from: range?.[0]?.startOf('day').toISOString(),
    to: range?.[1]?.endOf('day').toISOString(),
    page,
    pageSize,
  }
  const logs = useQuery({ queryKey: queryKeys.audit.list(params), queryFn: () => auditApi.list(params), placeholderData: (previous) => previous })

  const columns: TableColumnsType<AuditLogDto> = [
    { title: 'When', dataIndex: 'occurredAt', width: 190, render: formatDateTime },
    { title: 'Who', key: 'who', render: (_, l) => l.userName ?? <Typography.Text type="secondary">System</Typography.Text> },
    { title: 'Action', dataIndex: 'action', width: 110, render: (a: AuditAction) => <Tag color={actionColor[a]}>{a}</Tag> },
    {
      title: 'Record',
      key: 'record',
      render: (_, l) => (
        <>
          {l.entityType} <Typography.Text type="secondary" copyable={{ text: l.entityId }} style={{ fontSize: 12 }}>{l.entityId.slice(0, 8)}…</Typography.Text>
        </>
      ),
    },
    { title: 'IP', dataIndex: 'ipAddress', responsive: ['lg'], render: show },
  ]

  return (
    <>
      <PageHeader title="Audit trail" description="An append-only record of every change to your organisation's data: who changed what, and when." />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Select
            allowClear
            placeholder="All record types"
            style={{ width: 200 }}
            options={entityOptions}
            value={entityType}
            onChange={(v) => {
              setEntityType(v)
              setPage(1)
            }}
          />
          <DatePicker.RangePicker
            onChange={(v) => {
              setRange(v)
              setPage(1)
            }}
          />
        </Flex>
        {logs.isError && <Alert type="error" showIcon title={logs.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<AuditLogDto>
          rowKey="id"
          columns={columns}
          dataSource={logs.data?.items}
          loading={logs.isFetching}
          scroll={{ x: 'max-content' }}
          expandable={{
            rowExpandable: (l) => l.changes !== null && Object.keys(l.changes).length > 0,
            expandedRowRender: (l) => (l.changes ? <ChangeTable changes={l.changes} /> : null),
          }}
          pagination={{
            current: page,
            pageSize,
            total: logs.data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
            onChange: (p, size) => {
              setPage(p)
              setPageSize(size)
            },
          }}
        />
      </Card>
    </>
  )
}
