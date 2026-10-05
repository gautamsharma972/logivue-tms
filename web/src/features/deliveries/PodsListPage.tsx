import { SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Checkbox, Flex, Input, Select, Table, Tabs, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveriesApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListPodsParams, PodSummaryDto, ProofStatus } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { ProofStatusTag, ValidationMark, ocrStatusLabel, proofStatusOptions } from './shared'

function PodTable({ queue }: { queue: boolean }) {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<ProofStatus>()
  const [overdue, setOverdue] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const debounced = useDebouncedValue(search.trim())
  const params: ListPodsParams = { search: debounced || undefined, status: queue ? undefined : status, overdue: overdue || undefined, currentOnly: true, page, pageSize }
  const pods = useQuery({ queryKey: queue ? queryKeys.deliveries.reviewQueue(params) : queryKeys.deliveries.pods(params), queryFn: () => (queue ? deliveriesApi.reviewQueue(params) : deliveriesApi.pods(params)), placeholderData: (p) => p })

  const columns: TableColumnsType<PodSummaryDto> = [
    { title: 'Proof', key: 'p', render: (_, p) => <div><Typography.Text strong>{p.podNumber}</Typography.Text>{p.version > 1 && <Tag style={{ marginLeft: 6 }}>v{p.version}</Tag>}<br /><Typography.Text type="secondary">{p.deliveryNumber}</Typography.Text></div> },
    { title: 'Customer', dataIndex: 'customerName' },
    { title: 'Transporter', dataIndex: 'transporterReference', responsive: ['lg'], render: (v: string | null) => v ?? '—' },
    { title: 'Delivered', dataIndex: 'deliveredAt', responsive: ['md'], render: formatDateTime },
    { title: 'Submitted', dataIndex: 'submittedAt', responsive: ['md'], render: formatDateTime },
    { title: 'Waiting', dataIndex: 'hoursSinceSubmitted', align: 'right', render: (v: number | null) => (v == null ? '—' : `${v} h`) },
    { title: 'Checks', dataIndex: 'validation', responsive: ['lg'], render: (v) => <ValidationMark status={v} /> },
    { title: 'Paper', dataIndex: 'ocr', responsive: ['xl'], render: (v: PodSummaryDto['ocr']) => (v ? ocrStatusLabel[v] : '—') },
    { title: 'Status', dataIndex: 'status', render: (s: ProofStatus) => <ProofStatusTag status={s} /> },
    { title: '', dataIndex: 'hasDiscrepancy', render: (v: boolean) => (v ? <Tag color="orange">Discrepancy</Tag> : null) },
  ]

  return (
    <Card styles={{ body: { padding: 0 } }}>
      <Flex gap={12} wrap align="center" style={{ padding: 16 }}>
        <Input allowClear style={{ width: 260, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search proof, delivery, customer" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
        {!queue && <Select allowClear placeholder="All statuses" style={{ width: 200 }} options={proofStatusOptions.filter((o) => o.value !== 'Pending')} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />}
        <Checkbox checked={overdue} onChange={(e) => { setOverdue(e.target.checked); setPage(1) }}>Waiting longer than the review target</Checkbox>
      </Flex>
      {pods.isError && <Alert type="error" showIcon title={pods.error.message} style={{ margin: '0 16px 16px' }} />}
      <Table<PodSummaryDto>
        rowKey="id" columns={columns} dataSource={pods.data?.items} loading={pods.isFetching} scroll={{ x: 'max-content' }}
        onRow={(p) => ({ onClick: () => navigate(`/delivery/pods/${p.id}`), style: { cursor: 'pointer' } })}
        locale={{ emptyText: queue ? 'Nothing is waiting for review' : 'No proofs match these filters' }}
        pagination={{ current: page, pageSize, total: pods.data?.totalCount ?? 0, showSizeChanger: true, showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`, onChange: (p, size) => { setPage(p); setPageSize(size) } }}
      />
    </Card>
  )
}

export function PodsListPage() {
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const canReview = !isVendor && can('deliveries.pod.review')
  return (
    <>
      <PageHeader title="Proofs of delivery" description="The evidence that each delivery was made, checked and accepted." />
      <Tabs
        defaultActiveKey={canReview ? 'queue' : 'all'}
        items={[
          ...(canReview ? [{ key: 'queue', label: 'Review queue', children: <PodTable queue /> }] : []),
          { key: 'all', label: 'All proofs', children: <PodTable queue={false} /> },
        ]}
      />
    </>
  )
}
