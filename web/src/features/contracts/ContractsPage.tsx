import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Flex, Input, Segmented, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { contractsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractStatus, ContractSummaryDto, ContractType, ListContractsParams } from '@/lib/api/types'
import { formatInr } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { ContractFormDrawer } from './ContractFormDrawer'
import { ContractStatusTag, ContractTypeTag, expiryColor } from './shared'

const statuses: { value: ContractStatus; label: string }[] = [
  { value: 'Draft', label: 'Draft' },
  { value: 'PendingApproval', label: 'Pending approval' },
  { value: 'Active', label: 'Active' },
  { value: 'Rejected', label: 'Rejected' },
  { value: 'Terminated', label: 'Terminated' },
  { value: 'Superseded', label: 'Superseded' },
  { value: 'Expired', label: 'Expired' },
]

export function ContractsPage() {
  const navigate = useNavigate()
  const [view, setView] = useState<'all' | 'expiring'>('all')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<ContractStatus>()
  const [type, setType] = useState<ContractType>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [creating, setCreating] = useState(false)

  const debounced = useDebouncedValue(search.trim())
  const params: ListContractsParams = { search: debounced || undefined, status, type, page, pageSize }
  const all = useQuery({ queryKey: queryKeys.contracts.list(params), queryFn: () => contractsApi.list(params), placeholderData: (p) => p, enabled: view === 'all' })
  const expiring = useQuery({ queryKey: queryKeys.contracts.expiring(60), queryFn: () => contractsApi.expiring(60, 200), enabled: view === 'expiring' })
  const data = view === 'all' ? all : expiring

  const columns: TableColumnsType<ContractSummaryDto> = [
    {
      title: 'Contract',
      key: 'contract',
      render: (_, c) => (
        <div>
          <Typography.Text strong>{c.reference}</Typography.Text> <ContractTypeTag type={c.type} />
          <br />
          <Typography.Text type="secondary">{c.title}</Typography.Text>
        </div>
      ),
    },
    { title: 'Transporter', dataIndex: 'transporterName', responsive: ['md'] },
    {
      title: 'Validity',
      key: 'validity',
      responsive: ['lg'],
      render: (_, c) => (
        <>
          {c.effectiveFrom} → {c.effectiveTo}
          {c.status === 'Active' && c.daysUntilExpiry !== null && c.daysUntilExpiry <= 60 && (
            <Tag color={expiryColor(c.daysUntilExpiry)} style={{ marginInlineStart: 8 }}>
              {c.daysUntilExpiry < 0 ? `ended ${-c.daysUntilExpiry}d ago` : c.daysUntilExpiry === 0 ? 'ends today' : `${c.daysUntilExpiry}d left`}
            </Tag>
          )}
        </>
      ),
    },
    { title: 'Rates', dataIndex: 'rateCount', width: 80, align: 'right' },
    { title: 'Est. annual spend', dataIndex: 'estimatedAnnualSpend', responsive: ['xl'], align: 'right', render: formatInr },
    { title: 'Status', dataIndex: 'status', render: (s: ContractStatus) => <ContractStatusTag status={s} /> },
  ]

  return (
    <>
      <PageHeader
        title="Freight contracts"
        description="Agreed rates with each transporter: lanes, weight and distance slabs, and diesel clauses."
        actions={
          <Can permission="contracts.manage">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New contract</Button>
          </Can>
        }
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap align="center" style={{ padding: 16 }}>
          <Segmented value={view} onChange={(v) => { setView(v as 'all' | 'expiring'); setPage(1) }} options={[{ value: 'all', label: 'All contracts' }, { value: 'expiring', label: 'Ending within 60 days' }]} />
          {view === 'all' && (
            <>
              <Input allowClear style={{ width: 260, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search number or title" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
              <Select allowClear placeholder="All statuses" style={{ width: 170 }} options={statuses} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
              <Select allowClear placeholder="All types" style={{ width: 150 }} value={type} onChange={(v) => { setType(v); setPage(1) }}
                options={[{ value: 'Ftl', label: 'FTL' }, { value: 'Ptl', label: 'PTL' }, { value: 'Dedicated', label: 'Dedicated' }]} />
            </>
          )}
        </Flex>
        {data.isError && <Alert type="error" showIcon title={data.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<ContractSummaryDto>
          rowKey="id"
          columns={columns}
          dataSource={data.data?.items}
          loading={data.isFetching}
          scroll={{ x: 'max-content' }}
          onRow={(c) => ({ onClick: () => navigate(`/contracts/${c.id}`), style: { cursor: 'pointer' } })}
          locale={{ emptyText: view === 'expiring' ? 'No active contract ends within 60 days' : debounced || status || type ? 'No contracts match these filters' : 'No contracts yet' }}
          pagination={view === 'all' ? {
            current: page, pageSize, total: all.data?.totalCount ?? 0, showSizeChanger: true,
            showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
            onChange: (p, size) => { setPage(p); setPageSize(size) },
          } : false}
        />
      </Card>
      <ContractFormDrawer open={creating} contract={null} onClose={() => setCreating(false)} onCreated={(c) => navigate(`/contracts/${c.summary.id}`)} />
    </>
  )
}
