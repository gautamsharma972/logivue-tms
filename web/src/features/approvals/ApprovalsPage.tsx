import { useQuery } from '@tanstack/react-query'
import { Alert, Badge, Button, Card, Flex, Select, Table, Tabs, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { approvalsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ApprovalStatus, ListRequestsParams, RequestScope, RequestSummaryDto } from '@/lib/api/types'
import { formatDateTime, formatInr } from '@/lib/format'
import { DelegationsPanel } from './DelegationsPanel'
import { RequestDrawer } from './RequestDrawer'
import { StatusTag } from './StatusTag'

const statusOptions: { value: ApprovalStatus; label: string }[] = [
  { value: 'Pending', label: 'Pending' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Rejected', label: 'Rejected' },
  { value: 'Cancelled', label: 'Cancelled' },
]

function RequestsTable({ scope, onOpen }: { scope: RequestScope; onOpen: (id: string) => void }) {
  const [status, setStatus] = useState<ApprovalStatus>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const params: ListRequestsParams = { scope, status: scope === 'inbox' ? undefined : status, page, pageSize }
  const requests = useQuery({ queryKey: queryKeys.approvals.requests(params), queryFn: () => approvalsApi.requests(params), placeholderData: (p) => p })

  const columns: TableColumnsType<RequestSummaryDto> = [
    {
      title: 'Request',
      key: 'title',
      render: (_, r) => (
        <div>
          <Typography.Text strong>{r.title}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{r.documentTypeName}</Typography.Text>
        </div>
      ),
    },
    { title: 'Amount', dataIndex: 'amount', align: 'right', render: formatInr },
    { title: 'Requested by', dataIndex: 'requesterName', responsive: ['md'] },
    {
      title: 'Status',
      key: 'status',
      render: (_, r) => (
        <div>
          <StatusTag status={r.status} />
          {r.currentStepName && (
            <>
              <br />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                Waiting: {r.currentStepName}
              </Typography.Text>
            </>
          )}
        </div>
      ),
    },
    { title: 'Submitted', dataIndex: 'createdAt', responsive: ['lg'], render: formatDateTime },
    {
      title: '',
      key: 'open',
      width: 110,
      render: (_, r) => (
        <Button type={r.canDecide ? 'primary' : 'default'} size="small" onClick={() => onOpen(r.id)}>
          {r.canDecide ? 'Review' : 'View'}
        </Button>
      ),
    },
  ]

  const emptyText =
    scope === 'inbox' ? 'Nothing is waiting for your approval' : scope === 'mine' ? "You haven't submitted anything for approval" : 'No requests'

  return (
    <Card styles={{ body: { padding: 0 } }}>
      {scope !== 'inbox' && (
        <Flex style={{ padding: 16 }}>
          <Select allowClear placeholder="All statuses" style={{ width: 180 }} options={statusOptions} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
        </Flex>
      )}
      {requests.isError && <Alert type="error" showIcon title={requests.error.message} style={{ margin: 16 }} />}
      <Table<RequestSummaryDto>
        rowKey="id"
        columns={columns}
        dataSource={requests.data?.items}
        loading={requests.isFetching}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText }}
        pagination={{
          current: page,
          pageSize,
          total: requests.data?.totalCount ?? 0,
          showSizeChanger: true,
          showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
          onChange: (p, size) => { setPage(p); setPageSize(size) },
        }}
      />
    </Card>
  )
}

export function ApprovalsPage() {
  const { can } = useAuth()
  const [openId, setOpenId] = useState<string | null>(null)
  const inboxCount = useQuery({
    queryKey: queryKeys.approvals.requests({ scope: 'inbox', pageSize: 1 }),
    queryFn: () => approvalsApi.requests({ scope: 'inbox', pageSize: 1 }),
  })

  return (
    <>
      <PageHeader title="Approvals" description="Review what is waiting for you, track what you submitted, and cover for each other while away." />
      <Tabs
        defaultActiveKey="inbox"
        items={[
          {
            key: 'inbox',
            label: <Badge count={inboxCount.data?.totalCount ?? 0} offset={[10, 0]} size="small">Inbox</Badge>,
            children: <RequestsTable scope="inbox" onOpen={setOpenId} />,
          },
          { key: 'mine', label: 'My requests', children: <RequestsTable scope="mine" onOpen={setOpenId} /> },
          ...(can('approvals.read.all') ? [{ key: 'all', label: 'All requests', children: <RequestsTable scope="all" onOpen={setOpenId} /> }] : []),
          { key: 'delegations', label: 'Delegations', children: <DelegationsPanel /> },
        ]}
      />
      <RequestDrawer requestId={openId} onClose={() => setOpenId(null)} />
    </>
  )
}
