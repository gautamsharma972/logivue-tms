import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Flex, Input, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { transportersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListTransportersParams, TransporterStatus, TransporterSummaryDto } from '@/lib/api/types'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { TransporterFormDrawer } from './TransporterFormDrawer'
import { TransporterStatusTag } from './tags'

const statuses: { value: TransporterStatus; label: string }[] = [
  { value: 'Draft', label: 'Draft' },
  { value: 'PendingApproval', label: 'Pending approval' },
  { value: 'Active', label: 'Active' },
  { value: 'Rejected', label: 'Rejected' },
  { value: 'Suspended', label: 'Suspended' },
]

export function TransportersPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<TransporterStatus>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [creating, setCreating] = useState(false)

  const debounced = useDebouncedValue(search.trim())
  const params: ListTransportersParams = { search: debounced || undefined, status, page, pageSize }
  const transporters = useQuery({ queryKey: queryKeys.transporters.list(params), queryFn: () => transportersApi.list(params), placeholderData: (p) => p })

  const columns: TableColumnsType<TransporterSummaryDto> = [
    { title: 'Code', dataIndex: 'code', width: 110 },
    {
      title: 'Transporter',
      key: 'name',
      render: (_, t) => (
        <div>
          <Typography.Text strong>{t.legalName}</Typography.Text>
          {t.tradeName && <><br /><Typography.Text type="secondary">{t.tradeName}</Typography.Text></>}
        </div>
      ),
    },
    { title: 'Location', key: 'location', responsive: ['md'], render: (_, t) => `${t.city}, ${t.state}` },
    { title: 'Services', dataIndex: 'serviceModes', responsive: ['lg'], render: (modes: string[]) => modes.map((m) => <Tag key={m}>{m.toUpperCase()}</Tag>) },
    { title: 'Mobile', dataIndex: 'phone', responsive: ['lg'] },
    { title: 'Status', dataIndex: 'status', render: (s: TransporterStatus) => <TransporterStatusTag status={s} /> },
  ]

  return (
    <>
      <PageHeader
        title="Transporters"
        description="Freight vendors: onboarding, fleet, drivers and compliance papers."
        actions={
          <Can permission="transporters.manage">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New transporter</Button>
          </Can>
        }
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Input
            allowClear
            style={{ width: 320, maxWidth: '100%' }}
            prefix={<SearchOutlined />}
            placeholder="Search name, code, PAN or GSTIN"
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1) }}
          />
          <Select allowClear placeholder="All statuses" style={{ width: 190 }} options={statuses} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
        </Flex>
        {transporters.isError && <Alert type="error" showIcon title={transporters.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<TransporterSummaryDto>
          rowKey="id"
          columns={columns}
          dataSource={transporters.data?.items}
          loading={transporters.isFetching}
          scroll={{ x: 'max-content' }}
          onRow={(t) => ({ onClick: () => navigate(`/transporters/${t.id}`), style: { cursor: 'pointer' } })}
          pagination={{
            current: page,
            pageSize,
            total: transporters.data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
            onChange: (p, size) => { setPage(p); setPageSize(size) },
          }}
          locale={{ emptyText: debounced || status ? 'No transporters match these filters' : 'No transporters yet' }}
        />
      </Card>
      <TransporterFormDrawer open={creating} transporter={null} onClose={() => setCreating(false)} onCreated={(t) => navigate(`/transporters/${t.id}`)} />
    </>
  )
}
