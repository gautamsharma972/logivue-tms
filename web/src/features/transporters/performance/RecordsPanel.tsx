import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CapacityDayDto, ClaimDto, ClaimType, LoadCostDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'

const claimLabel: Record<ClaimType, string> = { Damage: 'Damage', Shortage: 'Shortage', LossTheft: 'Loss / theft' }

function Claims({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [adding, setAdding] = useState(false)
  const [valuing, setValuing] = useState<ClaimDto | null>(null)
  const [value, setValue] = useState<number | null>(null)
  const [form] = Form.useForm<{ claimType: ClaimType; claimDate: dayjs.Dayjs; claimValue: number; remarks?: string }>()
  const claims = useQuery({ queryKey: queryKeys.performance.claims(transporterId, from, to), queryFn: () => performanceApi.claims(transporterId, from, to) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
  const fail = (e: unknown) => void message.error(toApiError(e).message)
  const add = useMutation({
    mutationFn: (v: { claimType: ClaimType; claimDate: dayjs.Dayjs; claimValue: number; remarks?: string }) =>
      performanceApi.recordClaim(transporterId, { claimType: v.claimType, claimDate: v.claimDate.format('YYYY-MM-DD'), claimValue: v.claimValue, shipmentId: null, remarks: v.remarks?.trim() || null }),
    onSuccess: async () => { await refresh(); setAdding(false); form.resetFields(); void message.success('Claim recorded') },
    onError: fail,
  })
  const resolve = useMutation({ mutationFn: (id: string) => performanceApi.resolveClaim(id), onSuccess: refresh, onError: fail })
  const setClaimValue = useMutation({
    mutationFn: () => performanceApi.setClaimValue(valuing!.id, value ?? 0),
    onSuccess: async () => { await refresh(); setValuing(null) },
    onError: fail,
  })
  const staff = user?.transporterId == null

  return (
    <Card title="Claims" extra={<Can permission="transporters.performance.manage"><Button icon={<PlusOutlined />} onClick={() => setAdding(true)}>Record a claim</Button></Can>}>
      <Typography.Paragraph type="secondary">Damage, shortage and loss claims. Goods that arrive short or damaged raise a claim by themselves; fill in its value when it is known.</Typography.Paragraph>
      <Table<ClaimDto>
        size="small"
        rowKey="id"
        loading={claims.isLoading}
        pagination={{ pageSize: 8, hideOnSinglePage: true }}
        dataSource={claims.data ?? []}
        locale={{ emptyText: 'No claims in this period.' }}
        columns={[
          { title: 'Date', dataIndex: 'claimDate' },
          { title: 'Type', dataIndex: 'claimType', render: (t: ClaimType) => claimLabel[t] },
          { title: 'Shipment', key: 's', render: (_, c) => (c.shipmentId ? <Link to={`/shipments/${c.shipmentId}`}>{c.shipmentNumber}</Link> : '—') },
          { title: 'Value', dataIndex: 'claimValue', align: 'right', render: (v: number) => (v === 0 ? <Typography.Text type="secondary">Not set</Typography.Text> : formatInrExact(v)) },
          { title: 'Status', dataIndex: 'status', render: (s: string) => <Tag color={s === 'Open' ? 'orange' : 'green'}>{s}</Tag> },
          { title: '', key: 'a', align: 'right', render: (_, c) => (
            <Can permission="transporters.performance.manage">
              {c.status === 'Open' && staff && (
                <Flex gap={4} justify="flex-end">
                  <Button size="small" onClick={() => { setValuing(c); setValue(c.claimValue) }}>Set value</Button>
                  <Button size="small" loading={resolve.isPending && resolve.variables === c.id} onClick={() => resolve.mutate(c.id)}>Resolve</Button>
                </Flex>
              )}
            </Can>) },
        ]}
      />
      <Modal open={adding} title="Record a claim" okText="Record" confirmLoading={add.isPending} onCancel={() => setAdding(false)} onOk={() => form.submit()} destroyOnHidden>
        <Form form={form} layout="vertical" initialValues={{ claimType: 'Damage', claimDate: dayjs() }} onFinish={(v) => add.mutate(v)}>
          <Form.Item name="claimType" label="Type"><Select aria-label="Claim type" virtual={false} options={(Object.keys(claimLabel) as ClaimType[]).map((t) => ({ value: t, label: claimLabel[t] }))} /></Form.Item>
          <Form.Item name="claimDate" label="Date" rules={[{ required: true, message: 'Choose a date' }]}><DatePicker disabledDate={(d) => d.isAfter(dayjs())} format="DD MMM YYYY" style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="claimValue" label="Value (₹)" rules={[{ required: true, message: 'Enter the value' }]}><InputNumber min={0} precision={2} controls={false} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="remarks" label="Remarks"><Input.TextArea rows={2} maxLength={500} /></Form.Item>
        </Form>
      </Modal>
      <Modal open={valuing !== null} title="Claim value" okText="Save" confirmLoading={setClaimValue.isPending} onCancel={() => setValuing(null)} onOk={() => setClaimValue.mutate()} destroyOnHidden>
        <InputNumber aria-label="Claim value" min={0} precision={2} controls={false} style={{ width: '100%' }} value={value} onChange={setValue} />
      </Modal>
    </Card>
  )
}

function Costs({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [form] = Form.useForm<{ shipmentId: string; invoicedAmount: number; agreedAmount?: number }>()
  const costs = useQuery({ queryKey: queryKeys.performance.costs(transporterId, from, to), queryFn: () => performanceApi.costs(transporterId, from, to) })
  const executions = useQuery({ queryKey: queryKeys.performance.executions(transporterId), queryFn: () => performanceApi.executions(transporterId), enabled: adding })
  const add = useMutation({
    mutationFn: (v: { shipmentId: string; invoicedAmount: number; agreedAmount?: number }) => performanceApi.recordCost(transporterId, v.shipmentId, v.invoicedAmount, v.agreedAmount),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.all }); setAdding(false); form.resetFields(); void message.success('Cost recorded') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Card title="Invoiced cost" extra={<Button icon={<PlusOutlined />} onClick={() => setAdding(true)}>Record cost</Button>}>
      <Typography.Paragraph type="secondary">One record per load. Cost performance is the share of loads invoiced at or below the agreed amount, which defaults to the freight estimate the load was accepted at.</Typography.Paragraph>
      <Table<LoadCostDto>
        size="small"
        rowKey="id"
        loading={costs.isLoading}
        pagination={{ pageSize: 8, hideOnSinglePage: true }}
        dataSource={costs.data ?? []}
        locale={{ emptyText: 'No costs recorded in this period.' }}
        columns={[
          { title: 'Shipment', key: 's', render: (_, c) => <Link to={`/shipments/${c.shipmentId}`}>{c.shipmentNumber}</Link> },
          { title: 'Service date', dataIndex: 'serviceDate' },
          { title: 'Agreed', dataIndex: 'agreedAmount', align: 'right', render: formatInrExact },
          { title: 'Invoiced', dataIndex: 'invoicedAmount', align: 'right', render: formatInrExact },
          { title: '', dataIndex: 'onBudget', render: (b: boolean) => <Tag color={b ? 'green' : 'red'}>{b ? 'On budget' : 'Over budget'}</Tag> },
        ]}
      />
      <Modal open={adding} title="Record the invoiced cost of a load" okText="Record" confirmLoading={add.isPending} onCancel={() => setAdding(false)} onOk={() => form.submit()} destroyOnHidden>
        <Form form={form} layout="vertical" onFinish={(v) => add.mutate(v)}>
          <Form.Item name="shipmentId" label="Load" rules={[{ required: true, message: 'Choose the load' }]}>
            <Select aria-label="Load" showSearch optionFilterProp="label" virtual={false} loading={executions.isLoading} options={(executions.data ?? []).map((e) => ({ value: e.shipmentId, label: e.shipmentNumber }))} />
          </Form.Item>
          <Form.Item name="invoicedAmount" label="Invoiced (₹)" rules={[{ required: true, message: 'Enter the invoiced amount' }]}><InputNumber min={0} precision={2} controls={false} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="agreedAmount" label="Agreed (₹)" extra="Leave empty to use the amount the load was accepted at."><InputNumber min={0.01} precision={2} controls={false} style={{ width: '100%' }} /></Form.Item>
        </Form>
      </Modal>
    </Card>
  )
}

function Capacity({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ date: dayjs.Dayjs; committed: number; available: number }>()
  const days = useQuery({ queryKey: queryKeys.performance.capacity(transporterId, from, to), queryFn: () => performanceApi.capacity(transporterId, from, to) })
  const save = useMutation({
    mutationFn: (v: { date: dayjs.Dayjs; committed: number; available: number }) => performanceApi.saveCapacity(transporterId, v.date.format('YYYY-MM-DD'), v.committed, v.available),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.all }); void message.success('Capacity saved') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const canReport = ['transporters.performance.manage', 'transporters.performance.self']
  const { can } = useAuth()
  return (
    <Card title="Vehicle capacity">
      <Typography.Paragraph type="secondary">How many vehicles were committed for a day and how many were really available. Saving a day replaces its earlier figures; availability is the share available over the period.</Typography.Paragraph>
      {canReport.some(can) && (
        <Form form={form} layout="inline" initialValues={{ date: dayjs() }} onFinish={(v) => save.mutate(v)} style={{ marginBottom: 12, rowGap: 8 }}>
          <Form.Item name="date" rules={[{ required: true, message: 'Choose a day' }]}><DatePicker aria-label="Capacity day" allowClear={false} format="DD MMM YYYY" /></Form.Item>
          <Form.Item name="committed" rules={[{ required: true, message: 'Enter committed' }]}><InputNumber aria-label="Vehicles committed" placeholder="Committed" min={0} max={10000} precision={0} controls={false} /></Form.Item>
          <Form.Item name="available" dependencies={['committed']} rules={[{ required: true, message: 'Enter available' }, ({ getFieldValue }) => ({ validator: (_, v: number) => (v == null || v <= (getFieldValue('committed') ?? Infinity) ? Promise.resolve() : Promise.reject(new Error('More than committed'))) })]}>
            <InputNumber aria-label="Vehicles available" placeholder="Available" min={0} max={10000} precision={0} controls={false} />
          </Form.Item>
          <Button htmlType="submit" loading={save.isPending}>Save day</Button>
        </Form>
      )}
      <Table<CapacityDayDto>
        size="small"
        rowKey="id"
        loading={days.isLoading}
        pagination={{ pageSize: 8, hideOnSinglePage: true }}
        dataSource={days.data ?? []}
        locale={{ emptyText: 'No capacity reported in this period.' }}
        columns={[
          { title: 'Date', dataIndex: 'date' },
          { title: 'Committed', dataIndex: 'vehiclesCommitted', align: 'right' },
          { title: 'Available', dataIndex: 'vehiclesAvailable', align: 'right' },
          { title: 'Availability', key: 'a', align: 'right', render: (_, d) => (d.vehiclesCommitted === 0 ? '—' : `${Math.round((d.vehiclesAvailable * 100) / d.vehiclesCommitted)}%`) },
        ]}
      />
    </Card>
  )
}

/** The records that feed the claims, cost and availability KPIs. Each save rebuilds the KPI for its month; nothing is typed into a KPI. */
export function RecordsPanel({ transporterId, from, to }: { transporterId: string; from: string; to: string }) {
  const { user } = useAuth()
  const staff = user?.transporterId == null
  return (
    <Flex vertical gap={16}>
      <Claims transporterId={transporterId} from={from} to={to} />
      {staff && <Can permission="transporters.performance.read"><Costs transporterId={transporterId} from={from} to={to} /></Can>}
      <Capacity transporterId={transporterId} from={from} to={to} />
    </Flex>
  )
}
