import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, Flex, Form, Input, InputNumber, Modal, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { applyFieldErrors } from '@/lib/formErrors'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { BranchDto, ContactDto } from '@/lib/api/types'

interface ContactForm {
  name: string
  designation?: string
  email?: string
  phone?: string
  contactType: string
  isPrimary: boolean
  isActive: boolean
}

interface BranchForm {
  code: string
  name: string
  address?: string
  city?: string
  state?: string
  latitude?: number | null
  longitude?: number | null
  contactName?: string
  contactPhone?: string
  isActive: boolean
}

function ContactModal({ transporterId, contact, onClose }: { transporterId: string; contact: ContactDto | null; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<ContactForm>()
  const save = useMutation({
    mutationFn: (v: ContactForm) => {
      const body = { name: v.name.trim(), designation: v.designation?.trim() || null, email: v.email?.trim() || null, phone: v.phone?.trim() || null, contactType: v.contactType.trim(), isPrimary: v.isPrimary, isActive: v.isActive, version: contact?.version ?? null }
      return contact ? performanceApi.updateContact(contact.id, body) : performanceApi.addContact(transporterId, body)
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.contacts(transporterId) }); void message.success(contact ? 'Contact updated' : 'Contact added'); onClose() },
    onError: (e) => { const error = toApiError(e); if (!applyFieldErrors(form, error)) void message.error(error.message) },
  })
  return (
    <Modal open title={contact ? 'Edit contact' : 'Add a contact'} okText="Save" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form<ContactForm> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}
        initialValues={contact ? { ...contact, designation: contact.designation ?? '', email: contact.email ?? '', phone: contact.phone ?? '' } : { contactType: 'Operations', isPrimary: false, isActive: true }}>
        <Flex gap={12} wrap>
          <Form.Item name="name" label="Name" rules={[{ required: true, whitespace: true, message: 'Enter the name' }]} style={{ flex: 1, minWidth: 200 }}><Input aria-label="Name" maxLength={150} /></Form.Item>
          <Form.Item name="designation" label="Designation" style={{ flex: 1, minWidth: 160 }}><Input aria-label="Designation" maxLength={100} /></Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="phone" label="Mobile" style={{ flex: 1, minWidth: 160 }}><Input aria-label="Mobile" inputMode="tel" maxLength={15} /></Form.Item>
          <Form.Item name="email" label="Email" style={{ flex: 1, minWidth: 200 }}><Input aria-label="Email" maxLength={254} /></Form.Item>
        </Flex>
        <Form.Item name="contactType" label="For" rules={[{ required: true, whitespace: true, message: 'Say what this person is for' }]} extra="e.g. Operations, Accounts, Escalation"><Input aria-label="For" maxLength={50} /></Form.Item>
        <Flex gap={24}>
          <Form.Item name="isPrimary" label="Primary contact" valuePropName="checked"><Switch /></Form.Item>
          {contact && <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>}
        </Flex>
      </Form>
    </Modal>
  )
}

function BranchModal({ transporterId, branch, onClose }: { transporterId: string; branch: BranchDto | null; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<BranchForm>()
  const save = useMutation({
    mutationFn: (v: BranchForm) => {
      const body = {
        code: v.code.trim(), name: v.name.trim(), address: v.address?.trim() || null, city: v.city?.trim() || null, state: v.state?.trim() || null, latitude: v.latitude ?? null, longitude: v.longitude ?? null,
        contactName: v.contactName?.trim() || null, contactPhone: v.contactPhone?.trim() || null, isActive: v.isActive, version: branch?.version ?? null,
      }
      return branch ? performanceApi.updateBranch(branch.id, body) : performanceApi.addBranch(transporterId, body)
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.branches(transporterId) }); void message.success(branch ? 'Branch updated' : 'Branch added'); onClose() },
    onError: (e) => { const error = toApiError(e); if (!applyFieldErrors(form, error)) void message.error(error.message) },
  })
  return (
    <Modal open title={branch ? 'Edit branch' : 'Add a branch'} okText="Save" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form<BranchForm> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}
        initialValues={branch ? { ...branch, address: branch.address ?? '', city: branch.city ?? '', state: branch.state ?? '', contactName: branch.contactName ?? '', contactPhone: branch.contactPhone ?? '' } : { isActive: true }}>
        <Flex gap={12} wrap>
          <Form.Item name="code" label="Code" rules={[{ required: true, whitespace: true, message: 'Enter a code' }]} extra="2–30 letters, digits or hyphens" style={{ width: 160 }}><Input aria-label="Code" maxLength={30} /></Form.Item>
          <Form.Item name="name" label="Name" rules={[{ required: true, whitespace: true, message: 'Enter the name' }]} style={{ flex: 1, minWidth: 200 }}><Input aria-label="Name" maxLength={150} /></Form.Item>
        </Flex>
        <Form.Item name="address" label="Address"><Input aria-label="Address" maxLength={300} /></Form.Item>
        <Flex gap={12} wrap>
          <Form.Item name="city" label="City" style={{ flex: 1, minWidth: 140 }}><Input aria-label="City" maxLength={100} /></Form.Item>
          <Form.Item name="state" label="State" style={{ flex: 1, minWidth: 140 }}><Input aria-label="State" maxLength={100} /></Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="latitude" label="Latitude" style={{ flex: 1, minWidth: 120 }}><InputNumber aria-label="Latitude" min={-90} max={90} controls={false} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="longitude" label="Longitude" style={{ flex: 1, minWidth: 120 }}><InputNumber aria-label="Longitude" min={-180} max={180} controls={false} style={{ width: '100%' }} /></Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="contactName" label="Contact" style={{ flex: 1, minWidth: 160 }}><Input aria-label="Contact" maxLength={150} /></Form.Item>
          <Form.Item name="contactPhone" label="Contact mobile" style={{ flex: 1, minWidth: 160 }}><Input aria-label="Contact mobile" inputMode="tel" maxLength={15} /></Form.Item>
        </Flex>
        {branch && <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>}
      </Form>
    </Modal>
  )
}

/** Extra contacts and depots of a transporter, on its overview. Maintained by staff, or by the vendor for its own company. */
export function ContactsPanel({ transporterId }: { transporterId: string }) {
  const { can, user } = useAuth()
  const canEdit = user?.transporterId == null ? can('transporters.manage') : can('transporters.self.manage')
  const [contact, setContact] = useState<ContactDto | null | 'new'>(null)
  const [branch, setBranch] = useState<BranchDto | null | 'new'>(null)
  const contacts = useQuery({ queryKey: queryKeys.performance.contacts(transporterId), queryFn: () => performanceApi.contacts(transporterId) })
  const branches = useQuery({ queryKey: queryKeys.performance.branches(transporterId), queryFn: () => performanceApi.branches(transporterId) })

  return (
    <Flex vertical gap={16}>
      <Card title="Contacts" extra={canEdit && <Button icon={<PlusOutlined />} onClick={() => setContact('new')}>Add contact</Button>}>
        <Table<ContactDto>
          size="small"
          rowKey="id"
          loading={contacts.isLoading}
          pagination={false}
          dataSource={contacts.data ?? []}
          locale={{ emptyText: 'No extra contacts. The main contact is on the company details.' }}
          columns={[
            { title: 'Name', key: 'n', render: (_, c) => <div><Typography.Text strong>{c.name}</Typography.Text>{c.isPrimary && <Tag color="green" style={{ marginInlineStart: 8 }}>Primary</Tag>}{!c.isActive && <Tag style={{ marginInlineStart: 8 }}>Inactive</Tag>}<br /><Typography.Text type="secondary">{c.designation ?? ''}</Typography.Text></div> },
            { title: 'For', dataIndex: 'contactType' },
            { title: 'Mobile', dataIndex: 'phone', render: (p: string | null) => p ?? '—' },
            { title: 'Email', dataIndex: 'email', render: (e: string | null) => e ?? '—' },
            { title: '', key: 'a', align: 'right', render: (_, c) => (canEdit ? <Button size="small" onClick={() => setContact(c)}>Edit</Button> : null) },
          ]}
        />
      </Card>
      <Card title="Branches and depots" extra={canEdit && <Button icon={<PlusOutlined />} onClick={() => setBranch('new')}>Add branch</Button>}>
        <Table<BranchDto>
          size="small"
          rowKey="id"
          loading={branches.isLoading}
          pagination={false}
          dataSource={branches.data ?? []}
          locale={{ emptyText: 'No branches recorded.' }}
          columns={[
            { title: 'Branch', key: 'b', render: (_, b) => <div><Typography.Text strong>{b.name}</Typography.Text> <Tag variant="filled">{b.code}</Tag>{!b.isActive && <Tag>Inactive</Tag>}</div> },
            { title: 'Place', key: 'p', render: (_, b) => [b.city, b.state].filter(Boolean).join(', ') || '—' },
            { title: 'Contact', key: 'c', render: (_, b) => [b.contactName, b.contactPhone].filter(Boolean).join(' · ') || '—' },
            { title: '', key: 'a', align: 'right', render: (_, b) => (canEdit ? <Button size="small" onClick={() => setBranch(b)}>Edit</Button> : null) },
          ]}
        />
      </Card>
      {contact && <ContactModal transporterId={transporterId} contact={contact === 'new' ? null : contact} onClose={() => setContact(null)} />}
      {branch && <BranchModal transporterId={transporterId} branch={branch === 'new' ? null : branch} onClose={() => setBranch(null)} />}
    </Flex>
  )
}
