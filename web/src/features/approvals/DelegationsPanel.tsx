import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Form, Input, Modal, Popconfirm, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { approvalsApi, usersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DelegationDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

interface FormValues {
  delegateId: string
  period: [import('dayjs').Dayjs, import('dayjs').Dayjs]
  reason?: string
}

function NewDelegationModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search.trim())
  const people = useQuery({ queryKey: ['users', 'lookup', debounced], queryFn: () => usersApi.lookup(debounced || undefined), enabled: open })

  const create = useMutation({
    mutationFn: (v: FormValues) =>
      approvalsApi.createDelegation({
        delegateId: v.delegateId,
        validFrom: v.period[0].toISOString(),
        validTo: v.period[1].toISOString(),
        reason: v.reason?.trim() || null,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.delegations })
      void message.success('Delegation created')
      form.resetFields()
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Modal
      open={open}
      title="Delegate my approvals"
      okText="Create delegation"
      confirmLoading={create.isPending}
      onCancel={onClose}
      onOk={() => form.submit()}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">
        While active, this person can decide approvals that you could decide. They gain no other access, and you can revoke this at any time.
      </Typography.Paragraph>
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => create.mutate(v)}>
        <Form.Item label="Delegate to" name="delegateId" rules={[{ required: true, message: 'Choose a person' }]}>
          <Select
            showSearch
            filterOption={false}
            onSearch={setSearch}
            loading={people.isFetching}
            placeholder="Search by name or email"
            options={people.data?.map((p) => ({ value: p.id, label: `${p.fullName} — ${p.email}` }))}
          />
        </Form.Item>
        <Form.Item label="Period" name="period" rules={[{ required: true, message: 'Choose when the delegation applies' }]}>
          <DatePicker.RangePicker showTime={{ format: 'HH:mm' }} format="D MMM YYYY, HH:mm" style={{ width: '100%' }} />
        </Form.Item>
        <Form.Item label="Reason" name="reason" rules={[{ max: 300 }]}>
          <Input placeholder="e.g. On leave" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

export function DelegationsPanel() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [creating, setCreating] = useState(false)
  const delegations = useQuery({ queryKey: queryKeys.approvals.delegations, queryFn: approvalsApi.delegations })

  const revoke = useMutation({
    mutationFn: approvalsApi.revokeDelegation,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.delegations })
      void message.success('Delegation revoked')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const common: TableColumnsType<DelegationDto> = [
    { title: 'From', dataIndex: 'validFrom', render: formatDateTime },
    { title: 'Until', dataIndex: 'validTo', render: formatDateTime },
    { title: 'Reason', dataIndex: 'reason', render: (r: string | null) => r ?? '—' },
    { title: 'Status', dataIndex: 'isActive', render: (active: boolean) => <Tag color={active ? 'green' : 'default'}>{active ? 'Active' : 'Scheduled / ended'}</Tag> },
  ]

  const given: TableColumnsType<DelegationDto> = [
    { title: 'Delegated to', dataIndex: 'delegateName' },
    ...common,
    {
      title: '',
      key: 'revoke',
      render: (_, d) => (
        <Popconfirm title="Revoke this delegation?" okText="Revoke" onConfirm={() => revoke.mutate(d.id)}>
          <Button type="link" danger loading={revoke.isPending && revoke.variables === d.id}>
            Revoke
          </Button>
        </Popconfirm>
      ),
    },
  ]
  const received: TableColumnsType<DelegationDto> = [{ title: 'Delegated by', dataIndex: 'delegatorName' }, ...common]

  return (
    <Flex vertical gap={16}>
      {delegations.isError && <Alert type="error" showIcon title={delegations.error.message} />}
      <Card
        title="Approvals I've delegated"
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            New delegation
          </Button>
        }
        styles={{ body: { padding: 0 } }}
      >
        <Table rowKey="id" size="middle" columns={given} dataSource={delegations.data?.given} loading={delegations.isLoading} pagination={false} scroll={{ x: 'max-content' }} locale={{ emptyText: 'You have not delegated your approvals' }} />
      </Card>
      <Card title="Delegated to me" styles={{ body: { padding: 0 } }}>
        <Table rowKey="id" size="middle" columns={received} dataSource={delegations.data?.received} loading={delegations.isLoading} pagination={false} scroll={{ x: 'max-content' }} locale={{ emptyText: 'Nobody has delegated approvals to you' }} />
      </Card>
      <NewDelegationModal open={creating} onClose={() => setCreating(false)} />
    </Flex>
  )
}
