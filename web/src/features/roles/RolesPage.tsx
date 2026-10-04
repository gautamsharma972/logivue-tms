import { EyeOutlined, EditOutlined, LockOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { rolesApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { RoleDto } from '@/lib/api/types'
import { RoleFormDrawer } from './RoleFormDrawer'

export function RolesPage() {
  const { can } = useAuth()
  const [drawer, setDrawer] = useState<{ open: boolean; role: RoleDto | null }>({ open: false, role: null })
  const roles = useQuery({ queryKey: queryKeys.roles.all, queryFn: rolesApi.list })

  const columns: TableColumnsType<RoleDto> = [
    {
      title: 'Role',
      key: 'name',
      render: (_, r) => (
        <div>
          <Typography.Text strong>{r.name}</Typography.Text>
          {r.isSystem && (
            <Tag icon={<LockOutlined />} variant="filled" style={{ marginInlineStart: 8 }}>
              System
            </Tag>
          )}
          {r.audience === 'External' && (
            <Tag color="purple" variant="filled" style={{ marginInlineStart: 8 }}>
              External
            </Tag>
          )}
          {r.description && (
            <>
              <br />
              <Typography.Text type="secondary">{r.description}</Typography.Text>
            </>
          )}
        </div>
      ),
    },
    { title: 'Permissions', dataIndex: 'permissions', width: 130, render: (p: string[]) => p.length },
    { title: 'Users', dataIndex: 'userCount', width: 100 },
    {
      title: '',
      key: 'actions',
      width: 80,
      render: (_, r) => {
        const editable = can('roles.manage') && !r.isSystem
        return (
          <Button
            type="text"
            icon={editable ? <EditOutlined /> : <EyeOutlined />}
            aria-label={`${editable ? 'Edit' : 'View'} ${r.name}`}
            onClick={() => setDrawer({ open: true, role: r })}
          />
        )
      },
    },
  ]

  return (
    <>
      <PageHeader
        title="Roles & permissions"
        description="A role is a named set of permissions. Assign roles to users to control what they can see and do."
        actions={
          <Can permission="roles.manage">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setDrawer({ open: true, role: null })}>
              New role
            </Button>
          </Can>
        }
      />
      {roles.isError && <Alert type="error" showIcon title={roles.error.message} style={{ marginBottom: 16 }} />}
      <Card styles={{ body: { padding: 0 } }}>
        <Table<RoleDto> rowKey="id" columns={columns} dataSource={roles.data} loading={roles.isLoading} pagination={false} />
      </Card>
      <RoleFormDrawer open={drawer.open} role={drawer.role} onClose={() => setDrawer((d) => ({ ...d, open: false }))} />
    </>
  )
}
