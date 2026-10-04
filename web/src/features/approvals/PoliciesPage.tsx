import { EditOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { approvalsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { PolicyDto } from '@/lib/api/types'
import { formatInr } from '@/lib/format'
import { PolicyDrawer } from './PolicyDrawer'

function summary(policy: PolicyDto) {
  if (!policy.isConfigured) return <Typography.Text type="secondary">No approval chain — submissions are refused until one is set up</Typography.Text>
  return policy.steps.map((s, i) => (
    <Tag key={i} style={{ marginBottom: 4 }}>
      {i + 1}. {s.name}
      {s.minAmount !== null && <> · from {formatInr(s.minAmount)}</>}
    </Tag>
  ))
}

export function PoliciesPage() {
  const [editing, setEditing] = useState<PolicyDto | null>(null)
  const policies = useQuery({ queryKey: queryKeys.approvals.policies, queryFn: approvalsApi.policies })

  const columns: TableColumnsType<PolicyDto> = [
    { title: 'Document', dataIndex: 'documentTypeName', render: (n: string) => <Typography.Text strong>{n}</Typography.Text> },
    {
      title: 'Status',
      key: 'status',
      width: 150,
      render: (_, p) => (!p.isConfigured ? <Tag>Not configured</Tag> : p.isActive ? <Tag color="green">Active</Tag> : <Tag color="gold">Inactive</Tag>),
    },
    { title: 'Approval chain', key: 'steps', render: (_, p) => summary(p) },
    {
      title: '',
      key: 'edit',
      width: 120,
      render: (_, p) => (
        <Button icon={<EditOutlined />} onClick={() => setEditing(p)}>
          {p.isConfigured ? 'Edit' : 'Set up'}
        </Button>
      ),
    },
  ]

  return (
    <>
      <PageHeader title="Approval policies" description="Decide who must approve each kind of document, in what order, and above which amounts." />
      {policies.isError && <Alert type="error" showIcon title={policies.error.message} style={{ marginBottom: 16 }} />}
      <Card styles={{ body: { padding: 0 } }}>
        <Table<PolicyDto> rowKey="documentType" columns={columns} dataSource={policies.data} loading={policies.isLoading} pagination={false} scroll={{ x: 'max-content' }} />
      </Card>
      <PolicyDrawer open={editing !== null} policy={editing} onClose={() => setEditing(null)} />
    </>
  )
}
