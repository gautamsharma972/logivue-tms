import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Switch, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DelayAttribution, DelayReasonSetting, ExecutionDto, ExecutionEventType, LaneDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { INDIAN_STATES } from '@/lib/indiaStates'
import { attributionLabel, defaultDelayReasons, eventLabels } from './constants'

const attributionColor: Record<DelayAttribution, string> = { None: 'green', Carrier: 'red', NonCarrier: 'blue', Unattributed: 'orange' }

function Delay({ minutes, attribution }: { minutes: number | null; attribution: DelayAttribution }) {
  if (minutes === null) return <Typography.Text type="secondary">—</Typography.Text>
  return <Tag color={attributionColor[attribution]}>{attribution === 'None' ? 'On time' : `${minutes} min · ${attributionLabel[attribution]}`}</Tag>
}

function useDelayReasons(): DelayReasonSetting[] {
  const { can } = useAuth()
  const settings = useQuery({ queryKey: queryKeys.performance.settings, queryFn: () => performanceApi.settings(), enabled: can('transporters.performance.read') })
  const policy = settings.data?.find((s) => s.key === 'execution.delayPolicy')?.value as { reasons?: DelayReasonSetting[] } | undefined
  return policy?.reasons ?? defaultDelayReasons
}

function RecordEvent({ execution, onClose }: { execution: ExecutionDto; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const reasons = useDelayReasons()
  const [form] = Form.useForm<{ eventType: ExecutionEventType; eventAt: dayjs.Dayjs; reason?: string }>()
  const order = Object.keys(eventLabels) as ExecutionEventType[]
  const furthest = Math.max(-1, ...execution.events.map((e) => order.indexOf(e.eventType)))
  const options = order.filter((_, i) => i > furthest) // each milestone once, and only later than the last one recorded
  const save = useMutation({
    mutationFn: (v: { eventType: ExecutionEventType; eventAt: dayjs.Dayjs; reason?: string }) =>
      performanceApi.recordEvent(execution.id, v.eventType, v.eventAt.toISOString(), v.reason),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
      void message.success('Recorded')
      onClose()
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Modal open title={`Record a milestone: ${execution.shipmentNumber}`} okText="Record" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form form={form} layout="vertical" initialValues={{ eventAt: dayjs() }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="eventType" label="What happened" rules={[{ required: true, message: 'Choose the milestone' }]}>
          <Select aria-label="Milestone" virtual={false} options={options.map((t) => ({ value: t, label: eventLabels[t] }))} />
        </Form.Item>
        <Form.Item name="eventAt" label="When" rules={[{ required: true, message: 'Say when' }]}>
          <DatePicker showTime format="DD MMM YYYY HH:mm" style={{ width: '100%' }} aria-label="When" />
        </Form.Item>
        <Form.Item name="reason" label="Reason for any delay" extra="Only needed if this milestone was late.">
          <Select aria-label="Delay reason" allowClear virtual={false} options={reasons.map((r) => ({ value: r.code, label: `${r.name} (${attributionLabel[r.attribution === 'Carrier' ? 'Carrier' : r.attribution === 'NonCarrier' ? 'NonCarrier' : 'Unattributed']})` }))} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function Attribute({ execution, delivery, onClose }: { execution: ExecutionDto; delivery: boolean; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const reasons = useDelayReasons()
  const [code, setCode] = useState<string>()
  const save = useMutation({
    mutationFn: () => performanceApi.attributeDelay(execution.id, delivery, code!),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
      void message.success('Reason saved and KPIs updated')
      onClose()
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Modal open title={`Why was the ${delivery ? 'delivery' : 'pickup'} late? ${execution.shipmentNumber}`} okText="Save reason" okButtonProps={{ disabled: !code }} confirmLoading={save.isPending} onCancel={onClose} onOk={() => save.mutate()} destroyOnHidden>
      <Typography.Paragraph type="secondary">Only delays caused by the carrier count against it. The minutes late stay as recorded.</Typography.Paragraph>
      <Select aria-label="Reason" style={{ width: '100%' }} virtual={false} value={code} onChange={setCode} placeholder="Choose a reason"
        options={reasons.map((r) => ({ value: r.code, label: `${r.name} — ${r.attribution === 'Carrier' ? 'counts against the carrier' : r.attribution === 'NonCarrier' ? 'does not count against the carrier' : 'left out until clarified'}` }))} />
    </Modal>
  )
}

function Loads({ transporterId }: { transporterId: string }) {
  const executions = useQuery({ queryKey: queryKeys.performance.executions(transporterId), queryFn: () => performanceApi.executions(transporterId) })
  const [recording, setRecording] = useState<ExecutionDto | null>(null)
  const [attributing, setAttributing] = useState<{ execution: ExecutionDto; delivery: boolean } | null>(null)
  const { can, user } = useAuth()
  const canAttribute = can('transporters.performance.manage') && user?.transporterId == null
  return (
    <Card title="Loads">
      <Typography.Paragraph type="secondary">Created when a load is accepted. Departure and delivery are recorded when the shipment is dispatched and delivered; add arrival and loading times, and say why anything was late.</Typography.Paragraph>
      {executions.isError && <Alert type="error" showIcon title={executions.error.message} />}
      <Table<ExecutionDto>
        size="small"
        rowKey="id"
        loading={executions.isLoading}
        pagination={{ pageSize: 10, hideOnSinglePage: true }}
        dataSource={executions.data ?? []}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'No loads yet.' }}
        columns={[
          { title: 'Shipment', dataIndex: 'shipmentNumber', render: (n: string, e) => <Link to={`/shipments/${e.shipmentId}`}>{n}</Link> },
          { title: 'Planned pickup', dataIndex: 'plannedPickupAt', render: (d: string | null) => (d ? formatDateTime(d) : '—') },
          { title: 'Pickup', key: 'p', render: (_, e) => (
            <Flex gap={4} wrap align="center"><Delay minutes={e.pickupDelayMinutes} attribution={e.pickupAttribution} />
              {canAttribute && e.pickupAttribution === 'Unattributed' && <Button size="small" onClick={() => setAttributing({ execution: e, delivery: false })}>Say why</Button>}</Flex>) },
          { title: 'Planned delivery', dataIndex: 'plannedDeliveryAt', render: (d: string | null) => (d ? formatDateTime(d) : '—') },
          { title: 'Delivery', key: 'd', render: (_, e) => (
            <Flex gap={4} wrap align="center"><Delay minutes={e.deliveryDelayMinutes} attribution={e.deliveryAttribution} />
              {canAttribute && e.deliveryAttribution === 'Unattributed' && <Button size="small" onClick={() => setAttributing({ execution: e, delivery: true })}>Say why</Button>}</Flex>) },
          { title: 'Status', dataIndex: 'status', render: (s: string) => <Tag>{s.replace(/([A-Z])/g, ' $1').trim()}</Tag> },
          { title: '', key: 'a', align: 'right', render: (_, e) => (e.status !== 'Delivered' ? <Can permission={user?.transporterId == null ? 'transporters.performance.manage' : 'transporters.performance.self'}><Button size="small" onClick={() => setRecording(e)}>Record milestone</Button></Can> : null) },
        ]}
      />
      {recording && <RecordEvent execution={recording} onClose={() => setRecording(null)} />}
      {attributing && <Attribute execution={attributing.execution} delivery={attributing.delivery} onClose={() => setAttributing(null)} />}
    </Card>
  )
}

function AddLane({ transporterId, lane, onClose }: { transporterId: string; lane: LaneDto | null; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm()
  const save = useMutation({
    mutationFn: (v: { originState: string; originCity?: string; destinationState: string; destinationCity?: string; mode?: 'Ftl' | 'Ptl'; transitHours?: number; from: dayjs.Dayjs; isActive: boolean }) => {
      const body = {
        originState: v.originState, originCity: v.originCity?.trim() || null, destinationState: v.destinationState, destinationCity: v.destinationCity?.trim() || null,
        mode: v.mode ?? null, transitSlaMinutes: v.transitHours ? Math.round(v.transitHours * 60) : null, effectiveFrom: v.from.format('YYYY-MM-DD'), effectiveTo: null,
        isActive: v.isActive, version: lane?.version ?? null,
      }
      return lane ? performanceApi.updateLane(lane.id, body) : performanceApi.createLane(transporterId, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.performance.lanes(transporterId) })
      void message.success(lane ? 'Lane updated' : 'Lane added')
      onClose()
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const states = INDIAN_STATES.map((s) => ({ value: s, label: s }))
  return (
    <Modal open title={lane ? 'Edit lane' : 'Add a lane'} okText="Save" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={lane ? {
          originState: INDIAN_STATES.find((s) => s.toUpperCase() === lane.originState), originCity: lane.originCity ?? '', destinationState: INDIAN_STATES.find((s) => s.toUpperCase() === lane.destinationState),
          destinationCity: lane.destinationCity ?? '', mode: lane.mode ?? undefined, transitHours: lane.transitSlaMinutes ? lane.transitSlaMinutes / 60 : undefined, from: dayjs(lane.effectiveFrom), isActive: lane.isActive,
        } : { from: dayjs(), isActive: true }}
        onFinish={(v) => save.mutate(v)}
      >
        <Flex gap={12} wrap>
          <Form.Item name="originState" label="From state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1, minWidth: 180 }}><Select aria-label="From state" showSearch virtual={false} options={states} /></Form.Item>
          <Form.Item name="originCity" label="From city" extra="Optional" style={{ flex: 1, minWidth: 140 }}><Input aria-label="From city" maxLength={100} /></Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="destinationState" label="To state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1, minWidth: 180 }}><Select aria-label="To state" showSearch virtual={false} options={states} /></Form.Item>
          <Form.Item name="destinationCity" label="To city" extra="Optional" style={{ flex: 1, minWidth: 140 }}><Input aria-label="To city" maxLength={100} /></Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="mode" label="Service" style={{ flex: 1, minWidth: 140 }}><Select aria-label="Service" allowClear placeholder="Both" virtual={false} options={[{ value: 'Ftl', label: 'Full truck' }, { value: 'Ptl', label: 'Part load' }]} /></Form.Item>
          <Form.Item name="transitHours" label="Committed transit (hours)" style={{ flex: 1, minWidth: 140 }}><InputNumber min={1} max={300} style={{ width: '100%' }} controls={false} /></Form.Item>
        </Flex>
        <Form.Item name="from" label="In service from" rules={[{ required: true, message: 'Choose a date' }]}><DatePicker style={{ width: '100%' }} format="DD MMM YYYY" /></Form.Item>
        {lane && <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>}
      </Form>
    </Modal>
  )
}

function Lanes({ transporterId }: { transporterId: string }) {
  const lanes = useQuery({ queryKey: queryKeys.performance.lanes(transporterId), queryFn: () => performanceApi.lanes(transporterId) })
  const [editing, setEditing] = useState<LaneDto | null>(null)
  const [adding, setAdding] = useState(false)
  const place = (state: string, city: string | null) => (city ? `${city}, ${state}` : `Any city in ${state}`)
  return (
    <Card title="Lanes served" extra={<Can permission="transporters.performance.manage"><Button icon={<PlusOutlined />} onClick={() => setAdding(true)}>Add lane</Button></Can>}>
      <Typography.Paragraph type="secondary">The routes this transporter covers. KPIs, rankings and benchmarks can be compared lane by lane.</Typography.Paragraph>
      <Table<LaneDto>
        size="small"
        rowKey="id"
        loading={lanes.isLoading}
        pagination={false}
        dataSource={lanes.data ?? []}
        locale={{ emptyText: 'No lanes recorded.' }}
        columns={[
          { title: 'From', key: 'f', render: (_, l) => place(l.originState, l.originCity) },
          { title: 'To', key: 't', render: (_, l) => place(l.destinationState, l.destinationCity) },
          { title: 'Service', dataIndex: 'mode', render: (m: string | null) => (m === 'Ftl' ? 'Full truck' : m === 'Ptl' ? 'Part load' : 'Both') },
          { title: 'Committed transit', dataIndex: 'transitSlaMinutes', render: (m: number | null) => (m ? `${(m / 60).toFixed(m % 60 === 0 ? 0 : 1)} h` : '—') },
          { title: 'Status', dataIndex: 'isActive', render: (a: boolean) => <Tag color={a ? 'green' : 'default'}>{a ? 'Active' : 'Inactive'}</Tag> },
          { title: '', key: 'a', align: 'right', render: (_, l) => <Can permission="transporters.performance.manage"><Button size="small" onClick={() => setEditing(l)}>Edit</Button></Can> },
        ]}
      />
      {(adding || editing) && <AddLane transporterId={transporterId} lane={editing} onClose={() => { setAdding(false); setEditing(null) }} />}
    </Card>
  )
}

export function OperationsPanel({ transporterId }: { transporterId: string }) {
  return (
    <Flex vertical gap={16}>
      <Loads transporterId={transporterId} />
      <Lanes transporterId={transporterId} />
    </Flex>
  )
}
