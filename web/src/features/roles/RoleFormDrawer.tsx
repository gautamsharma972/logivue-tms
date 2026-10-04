import { LockOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, Collapse, Drawer, Flex, Form, Input, Select, Skeleton, Tag, Tooltip, Typography } from 'antd'
import { useEffect, useMemo } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { rolesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { PermissionDefinition, RoleAudience, RoleDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

interface FormValues {
  name: string
  description?: string
  permissions: string[]
  audience: RoleAudience
}

interface Props {
  open: boolean
  role: RoleDto | null
  onClose: () => void
}

export function RoleFormDrawer({ open, role, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const { can } = useAuth()
  const editing = role !== null
  const readOnly = role?.isSystem === true || !can('roles.manage')

  const audience = Form.useWatch('audience', form) as RoleAudience | undefined
  const catalog = useQuery({ queryKey: queryKeys.roles.permissions, queryFn: rolesApi.permissions, enabled: open })
  const grouped = useMemo(() => {
    const groups = new Map<string, PermissionDefinition[]>()
    catalog.data?.forEach((p) => groups.set(p.module, [...(groups.get(p.module) ?? []), p]))
    return [...groups.entries()]
  }, [catalog.data])

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      role ? { name: role.name, description: role.description ?? '', permissions: role.permissions, audience: role.audience } : { permissions: [], audience: 'Internal' },
    )
  }, [open, role, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = { name: v.name.trim(), description: v.description?.trim() || null, permissions: v.permissions, version: role?.version ?? null, audience: v.audience }
      return role ? rolesApi.update(role.id, body) : rolesApi.create(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.roles.all })
      void message.success(editing ? 'Role updated' : 'Role created')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      if (error.code === 'concurrency.conflict') {
        void queryClient.invalidateQueries({ queryKey: queryKeys.roles.all })
        onClose()
      }
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={560}
      destroyOnHidden
      title={
        <Flex align="center" gap={8}>
          {editing ? (readOnly ? 'Role details' : 'Edit role') : 'New role'}
          {role?.isSystem && (
            <Tag icon={<LockOutlined />} variant="filled">
              System role
            </Tag>
          )}
        </Flex>
      }
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>
            {readOnly ? 'Close' : 'Cancel'}
          </Button>
          {!readOnly && (
            <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
              {editing ? 'Save changes' : 'Create role'}
            </Button>
          )}
        </Flex>
      }
    >
      {role?.isSystem && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          title="This role is managed by the platform and always includes every permission, including those added by future modules."
        />
      )}
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" disabled={readOnly || save.isPending} onFinish={(v) => save.mutate(v)}>
        <Form.Item label="Name" name="name" rules={[{ required: true, message: 'Enter a role name' }, { max: 100 }]}>
          <Input autoFocus />
        </Form.Item>
        <Form.Item label="Description" name="description" rules={[{ max: 500 }]}>
          <Input.TextArea rows={2} />
        </Form.Item>

        <Form.Item
          label="Who is this role for?"
          name="audience"
          extra="Staff roles go to your own employees. External roles go to vendor-portal and driver accounts and can only hold the few permissions marked for them. This cannot be changed later."
        >
          <Select
            disabled={editing}
            onChange={() => form.setFieldValue('permissions', [])}
            options={[{ value: 'Internal', label: 'Staff (internal)' }, { value: 'External', label: 'Vendors & drivers (external)' }]}
          />
        </Form.Item>

        <Form.Item label="Permissions" name="permissions" extra="You can only grant permissions that you hold yourself.">
          {catalog.isLoading ? (
            <Skeleton active />
          ) : (
            <Checkbox.Group style={{ width: '100%' }}>
              <Collapse
                ghost
                defaultActiveKey={grouped.map(([module]) => module)}
                items={grouped.map(([module, permissions]) => ({
                  key: module,
                  label: <Typography.Text strong>{module}</Typography.Text>,
                  children: (
                    <Flex vertical gap={8}>
                      {permissions.map((p) => {
                        const external = audience === 'External'
                        const allowed = can(p.code) && (!external || p.externalAllowed)
                        return (
                          <Tooltip key={p.code} title={allowed ? undefined : external && !p.externalAllowed ? 'Staff-only permission: not available to external roles' : "You don't hold this permission, so you can't grant it"}>
                            <Checkbox value={p.code} disabled={!allowed}>
                              {p.description} <Typography.Text type="secondary" code>{p.code}</Typography.Text>
                            </Checkbox>
                          </Tooltip>
                        )
                      })}
                    </Flex>
                  ),
                }))}
              />
            </Checkbox.Group>
          )}
        </Form.Item>
      </Form>
    </Drawer>
  )
}
