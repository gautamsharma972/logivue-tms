import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, Drawer, Flex, Form, Input, Select, Switch } from 'antd'
import { useEffect, useState } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { toApiError } from '@/lib/api/errors'
import { rolesApi, transportersApi, usersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { UserDto, UserType } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

interface FormValues {
  fullName: string
  email: string
  password?: string
  type: UserType
  roleIds: string[]
  isActive: boolean
  transporterId?: string
  requirePasswordChange?: boolean
}

interface Props {
  open: boolean
  /** The user being edited, or null to create a new one. */
  user: UserDto | null
  onClose: () => void
}

export function UserFormDrawer({ open, user, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const { can } = useAuth()
  const editing = user !== null
  const type = Form.useWatch('type', form) as UserType | undefined
  const [companySearch, setCompanySearch] = useState('')
  const companies = useQuery({
    queryKey: queryKeys.transporters.lookup(companySearch),
    queryFn: () => transportersApi.lookup(companySearch || undefined),
    enabled: open && type === 'Transporter' && !editing,
  })

  const roles = useQuery({ queryKey: queryKeys.roles.all, queryFn: rolesApi.list, enabled: open && can('roles.read') })

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      user
        ? { fullName: user.fullName, email: user.email, type: user.type, roleIds: user.roles.map((r) => r.id), isActive: user.isActive, transporterId: user.transporterId ?? undefined }
        : { type: 'Internal', roleIds: [], isActive: true, requirePasswordChange: true },
    )
  }, [open, user, form])

  const save = useMutation({
    mutationFn: (values: FormValues) =>
      user
        ? usersApi.update(user.id, { fullName: values.fullName, isActive: values.isActive, roleIds: values.roleIds, version: user.version })
        : usersApi.create({ email: values.email, fullName: values.fullName, password: values.password ?? '', type: values.type, roleIds: values.roleIds, transporterId: values.type === 'Transporter' ? values.transporterId : null, requirePasswordChange: values.requirePasswordChange ?? false }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.users.all })
      void message.success(editing ? 'User updated' : 'User created')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      if (error.code === 'concurrency.conflict') {
        void queryClient.invalidateQueries({ queryKey: queryKeys.users.all })
        onClose()
      }
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={editing ? 'Edit user' : 'New user'}
      size={520}
      destroyOnHidden
      maskClosable={!save.isPending}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>
            Cancel
          </Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
            {editing ? 'Save changes' : 'Create user'}
          </Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)} disabled={save.isPending}>
        <Form.Item label="Full name" name="fullName" rules={[{ required: true, message: 'Enter the full name' }, { max: 200 }]}>
          <Input autoFocus autoComplete="off" />
        </Form.Item>
        <Form.Item
          label="Email"
          name="email"
          rules={[{ required: true, message: 'Enter an email' }, { type: 'email', message: 'Enter a valid email' }]}
        >
          <Input autoComplete="off" disabled={editing} />
        </Form.Item>
        {!editing && (
          <Form.Item
            label="Initial password"
            name="password"
            extra="At least 12 characters with upper-case, lower-case and a digit. Share it securely; the user should change it after first sign-in."
            rules={[
              { required: true, message: 'Set an initial password' },
              { min: 12, message: 'At least 12 characters' },
              { pattern: /[A-Z]/, message: 'Include an upper-case letter' },
              { pattern: /[a-z]/, message: 'Include a lower-case letter' },
              { pattern: /[0-9]/, message: 'Include a digit' },
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
        )}
        {!editing && (
          <Form.Item name="requirePasswordChange" valuePropName="checked" extra="Recommended: the user must choose their own password at first sign-in.">
            <Checkbox>Require a password change at first sign-in</Checkbox>
          </Form.Item>
        )}
        <Form.Item label="User type" name="type">
          <Select
            onChange={() => form.setFieldValue('roleIds', [])}
            disabled={editing}
            options={[
              { value: 'Internal', label: 'Internal (employee)' },
              { value: 'Transporter', label: 'Transporter (vendor portal)' },
              { value: 'Driver', label: 'Driver (mobile app)' },
            ]}
          />
        </Form.Item>

        {type === 'Transporter' && !editing && (
          <Form.Item label="Transporter company" name="transporterId" rules={[{ required: true, message: 'Choose the transporter this user belongs to' }]} extra="This user will only ever see and manage this company’s data.">
            <Select
              showSearch
              filterOption={false}
              onSearch={setCompanySearch}
              loading={companies.isFetching}
              placeholder="Search active transporters"
              options={companies.data?.map((c) => ({ value: c.id, label: `${c.legalName} (${c.code})` }))}
            />
          </Form.Item>
        )}

        {can('roles.read') ? (
          <Form.Item label="Roles" name="roleIds" extra={type && type !== 'Internal' ? 'Vendor and driver accounts can only hold external roles.' : undefined}>
            <Select
              mode="multiple"
              loading={roles.isLoading}
              placeholder="Select roles"
              optionFilterProp="label"
              options={roles.data?.filter((r) => r.audience === (type === 'Internal' || type === undefined ? 'Internal' : 'External')).map((r) => ({ value: r.id, label: r.name }))}
            />
          </Form.Item>
        ) : (
          <Alert type="info" showIcon title="You don't have permission to view roles, so role assignment is unavailable." style={{ marginBottom: 16 }} />
        )}

        {editing && (
          <Form.Item label="Active" name="isActive" valuePropName="checked" extra="Inactive users cannot sign in.">
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Drawer>
  )
}
