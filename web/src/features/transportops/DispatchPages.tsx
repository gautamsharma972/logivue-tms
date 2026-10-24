import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Col, DatePicker, Descriptions, Divider, Flex, Form, Input, InputNumber, List, Modal, Popconfirm, QRCode, Row, Select, Space, Steps, Table, Timeline, Typography, message } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useIsMobile } from '@/lib/useIsMobile'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type DeliveryStatus, type EventDto, type EventType, type LrDetailDto, type LrSummaryDto, type TripDetailDto, type TripSummaryDto } from '@/lib/api/opsApi'
import { useAuth } from '@/features/auth/AuthContext'
import { day, eventLabel, kg, mins, when } from './fmt'
import { ResponsiveDrawer, StatusTag } from './shared'
import { useCustomerOptions, useDebounced, useFileOpener, useLocationOptions, useUomOptions } from './hooks'
import { PhotoPicker } from './PhotoPicker'

const deliveryStatuses: DeliveryStatus[] = ['Created', 'Assigned', 'AtCfa', 'Loading', 'Loaded', 'InTransit', 'Arrived', 'UnloadingCompleted', 'Cancelled']

/** The goods table shared by the LR screen and its printout. */
const lineColumns = [
  { title: 'No. of packages', dataIndex: 'packages' }, { title: 'Unit', dataIndex: 'unit' }, { title: 'Gross weight', dataIndex: 'weightKg', render: kg },
  { title: 'Net weight', dataIndex: 'netWeightKg', render: (v: number | null | undefined) => (v == null ? '—' : kg(v)) },
  { title: 'Vol. cu. ft.', dataIndex: 'volumeCuFt', render: (v: number | null | undefined) => v ?? '—' },
]

const sourceText = (e: EventDto) => (e.source === 'QrWeb' ? 'driver (QR)' : e.source === 'Office' ? 'office' : 'system')

export function EventTimeline({ events }: { events: EventDto[] }) {
  return (
    <Timeline items={events.map((e) => ({
      color: e.type === 'ArrivalRejected' ? 'red' : e.type === 'Correction' ? 'orange' : e.type === 'PodPending' ? 'gray' : 'green',
      content: (
        <div>
          <b>{eventLabel[e.type]}</b> · {when(e.actualAt)} <Typography.Text type="secondary">({sourceText(e)}{e.recordedAt !== e.actualAt ? `, recorded ${when(e.recordedAt)}` : ''})</Typography.Text>
          {e.verification && <div><Typography.Text type="secondary">GPS check: {e.verification}{e.distanceM != null ? `, ${Math.round(e.distanceM)} m from the point (limit ${e.radiusM} m)` : ''}{e.accuracyM != null ? `, accuracy ${Math.round(e.accuracyM)} m` : ''}</Typography.Text></div>}
          {e.remarks && <div><Typography.Text type="secondary">{e.remarks}</Typography.Text></div>}
        </div>
      ),
    }))} />
  )
}

export function LrsPage({ openId, onOpen }: { openId?: string; onOpen?: (id: string | null) => void }) {
  const mobile = useIsMobile()
  const { can } = useAuth()
  const nav = useNavigate()
  const qc = useQueryClient()
  const customers = useCustomerOptions()
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [status, setStatus] = useState<DeliveryStatus>()
  const [customerId, setCustomerId] = useState<string>()
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(false)
  const [viewing, setViewing] = useState<string | null>(openId ?? null)
  const list = useQuery({ queryKey: ['ops', 'lrs', q, status, customerId, page], queryFn: () => opsApi.lrs({ search: q || undefined, status, customerId, page, pageSize: 25 }) })
  const show = (id: string | null) => { setViewing(id); onOpen?.(id) }
  return (
    <>
      <PageHeader title="Dispatch and LRs" description="One LR for each consignment to one destination. Each LR has its own QR code for the driver." actions={can('ops.dispatch') && <Button type="primary" onClick={() => setCreating(true)}>New LR</Button>} />
      <Card>
        <Space style={{ marginBottom: 12, width: mobile ? '100%' : undefined }} orientation={mobile ? 'vertical' : 'horizontal'} wrap>
          <Input.Search allowClear placeholder="LR number, PO or vehicle" style={{ width: mobile ? '100%' : 260 }} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="Delivery status" style={{ width: mobile ? '100%' : 200 }} value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={deliveryStatuses.map((s) => ({ value: s, label: s.replace(/([a-z])([A-Z])/g, '$1 $2') }))} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="Customer" style={{ width: mobile ? '100%' : 260 }} options={customers} value={customerId} onChange={(v) => { setCustomerId(v); setPage(1) }} />
          {can('ops.dispatch') && <Button onClick={() => nav('/ops/trips?new=1')}>Plan a trip</Button>}
        </Space>
        {mobile ? (
          <List loading={list.isLoading} dataSource={list.data?.items} locale={{ emptyText: 'No LRs' }} pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false, simple: true }}
            renderItem={(r) => (
              <List.Item onClick={() => show(r.id)} style={{ cursor: 'pointer' }}>
                <Flex vertical gap={2} style={{ width: '100%' }}>
                  <Flex justify="space-between" align="center"><Typography.Text strong>{r.number}</Typography.Text><StatusTag value={r.status} /></Flex>
                  <div>{r.customerName}</div>
                  <Typography.Text type="secondary">{r.destinationName}, {r.destinationCity}</Typography.Text>
                  <Flex gap={8} wrap align="center"><span>{kg(r.totalWeightKg)}</span><span>{r.vehicleRegistration ?? 'No vehicle yet'}</span>{r.customerReference && <span>PO {r.customerReference}</span>}<StatusTag value={r.podStatus} /></Flex>
                </Flex>
              </List.Item>
            )} />
        ) : (
        <Table<LrSummaryDto> rowKey="id" loading={list.isLoading} dataSource={list.data?.items} scroll={{ x: 1100 }}
          pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false }}
          columns={[
            { title: 'LR', dataIndex: 'number', render: (v, r) => <a onClick={() => show(r.id)}>{v}</a> }, { title: 'Customer', dataIndex: 'customerName' }, { title: 'PO', dataIndex: 'customerReference', render: (v) => v ?? '—' },
            { title: 'Destination', render: (_, r) => `${r.destinationName}, ${r.destinationCity}` }, { title: 'Vehicle', dataIndex: 'vehicleRegistration', render: (v) => v ?? '—' }, { title: 'Trip', dataIndex: 'tripNumber', render: (v) => v ?? '—' },
            { title: 'Weight', dataIndex: 'totalWeightKg', render: kg }, { title: 'Delivery', dataIndex: 'status', render: (v) => <StatusTag value={v} /> }, { title: 'POD', dataIndex: 'podStatus', render: (v) => <StatusTag value={v} /> },
            { title: 'Unloaded', dataIndex: 'unloadedAt', render: when },
          ]} />
        )}
      </Card>
      <LrDrawer id={viewing} onClose={() => show(null)} onChanged={() => void qc.invalidateQueries({ queryKey: ['ops'] })} />
      <CreateLrDrawer open={creating} onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); void qc.invalidateQueries({ queryKey: ['ops'] }); show(id) }} />
    </>
  )
}

function LrDrawer({ id, onClose, onChanged }: { id: string | null; onClose: () => void; onChanged: () => void }) {
  const mobile = useIsMobile()
  const { can } = useAuth()
  const nav = useNavigate()
  const openFile = useFileOpener()
  const q = useQuery({ queryKey: ['ops', 'lr', id], queryFn: () => opsApi.lr(id!), enabled: !!id })
  const d = q.data
  const refresh = () => { void q.refetch(); onChanged() }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const revoke = useMutation({ mutationFn: () => opsApi.revokeQr(id!), onSuccess: refresh, onError: fail })
  const reissue = useMutation({ mutationFn: () => opsApi.reissueQr(id!), onSuccess: () => { message.success('New code issued. Reprint the LR: the old code no longer works.'); refresh() }, onError: fail })
  const cancel = useMutation({ mutationFn: () => opsApi.cancelLr(id!), onSuccess: refresh, onError: fail })
  const [event, setEvent] = useState<{ type: 'DestinationArrival' | 'UnloadingCompleted'; correct: boolean } | null>(null)
  const [form] = Form.useForm<{ at?: dayjs.Dayjs; reason: string }>()
  const record = useMutation({
    mutationFn: (v: { at?: dayjs.Dayjs; reason: string }) => event!.correct
      ? opsApi.correctDestinationEvent(id!, { type: event!.type, newActualAt: v.at!.toISOString(), reason: v.reason })
      : opsApi.destinationEvent(id!, { type: event!.type, actualAt: v.at?.toISOString() ?? null, reason: v.reason }),
    onSuccess: () => { setEvent(null); form.resetFields(); refresh() }, onError: fail,
  })
  const lr = d?.lr
  const steps = [lr?.cfaReachedAt, lr?.loadingStartedAt, lr?.loadingCompletedAt, lr?.departedAt, lr?.arrivedAt, lr?.unloadedAt]
  return (
    <ResponsiveDrawer open={!!id} onClose={onClose} desktopWidth={760} title={lr ? `LR ${lr.number}` : 'LR'} loading={q.isLoading}>
      {d && lr && (
        <>
          <Space wrap style={{ marginBottom: 12 }}>
            <StatusTag value={lr.status} /><span>POD:</span><StatusTag value={lr.podStatus} />
            {can('ops.dispatch') && lr.status !== 'Cancelled' && <Button onClick={() => nav(`/ops/lrs/${lr.id}/print`)} disabled={!lr.qrActive}>Print LR with QR</Button>}
            {can('ops.dispatch') && lr.status !== 'Cancelled' && <Popconfirm title="Issue a new QR code?" description="The printed code stops working at once." onConfirm={() => reissue.mutate()}><Button>New QR code</Button></Popconfirm>}
            {can('ops.dispatch') && lr.qrActive && <Popconfirm title="Revoke the QR code?" onConfirm={() => revoke.mutate()}><Button danger>Revoke QR</Button></Popconfirm>}
            {can('ops.dispatch') && ['Created', 'Assigned', 'AtCfa', 'Loading', 'Loaded'].includes(lr.status) && <Popconfirm title="Cancel this LR?" onConfirm={() => cancel.mutate()}><Button danger>Cancel LR</Button></Popconfirm>}
          </Space>
          {!lr.qrActive && lr.status !== 'Cancelled' && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="The QR code on this LR is not active (revoked or expired). Issue a new code and reprint." />}
          <Descriptions size="small" bordered column={{ xs: 1, md: 2 }}>
            <Descriptions.Item label="Customer">{lr.customerName}</Descriptions.Item><Descriptions.Item label="Customer PO">{lr.customerReference ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="From">{lr.cfaName}</Descriptions.Item><Descriptions.Item label="To">{lr.destinationName}, {lr.destinationCity}</Descriptions.Item>
            <Descriptions.Item label="Vehicle">{lr.vehicleRegistration ?? '—'}</Descriptions.Item><Descriptions.Item label="Trip">{lr.tripNumber ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Delivery date">{day(lr.deliveryDate)}</Descriptions.Item><Descriptions.Item label="QR valid until">{when(d.qrExpiresAt)} · printed {d.printCount}×</Descriptions.Item>
            <Descriptions.Item label="Indent no.">{d.indentNo ?? '—'}</Descriptions.Item><Descriptions.Item label="Instructions">{d.instructions ?? '—'}</Descriptions.Item>
          </Descriptions>
          <Table scroll={{ x: 'max-content' }} size="small" style={{ marginTop: 12 }} pagination={false} rowKey={(l) => `${l.materialName}${l.quantity}${l.weightKg}`} dataSource={d.lines}
            columns={lineColumns} />
          <Divider titlePlacement="left">Invoice copies</Divider>
          <List size="small" dataSource={d.files} locale={{ emptyText: 'No invoice copies kept' }}
            renderItem={(f) => <List.Item><a onClick={() => void openFile(() => opsApi.requestFile(f.id), f.fileName)}>{f.fileName}</a></List.Item>} />
          <Divider titlePlacement="left">Progress</Divider>
          <Steps size="small" orientation={mobile ? 'vertical' : 'horizontal'} current={steps.filter(Boolean).length} items={['At CFA', 'Loading', 'Loaded', 'Left CFA', 'Arrived', 'Unloaded'].map((t, i) => ({ title: t, description: steps[i] ? when(steps[i]) : undefined }))} />
          {lr.loadingStartedAt && lr.loadingCompletedAt && <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>Loading took {mins((new Date(lr.loadingCompletedAt).getTime() - new Date(lr.loadingStartedAt).getTime()) / 60000)}.</Typography.Paragraph>}
          {lr.arrivedAt && lr.unloadedAt && <Typography.Paragraph type="secondary">Waited {mins((new Date(lr.unloadedAt).getTime() - new Date(lr.arrivedAt).getTime()) / 60000)} at the destination (arrival to unloading).</Typography.Paragraph>}
          {can('ops.dispatch') && (
            <Space style={{ margin: '8px 0' }} wrap>
              {lr.status === 'InTransit' && can('ops.arrival.override') && <Button onClick={() => setEvent({ type: 'DestinationArrival', correct: false })}>Record arrival (supervisor)</Button>}
              {lr.status === 'Arrived' && <Button onClick={() => setEvent({ type: 'UnloadingCompleted', correct: false })}>Record unloading completed</Button>}
              {lr.arrivedAt && <Button onClick={() => setEvent({ type: 'DestinationArrival', correct: true })}>Correct arrival time</Button>}
              {lr.unloadedAt && <Button onClick={() => setEvent({ type: 'UnloadingCompleted', correct: true })}>Correct unloading time</Button>}
            </Space>
          )}
          <Divider titlePlacement="left">History</Divider>
          <EventTimeline events={d.events} />
          <Modal open={event !== null} title={event ? `${event.correct ? 'Correct' : 'Record'}: ${eventLabel[event.type as EventType]}` : ''} onCancel={() => setEvent(null)} onOk={() => form.submit()} confirmLoading={record.isPending} destroyOnHidden okText="Save">
            <Form form={form} layout="vertical" onFinish={(v) => record.mutate(v)} initialValues={{ at: dayjs() }}>
              {event?.type === 'DestinationArrival' && !event.correct && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="This replaces the driver's GPS check. Use it only when the driver could not confirm arrival; the reason is kept." />}
              <Form.Item name="at" label="When it happened" rules={[{ required: true }]}><DatePicker showTime format="DD MMM YYYY HH:mm" style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="reason" label="Reason" rules={[{ required: true, message: 'A reason is required' }]}><Input.TextArea rows={2} /></Form.Item>
            </Form>
          </Modal>
        </>
      )}
    </ResponsiveDrawer>
  )
}

const maxInvoiceFiles = 10

function CreateLrDrawer({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: string) => void }) {
  const mobile = useIsMobile()
  const customers = useCustomerOptions()
  const cfas = useLocationOptions('Cfa')
  const places = useLocationOptions('Distributor')
  const uoms = useUomOptions()
  const [form] = Form.useForm()
  const [copies, setCopies] = useState<File[]>([])
  const blankLine = { packages: 1, unit: uoms.find((u) => u.value === 'PKG')?.value ?? uoms[0]?.value, weightKg: 0 }
  const save = useMutation({
    mutationFn: async (v: Record<string, unknown>) => {
      const lines = (v.lines as { packages: number; unit: string; weightKg: number; netWeightKg?: number | null; volumeCuFt?: number | null }[]).map((l) => ({
        quantity: l.packages, packages: l.packages, unit: l.unit, weightKg: l.weightKg, netWeightKg: l.netWeightKg ?? null, volumeCuFt: l.volumeCuFt ?? null,
      }))
      const created = await opsApi.createLr({ ...(v as object), lines, deliveryDate: (v.deliveryDate as dayjs.Dayjs | undefined)?.format('YYYY-MM-DD') ?? null } as never)
      const failed: string[] = []
      for (const file of copies) {
        try { await opsApi.attachLrFile(created.lr.lr.id, file) } catch { failed.push(file.name) }
      }
      return { created, failed }
    },
    onSuccess: ({ created, failed }) => {
      message.success(`LR ${created.lr.lr.number} created`)
      if (failed.length > 0) message.warning(`The LR was created, but these invoice copies were not saved: ${failed.join(', ')}. Add them again from the LR.`)
      onCreated(created.lr.lr.id)
    }, onError: (e) => message.error(toApiError(e).message),
  })
  return (
    <ResponsiveDrawer open={open} onClose={onClose} desktopWidth={720} title="New LR" destroyOnHidden afterOpenChange={(o) => { if (o) { form.resetFields(); setCopies([]); form.setFieldsValue({ lines: [blankLine] }) } }}
      footer={<Flex gap={8} justify="flex-end"><Button size={mobile ? 'large' : 'middle'} onClick={onClose}>Cancel</Button><Button size={mobile ? 'large' : 'middle'} type="primary" loading={save.isPending} onClick={() => form.submit()}>Create LR</Button></Flex>}>
      <Form form={form} layout="vertical" size={mobile ? 'large' : 'middle'} onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Form.Item name="customerId" label="Customer" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={customers} /></Form.Item>
        <Row gutter={12}>
          <Col xs={24} md={12}><Form.Item name="customerReference" label="Customer PO / reference"><Input /></Form.Item></Col>
          <Col xs={24} md={12}><Form.Item name="indentNo" label="Indent no."><Input maxLength={60} /></Form.Item></Col>
        </Row>
        <Form.Item name="cfaLocationId" label="From (CFA)" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={cfas} /></Form.Item>
        <Form.Item name="destinationLocationId" label="To (distributor)" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={places} /></Form.Item>
        <Form.Item name="deliveryDate" label="Delivery date"><DatePicker format="DD MMM YYYY" style={{ width: mobile ? '100%' : undefined }} /></Form.Item>
        <Form.Item name="instructions" label="Instructions"><Input.TextArea rows={2} /></Form.Item>
        <Form.List name="lines">
          {(fields, { add, remove }) => (
            <>
              {fields.map((f, index) => (
                <Card key={f.key} size="small" style={{ marginBottom: 12 }} title={fields.length > 1 ? `Line ${index + 1}` : undefined} extra={fields.length > 1 ? <Button size="small" danger onClick={() => remove(f.name)}>Remove</Button> : undefined}>
                  <Row gutter={12}>
                    <Col xs={12} md={6}><Form.Item name={[f.name, 'packages']} label="No. of packages" rules={[{ required: true }]}><InputNumber min={1} precision={0} inputMode="numeric" style={{ width: '100%' }} /></Form.Item></Col>
                    <Col xs={12} md={6}><Form.Item name={[f.name, 'unit']} label="Unit" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={uoms} /></Form.Item></Col>
                    <Col xs={12} md={6}><Form.Item name={[f.name, 'weightKg']} label="Gross weight (kg)" rules={[{ required: true }, { type: 'number', min: 0.001, message: 'Enter the weight' }]}><InputNumber min={0} inputMode="decimal" style={{ width: '100%' }} /></Form.Item></Col>
                    <Col xs={12} md={6}>
                      <Form.Item name={[f.name, 'netWeightKg']} label="Net weight (kg)" dependencies={[['lines', f.name, 'weightKg']]}
                        rules={[({ getFieldValue }) => ({ validator: (_, v) => (v == null || v <= (getFieldValue(['lines', f.name, 'weightKg']) ?? 0) ? Promise.resolve() : Promise.reject(new Error('More than gross weight'))) })]}>
                        <InputNumber min={0} inputMode="decimal" style={{ width: '100%' }} />
                      </Form.Item>
                    </Col>
                    <Col xs={12} md={6}><Form.Item name={[f.name, 'volumeCuFt']} label="Vol. cu. ft."><InputNumber min={0} inputMode="decimal" style={{ width: '100%' }} /></Form.Item></Col>
                  </Row>
                </Card>
              ))}
              <Button block={mobile} onClick={() => add(blankLine)}>Add line</Button>
            </>
          )}
        </Form.List>
        <Form.Item label={`Invoice copies (up to ${maxInvoiceFiles} photos or PDFs)`} style={{ marginTop: 16 }} extra="Take a photo with the camera or upload. They are saved with the LR once it is created.">
          <PhotoPicker files={copies} onChange={setCopies} max={maxInvoiceFiles} />
        </Form.Item>
      </Form>
    </ResponsiveDrawer>
  )
}

/** The page the office prints and hands to the driver. The QR carries only the address of the driver's page and its secret, nothing about the customer or the goods. */
export function LrPrintPage() {
  const { id = '' } = useParams()
  const nav = useNavigate()
  const q = useQuery({ queryKey: ['ops', 'print', id], queryFn: () => opsApi.printLr(id), retry: false, staleTime: Infinity })
  if (q.isError) return <Alert type="error" showIcon style={{ margin: 24 }} title={toApiError(q.error).message} action={<Button onClick={() => nav(-1)}>Back</Button>} />
  const p = q.data
  if (!p) return null
  const link = `${window.location.origin}${p.qrPath}`
  return (
    <div style={{ maxWidth: 820, margin: '0 auto', padding: 24, background: '#fff' }}>
      <style>{'@media print { .no-print, .ant-layout-sider, .ant-layout-header, .ant-float-btn-group { display: none !important } body, .ant-layout { background: #fff !important } .ant-layout-content { padding: 0 !important; max-width: none !important } @page { margin: 12mm } }'}</style>
      <Space className="no-print" style={{ marginBottom: 16 }}><Button onClick={() => nav(-1)}>Back</Button><Button type="primary" onClick={() => window.print()}>Print</Button></Space>
      <Space align="start" style={{ width: '100%', justifyContent: 'space-between' }}>
        <div>
          <Typography.Title level={2} style={{ margin: 0 }}>Lorry receipt</Typography.Title>
          <Typography.Title level={1} style={{ margin: '0 0 8px' }}>{p.number}</Typography.Title>
          <div>Customer: <b>{p.customerName}</b></div>
          {p.indentNo && <div>Indent no.: <b>{p.indentNo}</b></div>}
          {p.customerReference && <div>PO / reference: <b>{p.customerReference}</b></div>}
          {p.tripNumber && <div>Trip: {p.tripNumber}</div>}
        </div>
        <div style={{ textAlign: 'center' }}>
          <QRCode value={link} size={170} errorLevel="M" />
          <div style={{ fontSize: 12, maxWidth: 170 }}>Driver: scan to confirm arrival, unloading and upload the POD</div>
        </div>
      </Space>
      <Divider />
      <Descriptions column={{ xs: 1, md: 2 }} size="small" bordered>
        <Descriptions.Item label="From">{p.cfaName}<br />{p.cfaAddress}</Descriptions.Item>
        <Descriptions.Item label="To">{p.destinationName}<br />{p.destinationAddress}</Descriptions.Item>
        <Descriptions.Item label="Vehicle">{p.vehicleRegistration ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="Driver">{p.driverName ?? '—'} {p.driverPhone ?? ''}</Descriptions.Item>
        <Descriptions.Item label="Delivery date">{day(p.deliveryDate)}</Descriptions.Item>
        <Descriptions.Item label="Instructions">{p.instructions ?? '—'}</Descriptions.Item>
      </Descriptions>
      <Table scroll={{ x: 'max-content' }} size="small" style={{ marginTop: 16 }} pagination={false} rowKey={(l) => `${l.materialName}${l.quantity}${l.weightKg}`} dataSource={p.lines}
        columns={lineColumns}
        footer={() => <b>Total: {kg(p.totalWeightKg)}, {p.totalPackages} packages</b>} />
      <Divider />
      <Space size={80}><div style={{ marginTop: 40 }}>Receiver's signature and stamp</div><div style={{ marginTop: 40 }}>Driver's signature</div></Space>
    </div>
  )
}

// ---------------------------------------------------------------- trips and the CFA timeline
const cfaSteps: { type: EventType; label: string }[] = [
  { type: 'CfaReached', label: 'Vehicle reached CFA' }, { type: 'LoadingStarted', label: 'Loading started' }, { type: 'LoadingCompleted', label: 'Loading completed' }, { type: 'CfaDeparted', label: 'Vehicle departed' },
]

export function TripsPage() {
  const mobile = useIsMobile()
  const { can } = useAuth()
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [page, setPage] = useState(1)
  const [creating, setCreating] = useState(new URLSearchParams(window.location.search).get('new') === '1')
  const [viewing, setViewing] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['ops', 'trips', q, page], queryFn: () => opsApi.trips({ search: q || undefined, page, pageSize: 25 }) })
  return (
    <>
      <PageHeader title="Trips and delivery timeline" description="A vehicle's journey from the CFA to one or more distributors. Record the CFA events here; destination events come from the driver's QR code."
        actions={can('ops.dispatch') && <Button type="primary" onClick={() => setCreating(true)}>Plan a trip</Button>} />
      <Card>
        <Space style={{ marginBottom: 12, width: mobile ? '100%' : undefined }}><Input.Search allowClear placeholder="Trip, vehicle or driver" style={{ width: mobile ? '100%' : 280 }} onChange={(e) => { setSearch(e.target.value); setPage(1) }} /></Space>
        {mobile ? (
          <List loading={list.isLoading} dataSource={list.data?.items} locale={{ emptyText: 'No trips' }} pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false, simple: true }}
            renderItem={(r) => (
              <List.Item onClick={() => setViewing(r.id)} style={{ cursor: 'pointer' }}>
                <Flex vertical gap={2} style={{ width: '100%' }}>
                  <Flex justify="space-between" align="center"><Typography.Text strong>{r.number}</Typography.Text><StatusTag value={r.status} /></Flex>
                  <div>{r.vehicleRegistration}{r.driverName ? ` · ${r.driverName}` : ''}</div>
                  <Typography.Text type="secondary">{r.cfaName}</Typography.Text>
                  <span>{r.stopCount} stop(s) · {r.lrCount} LR(s){r.departedAt ? ` · left ${when(r.departedAt)}` : ''}</span>
                </Flex>
              </List.Item>
            )} />
        ) : (
        <Table<TripSummaryDto> rowKey="id" loading={list.isLoading} dataSource={list.data?.items} scroll={{ x: 1000 }} pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false }}
          columns={[
            { title: 'Trip', dataIndex: 'number', render: (v, r) => <a onClick={() => setViewing(r.id)}>{v}</a> }, { title: 'CFA', dataIndex: 'cfaName' }, { title: 'Vehicle', dataIndex: 'vehicleRegistration' },
            { title: 'Driver', dataIndex: 'driverName', render: (v) => v ?? '—' }, { title: 'Stops', dataIndex: 'stopCount' }, { title: 'LRs', dataIndex: 'lrCount' }, { title: 'Status', dataIndex: 'status', render: (v) => <StatusTag value={v} /> },
            { title: 'Loading', dataIndex: 'loadingMinutes', render: mins }, { title: 'At CFA', dataIndex: 'cfaTurnaroundMinutes', render: mins }, { title: 'Departed', dataIndex: 'departedAt', render: when },
          ]} />
        )}
      </Card>
      <TripDrawer id={viewing} onClose={() => setViewing(null)} onChanged={() => void qc.invalidateQueries({ queryKey: ['ops'] })} />
      <PlanTripDrawer open={creating} onClose={() => setCreating(false)} onCreated={(id) => { setCreating(false); void qc.invalidateQueries({ queryKey: ['ops'] }); setViewing(id) }} />
    </>
  )
}

function TripDrawer({ id, onClose, onChanged }: { id: string | null; onClose: () => void; onChanged: () => void }) {
  const mobile = useIsMobile()
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['ops', 'trip', id], queryFn: () => opsApi.trip(id!), enabled: !!id })
  const t = q.data
  const refresh = () => { void q.refetch(); onChanged() }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const [modal, setModal] = useState<{ type: EventType; correct: boolean } | null>(null)
  const [form] = Form.useForm<{ at: dayjs.Dayjs; remarks?: string }>()
  const run = useMutation({
    mutationFn: (v: { at: dayjs.Dayjs; remarks?: string }) => modal!.correct
      ? opsApi.correctTripEvent(id!, { type: modal!.type, newActualAt: v.at.toISOString(), reason: v.remarks ?? '' })
      : opsApi.tripEvent(id!, { type: modal!.type, actualAt: v.at.toISOString(), remarks: v.remarks ?? null }),
    onSuccess: () => { setModal(null); refresh() }, onError: fail,
  })
  const cancel = useMutation({ mutationFn: () => opsApi.cancelTrip(id!), onSuccess: refresh, onError: fail })
  const times: Record<string, string | null> = t ? { CfaReached: t.trip.cfaReachedAt, LoadingStarted: t.trip.loadingStartedAt, LoadingCompleted: t.trip.loadingCompletedAt, CfaDeparted: t.trip.departedAt } : {}
  const next = cfaSteps.find((s) => !times[s.type])
  return (
    <ResponsiveDrawer open={!!id} onClose={onClose} desktopWidth={820} title={t ? `Trip ${t.trip.number}` : 'Trip'} loading={q.isLoading}>
      {t && (
        <>
          <Space wrap style={{ marginBottom: 12 }}>
            <StatusTag value={t.trip.status} />
            {can('ops.dispatch') && next && t.trip.status !== 'Cancelled' && <Button type="primary" onClick={() => { setModal({ type: next.type, correct: false }); form.setFieldsValue({ at: dayjs() }) }}>Record: {next.label}</Button>}
            {can('ops.dispatch') && !t.trip.departedAt && t.trip.status !== 'Cancelled' && <Popconfirm title="Cancel this trip?" description="Its LRs become free again." onConfirm={() => cancel.mutate()}><Button danger>Cancel trip</Button></Popconfirm>}
          </Space>
          <Descriptions size="small" bordered column={{ xs: 1, md: 2 }}>
            <Descriptions.Item label="CFA">{t.trip.cfaName}</Descriptions.Item><Descriptions.Item label="Vehicle">{t.trip.vehicleRegistration}</Descriptions.Item>
            <Descriptions.Item label="Driver">{t.trip.driverName ?? '—'} {t.trip.driverPhone ?? ''}</Descriptions.Item><Descriptions.Item label="Planned">{day(t.trip.plannedDate)}</Descriptions.Item>
            <Descriptions.Item label="Loading took">{mins(t.trip.loadingMinutes)}</Descriptions.Item><Descriptions.Item label="Time at CFA">{mins(t.trip.cfaTurnaroundMinutes)}</Descriptions.Item>
          </Descriptions>
          <Divider titlePlacement="left">At the CFA</Divider>
          <Steps size="small" orientation={mobile ? 'vertical' : 'horizontal'} current={cfaSteps.filter((s) => times[s.type]).length} items={cfaSteps.map((s) => ({ title: s.label, description: (
            <span>{when(times[s.type])}{times[s.type] && can('ops.dispatch') && <a style={{ marginLeft: 6 }} onClick={() => { setModal({ type: s.type, correct: true }); form.setFieldsValue({ at: dayjs(times[s.type]!) }) }}>correct</a>}</span>
          ) }))} />
          <Divider titlePlacement="left">Stops</Divider>
          {t.stops.map((s) => (
            <Card key={s.id} size="small" style={{ marginBottom: 8 }} title={`Stop ${s.sequence}: ${s.destinationName}, ${s.destinationCity}`} extra={<span>arrived {when(s.arrivedAt)} · unloaded {when(s.unloadedAt)}</span>}>
              {s.lrs.map((l) => <div key={l.id}>{l.number} · {l.customerReference ?? 'no PO'} · {kg(l.totalWeightKg)} · <StatusTag value={l.status} /> · POD <StatusTag value={l.podStatus} /></div>)}
            </Card>
          ))}
          <Divider titlePlacement="left">Timeline</Divider>
          <EventTimeline events={t.events} />
          <Modal open={modal !== null} title={modal ? `${modal.correct ? 'Correct' : 'Record'}: ${eventLabel[modal.type]}` : ''} onCancel={() => setModal(null)} onOk={() => form.submit()} confirmLoading={run.isPending} destroyOnHidden okText="Save">
            <Form form={form} layout="vertical" onFinish={(v) => run.mutate(v)}>
              <Form.Item name="at" label="When it happened" rules={[{ required: true }]}><DatePicker showTime format="DD MMM YYYY HH:mm" style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="remarks" label={modal?.correct ? 'Reason for the correction' : 'Remarks'} rules={modal?.correct ? [{ required: true, message: 'A reason is required' }] : []}><Input.TextArea rows={2} /></Form.Item>
            </Form>
          </Modal>
        </>
      )}
    </ResponsiveDrawer>
  )
}

function PlanTripDrawer({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: string) => void }) {
  const mobile = useIsMobile()
  const cfas = useLocationOptions('Cfa')
  const [cfa, setCfa] = useState<string>()
  const free = useQuery({ queryKey: ['ops', 'lrs', 'free', cfa], queryFn: () => opsApi.lrs({ status: 'Created', pageSize: 100 }), enabled: open })
  const vehicles = useQuery({ queryKey: ['ops', 'vehicles'], queryFn: opsApi.vehicles, enabled: open, retry: false })
  const [form] = Form.useForm()
  const save = useMutation({
    mutationFn: (v: Record<string, unknown>) => opsApi.createTrip({ cfaLocationId: v.cfaLocationId as string, vehicleId: (v.vehicleId as string) ?? null, vehicleRegistration: (v.vehicleRegistration as string) ?? null, transporterId: null,
      driverName: (v.driverName as string) ?? null, driverPhone: (v.driverPhone as string) ?? null, plannedDate: (v.plannedDate as dayjs.Dayjs | undefined)?.format('YYYY-MM-DD') ?? null, remarks: (v.remarks as string) ?? null, lrIds: v.lrIds as string[] }),
    onSuccess: (t) => { message.success(`Trip ${t.trip.number} created`); onCreated(t.trip.id) }, onError: (e) => message.error(toApiError(e).message),
  })
  return (
    <ResponsiveDrawer open={open} onClose={onClose} desktopWidth={600} title="Plan a trip" destroyOnHidden afterOpenChange={(o) => { if (o) { form.resetFields(); setCfa(undefined) } }}
      footer={<Flex gap={8} justify="flex-end"><Button size={mobile ? 'large' : 'middle'} onClick={onClose}>Cancel</Button><Button size={mobile ? 'large' : 'middle'} type="primary" loading={save.isPending} onClick={() => form.submit()}>Create trip</Button></Flex>}>
      <Form form={form} layout="vertical" size={mobile ? 'large' : 'middle'} onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Form.Item name="cfaLocationId" label="Loads at (CFA)" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={cfas} onChange={(v) => { setCfa(v); form.setFieldValue('lrIds', []) }} /></Form.Item>
        <Form.Item name="lrIds" label="LRs on this trip" rules={[{ required: true, message: 'Choose at least one LR' }]} extra="LRs for the same distributor share one stop; different distributors become separate stops.">
          <Select mode="multiple" optionFilterProp="label" options={(free.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.number} → ${l.destinationName} (${kg(l.totalWeightKg)})` }))} />
        </Form.Item>
        {vehicles.data ? (
          <Form.Item name="vehicleId" label="Vehicle (from your fleet)"><Select allowClear showSearch optionFilterProp="label" options={vehicles.data.map((v) => ({ value: v.id, label: `${v.registrationNumber} · ${v.vehicleTypeName}${v.available ? '' : ` — ${v.note}`}`, disabled: !v.available }))} /></Form.Item>
        ) : null}
        <Form.Item name="vehicleRegistration" label={vehicles.data ? 'or registration number' : 'Vehicle registration number'} extra="For example MH12AB1234."><Input maxLength={20} style={{ textTransform: 'uppercase' }} /></Form.Item>
        <Row gutter={12}>
          <Col xs={24} md={14}><Form.Item name="driverName" label="Driver"><Input /></Form.Item></Col>
          <Col xs={24} md={10}><Form.Item name="driverPhone" label="Driver mobile"><Input inputMode="tel" maxLength={15} /></Form.Item></Col>
        </Row>
        <Form.Item name="plannedDate" label="Planned date"><DatePicker format="DD MMM YYYY" style={{ width: mobile ? '100%' : undefined }} /></Form.Item>
        <Form.Item name="remarks" label="Remarks"><Input.TextArea rows={2} /></Form.Item>
      </Form>
    </ResponsiveDrawer>
  )
}

export type { LrDetailDto, TripDetailDto }
