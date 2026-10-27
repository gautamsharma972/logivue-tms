import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, Flex, Form, Input, InputNumber, Modal, Popconfirm, Table, Tag, Typography, message } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { platformApi, type AdminPasswordReset, type CreateTenantInput, type PlatformTenant, type ProvisionedTenant, type UpdateTenantInput } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { applyFieldErrors } from '@/lib/formErrors'

/** The operator allocates business modules to each customer organisation. Core administration (users, roles, audit, approvals) is always included. */
export function TenantsPage() {
  const qc = useQueryClient()
  const tenants = useQuery({ queryKey: ['platform', 'tenants'], queryFn: platformApi.tenants })
  const modules = useQuery({ queryKey: ['platform', 'modules'], queryFn: platformApi.modules, staleTime: Infinity })
  const [editing, setEditing] = useState<PlatformTenant | null>(null)
  const [selected, setSelected] = useState<string[]>([])
  const [editForm] = Form.useForm<{ name: string; maxUsers: number | null; maxLocations: number | null }>()
  const [adding, setAdding] = useState(false)
  const [created, setCreated] = useState<ProvisionedTenant | null>(null)
  const [form] = Form.useForm<Omit<CreateTenantInput, 'modules'>>()
  const [newModules, setNewModules] = useState<string[]>([])
  const add = useMutation({
    mutationFn: (v: Omit<CreateTenantInput, 'modules'>) => platformApi.createTenant({ ...v, modules: newModules, maxUsers: v.maxUsers ?? null, maxLocations: v.maxLocations ?? null }),
    onSuccess: (p) => { setAdding(false); setCreated(p); void qc.invalidateQueries({ queryKey: ['platform', 'tenants'] }) },
    onError: (e) => { const err = toApiError(e); if (!applyFieldErrors(form, err)) message.error(err.message) },
  })
  const [reset, setReset] = useState<AdminPasswordReset | null>(null)
  const resetAdmin = useMutation({
    mutationFn: (t: PlatformTenant) => platformApi.resetAdminPassword(t.id),
    onSuccess: (r) => setReset(r),
    onError: (e) => message.error(toApiError(e).message),
  })
  const nameOf = (key: string) => modules.data?.find((m) => m.key === key)?.name ?? key
  const save = useMutation({
    mutationFn: (v: Pick<UpdateTenantInput, 'name' | 'maxUsers' | 'maxLocations'>) => platformApi.updateTenant(editing!.id, { ...v, modules: selected }),
    onSuccess: () => { message.success('Organisation updated'); setEditing(null); void qc.invalidateQueries({ queryKey: ['platform', 'tenants'] }) },
    onError: (e) => message.error(toApiError(e).message),
  })
  const edit = (t: PlatformTenant) => { setEditing(t); setSelected(t.modules); editForm.setFieldsValue({ name: t.name, maxUsers: t.maxUsers, maxLocations: t.maxLocations }) }
  return (
    <>
      <PageHeader title="Organisations & modules" description="Choose which parts of the TMS each customer organisation has. A module that is switched off disappears from their menu, their API and their reports."
        actions={<Button type="primary" onClick={() => { form.resetFields(); setNewModules([]); setAdding(true) }}>Add organisation</Button>} />
      <Card>
        <Table size="small" rowKey="id" loading={tenants.isLoading} dataSource={tenants.data ?? []} pagination={false}
          columns={[
            { title: 'Organisation', render: (_, t: PlatformTenant) => <div><Typography.Text strong>{t.name}</Typography.Text><br /><Typography.Text type="secondary">{t.code}</Typography.Text></div> },
            { title: 'First administrator', dataIndex: 'firstAdminEmail', render: (v: string | null) => v ?? '—' },
            { title: 'Users', width: 110, render: (_, t: PlatformTenant) => (t.maxUsers == null ? t.activeUsers : <span>{t.activeUsers} / {t.maxUsers}</span>) },
            { title: 'CFAs & distributors', width: 130, render: (_, t: PlatformTenant) => (t.isPlatform ? '—' : t.maxLocations == null ? 'No limit' : `Up to ${t.maxLocations}`) },
            { title: 'Modules', render: (_, t: PlatformTenant) => (t.isPlatform ? <Tag>Platform operator</Tag> : t.modules.length === 0 ? <Typography.Text type="secondary">None (administration only)</Typography.Text> : <Flex wrap gap={4}>{t.modules.map((m) => <Tag key={m} color="blue">{nameOf(m)}</Tag>)}</Flex>) },
            { title: '', width: 260, render: (_, t: PlatformTenant) => (
              <Flex gap={8} wrap>
                {!t.isPlatform && <Button size="small" onClick={() => edit(t)}>Edit</Button>}
                {t.firstAdminEmail && (
                  <Popconfirm title="Reset the administrator's password?" description={`${t.firstAdminEmail} gets a single-use link to choose a new password. It expires in 10 minutes. Their current password keeps working until they use it.`} okText="Send link" onConfirm={() => resetAdmin.mutate(t)}>
                    <Button size="small" loading={resetAdmin.isPending && resetAdmin.variables?.id === t.id}>Reset admin password</Button>
                  </Popconfirm>
                )}
              </Flex>
            ) },
          ]} />
      </Card>
      <Modal open={adding} title="Add organisation" onCancel={() => setAdding(false)} onOk={() => form.submit()} confirmLoading={add.isPending} okText="Create" destroyOnHidden width={560}>
        <Form form={form} layout="vertical" onFinish={(v) => add.mutate(v)} requiredMark="optional">
          <Form.Item name="code" label="Organisation code" rules={[{ required: true }]} normalize={(v: string) => v?.toUpperCase()} extra="What people type to sign in: 2–32 letters, digits, - or _."><Input maxLength={32} /></Form.Item>
          <Form.Item name="name" label="Organisation name" rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>
          <Form.Item name="adminName" label="First administrator's name" rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>
          <Form.Item name="adminEmail" label="First administrator's email" rules={[{ required: true }, { type: 'email' }]} extra="They receive a link to choose their own password."><Input maxLength={256} /></Form.Item>
          <Flex gap={12}>
            <Form.Item name="maxUsers" label="Most users" style={{ flex: 1 }} extra="Leave empty for no limit."><InputNumber min={1} precision={0} style={{ width: '100%' }} /></Form.Item>
            <Form.Item name="maxLocations" label="Most CFAs & distributors" style={{ flex: 1 }} extra="Leave empty for no limit."><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
          </Flex>
          <Form.Item label="Modules" extra="Users, roles, audit and approvals are always included. You can change this later.">
            <Checkbox.Group value={newModules} onChange={(v) => setNewModules(v as string[])} style={{ display: 'block' }}>
              <Flex vertical gap={8}>{(modules.data ?? []).map((m) => <Checkbox key={m.key} value={m.key}><b>{m.name}</b></Checkbox>)}</Flex>
            </Checkbox.Group>
          </Form.Item>
        </Form>
      </Modal>
      <Modal open={reset !== null} title="Password link issued" onCancel={() => setReset(null)} footer={<Button type="primary" onClick={() => setReset(null)}>Done</Button>} destroyOnHidden width={620}>
        {reset && (
          <>
            <Typography.Paragraph>A link was emailed to <b>{reset.adminName}</b> ({reset.adminEmail}). If it does not arrive, give them this single-use link yourself. It is shown only now and expires {new Date(reset.expiresAt).toLocaleString()}.</Typography.Paragraph>
            <Typography.Paragraph copyable code style={{ wordBreak: 'break-all' }}>{reset.resetLink}</Typography.Paragraph>
          </>
        )}
      </Modal>
      <Modal open={created !== null} title="Organisation created" onCancel={() => setCreated(null)} footer={<Button type="primary" onClick={() => setCreated(null)}>Done</Button>} destroyOnHidden width={620}>
        {created && (
          <>
            <Typography.Paragraph>{created.code} was created. An invitation was emailed to <b>{created.adminEmail}</b>. If the email does not arrive, give them this single-use link yourself. It is shown only now and expires {new Date(created.inviteExpiresAt).toLocaleString()}.</Typography.Paragraph>
            <Typography.Paragraph copyable code style={{ wordBreak: 'break-all' }}>{created.inviteLink}</Typography.Paragraph>
          </>
        )}
      </Modal>
      <Modal open={editing !== null} title={editing ? `Edit ${editing.name}` : ''} onCancel={() => setEditing(null)} onOk={() => editForm.submit()} confirmLoading={save.isPending} okText="Save" destroyOnHidden width={560}>
        <Form form={editForm} layout="vertical" onFinish={(v) => save.mutate({ name: v.name, maxUsers: v.maxUsers ?? null, maxLocations: v.maxLocations ?? null })} requiredMark="optional">
          <Form.Item label="Organisation code"><Input value={editing?.code} disabled /></Form.Item>
          <Form.Item name="name" label="Organisation name" rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>
          <Flex gap={12}>
            <Form.Item name="maxUsers" label="Most active users" style={{ flex: 1 }} extra={editing ? `Now ${editing.activeUsers} active. Empty = no limit.` : undefined}><InputNumber min={1} precision={0} style={{ width: '100%' }} /></Form.Item>
            <Form.Item name="maxLocations" label="Most CFAs & distributors" style={{ flex: 1 }} extra="Empty = no limit."><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
          </Flex>
          <Typography.Paragraph type="secondary">A limit below what the organisation already has does not remove anything: it only stops new users or locations from being added.</Typography.Paragraph>
        </Form>
        <Alert type="info" showIcon style={{ marginBottom: 12 }} title="People already signed in see a module change within 15 minutes, or at their next sign-in. Nothing is deleted: switching a module back on restores access to its data." />
        <Checkbox.Group value={selected} onChange={(v) => setSelected(v as string[])} style={{ display: 'block' }}>
          <Flex vertical gap={10}>
            {(modules.data ?? []).map((m) => (
              <Checkbox key={m.key} value={m.key}><b>{m.name}</b><br /><Typography.Text type="secondary">{m.description}</Typography.Text></Checkbox>
            ))}
          </Flex>
        </Checkbox.Group>
      </Modal>
    </>
  )
}
