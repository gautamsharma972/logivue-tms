import { EditOutlined, PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Badge, Button, Card, Flex, Input, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { usersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListUsersParams, UserDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { UserFormDrawer } from './UserFormDrawer'

type StatusFilter = 'all' | 'active' | 'inactive'

export function UsersPage() {
  const { can } = useAuth()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<StatusFilter>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [drawer, setDrawer] = useState<{ open: boolean; user: UserDto | null }>({ open: false, user: null })

  const debouncedSearch = useDebouncedValue(search.trim())
  const params: ListUsersParams = {
    search: debouncedSearch || undefined,
    isActive: status === 'all' ? undefined : status === 'active',
    page,
    pageSize,
  }
  const users = useQuery({ queryKey: queryKeys.users.list(params), queryFn: () => usersApi.list(params), placeholderData: (previous) => previous })

  const columns: TableColumnsType<UserDto> = [
    {
      title: 'User',
      key: 'user',
      render: (_, u) => (
        <div>
          <Typography.Text strong>{u.fullName}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{u.email}</Typography.Text>
        </div>
      ),
    },
    {
      title: 'Roles',
      key: 'roles',
      render: (_, u) => (u.roles.length ? u.roles.map((r) => <Tag key={r.id}>{r.name}</Tag>) : <Typography.Text type="secondary">None</Typography.Text>),
    },
    { title: 'Type', dataIndex: 'type', responsive: ['md'] },
    {
      title: 'Status',
      dataIndex: 'isActive',
      render: (active: boolean) => <Badge status={active ? 'success' : 'default'} text={active ? 'Active' : 'Inactive'} />,
    },
    { title: 'Last sign-in', dataIndex: 'lastLoginAt', responsive: ['lg'], render: (v: string | null) => formatDateTime(v) },
    {
      title: '',
      key: 'actions',
      width: 80,
      render: (_, u) =>
        can('users.manage') && (
          <Button type="text" icon={<EditOutlined />} aria-label={`Edit ${u.fullName}`} onClick={() => setDrawer({ open: true, user: u })} />
        ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Users"
        description="People who can sign in to your organisation, and the roles that decide what they can do."
        actions={
          <Can permission="users.manage">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true, user: null })}>
              New user
            </Button>
          </Can>
        }
      />

      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Input
            allowClear
            style={{ width: 300, maxWidth: '100%' }}
            prefix={<SearchOutlined />}
            placeholder="Search name or email"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
          <Select<StatusFilter>
            style={{ width: 150 }}
            value={status}
            onChange={(v) => {
              setStatus(v)
              setPage(1)
            }}
            options={[
              { value: 'all', label: 'All statuses' },
              { value: 'active', label: 'Active' },
              { value: 'inactive', label: 'Inactive' },
            ]}
          />
        </Flex>

        {users.isError && <Alert type="error" showIcon title={users.error.message} style={{ margin: '0 16px 16px' }} />}

        <Table<UserDto>
          rowKey="id"
          columns={columns}
          dataSource={users.data?.items}
          loading={users.isFetching}
          scroll={{ x: 'max-content' }}
          pagination={{
            current: page,
            pageSize,
            total: users.data?.totalCount ?? 0,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}–${range[1]} of ${total}`,
            onChange: (p, size) => {
              setPage(p)
              setPageSize(size)
            },
          }}
          locale={{ emptyText: debouncedSearch || status !== 'all' ? 'No users match these filters' : 'No users yet' }}
        />
      </Card>

      <UserFormDrawer open={drawer.open} user={drawer.user} onClose={() => setDrawer((d) => ({ ...d, open: false }))} />
    </>
  )
}
