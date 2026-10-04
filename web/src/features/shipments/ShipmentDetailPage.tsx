import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Empty, Flex, Form, Input, InputNumber, Modal, Radio, Row, Select, Skeleton, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { planningApi, shipmentsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { FleetOptionDto, FreightMode, ShipmentDto, ShipmentQuoteDto } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { PodDrawer, RecordDeliveryModal } from './DeliveryPanel'
import { PodStatusTag, ShipmentStatusTag, UtilizationBar, formatKg, modeLabel } from './shared'

type Dialog = 'plan' | 'accept' | 'reassign' | 'reject' | 'cancel' | null

function useShipmentMutation<T>(id: string, fn: (v: T) => Promise<ShipmentDto>, success: string, onDone?: () => void) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: async (shipment) => {
      queryClient.setQueryData(queryKeys.shipments.detail(id), shipment)
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all })
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
      void message.success(success)
      onDone?.()
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (error.code === 'concurrency.conflict') await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.detail(id) })
      void message.error(error.message)
    },
  })
}

function QuotesCard({ shipment }: { shipment: ShipmentDto }) {
  const id = shipment.summary.id
  const quotes = useQuery({ queryKey: queryKeys.shipments.quotes(id), queryFn: () => shipmentsApi.quotes(id) })
  const [choosing, setChoosing] = useState<ShipmentQuoteDto | null>(null)
  const [reason, setReason] = useState('')
  const tender = useShipmentMutation(
    id,
    (v: { quote: ShipmentQuoteDto; why: string | null }) => shipmentsApi.tender(id, v.quote.contractId, v.why),
    'Offered to the transporter',
    () => { setChoosing(null); setReason('') },
  )

  const needsReason = choosing !== null && !choosing.isCheapest
  return (
    <Card title="Choose a transporter" extra={<Typography.Text type="secondary">Priced on the pickup date from active contracts</Typography.Text>}>
      {quotes.isError && <Alert type="error" showIcon title={quotes.error.message} />}
      {quotes.data?.quotes.length === 0 && <Empty description={quotes.data.message ?? 'No contract can carry this load.'} />}
      <Table<ShipmentQuoteDto>
        rowKey="contractId"
        loading={quotes.isLoading}
        pagination={false}
        dataSource={quotes.data?.quotes}
        scroll={{ x: 'max-content' }}
        expandable={{
          expandedRowRender: (q) => (
            <Table size="small" pagination={false} rowKey="code" dataSource={q.lines}
              columns={[{ title: 'Charge', dataIndex: 'description' }, { title: 'Amount', dataIndex: 'amount', align: 'right', render: formatInrExact }]} />
          ),
        }}
        columns={[
          { title: 'Transporter', key: 't', render: (_, q) => <><Typography.Text strong>{q.transporterName}</Typography.Text> {q.isCheapest && <Tag color="green">Lowest</Tag>}<br /><Typography.Text type="secondary">{q.contractReference}</Typography.Text></> },
          { title: 'Freight', dataIndex: 'total', align: 'right', render: formatInrExact },
          { title: '', key: 'a', align: 'right', render: (_, q) => <Button type={q.isCheapest ? 'primary' : 'default'} onClick={() => setChoosing(q)}>Offer this load</Button> },
        ]}
      />
      <Modal
        open={choosing !== null}
        title={choosing ? `Offer ${shipment.summary.number} to ${choosing.transporterName}?` : ''}
        okText="Offer load"
        okButtonProps={{ disabled: needsReason && reason.trim().length === 0 }}
        confirmLoading={tender.isPending}
        onOk={() => choosing && tender.mutate({ quote: choosing, why: needsReason ? reason.trim() : null })}
        onCancel={() => { setChoosing(null); setReason('') }}
        destroyOnHidden
      >
        {choosing && <p>Freight {formatInrExact(choosing.total)}. The transporter is asked to accept with a vehicle and driver.</p>}
        {needsReason && (
          <>
            <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="This is not the lowest quote." />
            <Input.TextArea rows={3} maxLength={500} aria-label="Reason for not choosing the lowest quote" placeholder="Why not the lowest quote? (kept on the shipment for audit)" value={reason} onChange={(e) => setReason(e.target.value)} />
          </>
        )}
      </Modal>
    </Card>
  )
}

function FleetModal({ shipment, mode, onClose }: { shipment: ShipmentDto; mode: 'accept' | 'reassign'; onClose: () => void }) {
  const id = shipment.summary.id
  const options = useQuery({ queryKey: queryKeys.shipments.fleet(id), queryFn: () => shipmentsApi.fleetOptions(id) })
  const [vehicleId, setVehicleId] = useState<string>()
  const [driverId, setDriverId] = useState<string>()
  const save = useShipmentMutation(
    id,
    (v: { vehicleId: string; driverId: string }) => (mode === 'accept' ? shipmentsApi.accept(id, v.vehicleId, v.driverId) : shipmentsApi.reassign(id, v.vehicleId, v.driverId)),
    mode === 'accept' ? 'Load accepted' : 'Vehicle and driver changed',
    onClose,
  )

  const toOption = (o: FleetOptionDto) => ({
    value: o.id,
    disabled: !o.isOk,
    label: `${o.label}${o.issues.length ? ` — ${o.issues.join('; ')}` : ''}`,
  })

  return (
    <Modal
      open
      title={mode === 'accept' ? `Accept ${shipment.summary.number}` : 'Change vehicle or driver'}
      okText={mode === 'accept' ? 'Accept load' : 'Save'}
      okButtonProps={{ disabled: !vehicleId || !driverId }}
      confirmLoading={save.isPending}
      onOk={() => vehicleId && driverId && save.mutate({ vehicleId, driverId })}
      onCancel={onClose}
      destroyOnHidden
    >
      <p>
        {formatKg(shipment.summary.totalWeightKg)} from {shipment.summary.lane}. Vehicles and drivers with lapsed papers, or too small for the load, cannot be chosen.
      </p>
      {options.isError && <Alert type="error" showIcon title={options.error.message} />}
      <Flex vertical gap={12}>
        <Select aria-label="Vehicle" placeholder="Vehicle" virtual={false} loading={options.isLoading} options={options.data?.vehicles.map(toOption)} value={vehicleId} onChange={setVehicleId} />
        <Select aria-label="Driver" placeholder="Driver" virtual={false} loading={options.isLoading} options={options.data?.drivers.map(toOption)} value={driverId} onChange={setDriverId} />
      </Flex>
    </Modal>
  )
}

interface PlanValues {
  mode: FreightMode
  vehicleTypeId?: string
  pickup: Dayjs
  distanceKm?: number | null
}

/** Change how a draft will move (full/part load, vehicle type, date). Quotes are re-priced from this. */
function PlanModal({ shipment, onClose }: { shipment: ShipmentDto; onClose: () => void }) {
  const id = shipment.summary.id
  const [form] = Form.useForm<PlanValues>()
  const types = useQuery({ queryKey: queryKeys.shipments.vehicleTypes, queryFn: () => planningApi.vehicleTypes() })
  const save = useShipmentMutation(
    id,
    (v: PlanValues) => shipmentsApi.updatePlan(id, { mode: v.mode, vehicleTypeId: v.mode === 'Ftl' ? (v.vehicleTypeId ?? null) : null, plannedPickupDate: v.pickup.format('YYYY-MM-DD'), distanceKm: v.distanceKm ?? null }),
    'Plan updated',
    onClose,
  )
  const queryClient = useQueryClient()
  const mode = Form.useWatch('mode', form) as FreightMode | undefined

  return (
    <Modal
      open
      title="Edit plan"
      okText="Save plan"
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
      afterClose={() => void queryClient.invalidateQueries({ queryKey: queryKeys.shipments.quotes(id) })}
    >
      <Form<PlanValues>
        form={form}
        layout="vertical"
        initialValues={{ mode: shipment.summary.mode, vehicleTypeId: shipment.vehicleTypeId ?? undefined, pickup: dayjs(shipment.summary.plannedPickupDate), distanceKm: shipment.distanceKm }}
        onFinish={(v) => save.mutate(v)}
      >
        <Form.Item name="mode" label="Mode">
          <Radio.Group optionType="button" options={[{ value: 'Ftl', label: modeLabel.Ftl }, { value: 'Ptl', label: modeLabel.Ptl }]} />
        </Form.Item>
        {mode === 'Ftl' && (
          <Form.Item name="vehicleTypeId" label="Vehicle type">
            <Select allowClear aria-label="Vehicle type" placeholder="Any vehicle" virtual={false} loading={types.isLoading} options={types.data?.map((t) => ({ value: t.id, label: `${t.name}` }))} />
          </Form.Item>
        )}
        <Form.Item name="pickup" label="Pickup date" rules={[{ required: true, message: 'Choose a date' }]}>
          <DatePicker style={{ width: '100%' }} format="DD MMM YYYY" />
        </Form.Item>
        <Form.Item name="distanceKm" label="Distance (km)" extra="Needed only when a contract prices by the kilometre.">
          <InputNumber min={1} precision={0} style={{ width: '100%' }} controls={false} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function ReasonModal({ title, okText, label, danger, onOk, onClose, pending }: { title: string; okText: string; label: string; danger?: boolean; onOk: (reason: string) => void; onClose: () => void; pending: boolean }) {
  const [reason, setReason] = useState('')
  return (
    <Modal open title={title} okText={okText} okButtonProps={{ danger, disabled: reason.trim().length === 0 }} confirmLoading={pending} onOk={() => onOk(reason.trim())} onCancel={onClose} destroyOnHidden>
      <Input.TextArea rows={3} maxLength={500} aria-label={label} placeholder={label} value={reason} onChange={(e) => setReason(e.target.value)} />
    </Modal>
  )
}

export function ShipmentDetailPage() {
  const { id = '' } = useParams()
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const canPlan = !isVendor && can('shipments.plan')
  const canRespond = isVendor ? can('shipments.respond') : canPlan
  const [dialog, setDialog] = useState<Dialog>(null)
  const queryClient = useQueryClient()
  const [recording, setRecording] = useState<ShipmentDto['orders'][number] | null>(null)
  const [proofFor, setProofFor] = useState<string | null>(null)
  const close = () => setDialog(null)

  const query = useQuery({ queryKey: queryKeys.shipments.detail(id), queryFn: () => shipmentsApi.get(id) })
  const reject = useShipmentMutation(id, (reason: string) => shipmentsApi.reject(id, reason), 'Load declined', close)
  const cancel = useShipmentMutation(id, (reason: string) => shipmentsApi.cancel(id, reason), 'Shipment cancelled', close)
  const withdraw = useShipmentMutation(id, () => shipmentsApi.withdraw(id), 'Offer withdrawn')
  const dispatch = useShipmentMutation(id, () => shipmentsApi.dispatch(id), 'Shipment dispatched, lorry receipts issued')
  const deliver = useShipmentMutation(id, () => shipmentsApi.deliver(id), 'Marked delivered')
  const sequence = useShipmentMutation(id, (orderIds: string[]) => shipmentsApi.sequence(id, orderIds), 'Drop order updated')
  const removeOrder = useShipmentMutation(id, (orderId: string) => shipmentsApi.removeOrder(id, orderId), 'Order taken off the shipment')

  if (query.isLoading) return <Skeleton active />
  if (query.isError) return <Alert type="error" showIcon title={query.error.message} action={<Link to="/shipments">Back to shipments</Link>} />
  const shipment = query.data
  if (!shipment) return null

  const s = shipment.summary
  const draft = s.status === 'Draft'
  const move = (index: number, by: -1 | 1) => {
    const ids = shipment.orders.map((o) => o.orderId)
    const target = index + by
    ;[ids[index], ids[target]] = [ids[target]!, ids[index]!]
    sequence.mutate(ids)
  }

  return (
    <>
      <PageHeader
        title={s.number}
        description={s.lane}
        actions={
          <>
            <ShipmentStatusTag status={s.status} />
            {canPlan && draft && <Button onClick={() => setDialog('plan')}>Edit plan</Button>}
            {canPlan && s.status === 'Tendered' && <Button loading={withdraw.isPending} onClick={() => withdraw.mutate(undefined)}>Withdraw offer</Button>}
            {canRespond && s.status === 'Tendered' && (
              <>
                <Button danger onClick={() => setDialog('reject')}>Decline</Button>
                <Button type="primary" onClick={() => setDialog('accept')}>Accept…</Button>
              </>
            )}
            {canRespond && s.status === 'Accepted' && <Button onClick={() => setDialog('reassign')}>Change vehicle / driver</Button>}
            {canPlan && s.status === 'Accepted' && <Button type="primary" loading={dispatch.isPending} onClick={() => dispatch.mutate(undefined)}>Dispatch</Button>}
            {canPlan && s.status === 'Dispatched' && <Button type="primary" loading={deliver.isPending} onClick={() => deliver.mutate(undefined)}>Mark delivered</Button>}
            {canPlan && ['Draft', 'Tendered', 'Accepted'].includes(s.status) && <Button danger onClick={() => setDialog('cancel')}>Cancel shipment</Button>}
          </>
        }
      />

      {shipment.rejectionCount > 0 && draft && canPlan && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }} title={`Declined ${shipment.rejectionCount} time(s). Last reason: ${shipment.lastRejectionReason ?? 'none given'}`} />
      )}
      {s.status === 'Cancelled' && <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Cancelled: ${shipment.cancelReason ?? ''}`} />}

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          <Flex vertical gap={16}>
            <Card title="Orders in drop sequence">
              <Table
                rowKey="orderId"
                pagination={false}
                dataSource={shipment.orders}
                scroll={{ x: 'max-content' }}
                columns={[
                  { title: '#', dataIndex: 'dropSequence', width: 50 },
                  { title: 'Order', key: 'orderNumber', render: (_: unknown, o: ShipmentDto['orders'][number]) => <>{o.orderNumber} {o.isReturn && <Tag color="purple">Return pickup</Tag>}</> },
                  { title: 'Deliver to', key: 'drop', render: (_: unknown, o: ShipmentDto['orders'][number]) => (o.isReturn ? `Collect from ${o.pickup.name}, ${o.pickup.city}` : `${o.drop.name}, ${o.drop.city}, ${o.drop.state}`) },
                  { title: 'Goods', dataIndex: 'description', responsive: ['md'] },
                  { title: 'Weight', dataIndex: 'weightKg', align: 'right', render: formatKg },
                  { title: 'LR no.', dataIndex: 'lrNumber', render: (v: string | null) => v ?? '—' },
                  ...(['Dispatched', 'Delivered'].includes(s.status)
                    ? [{
                        title: 'Delivery',
                        key: 'delivery',
                        render: (_: unknown, o: ShipmentDto['orders'][number]) =>
                          o.deliveredAt ? (
                            <Flex vertical gap={4}>
                              <span>{formatDateTime(o.deliveredAt)}{o.receiverName ? ` · ${o.receiverName}` : ''}</span>
                              <Flex gap={4} wrap>
                                {(o.shortagePackages ?? 0) > 0 && <Tag color="red">{o.shortagePackages} short</Tag>}
                                {(o.damagedPackages ?? 0) > 0 && <Tag color="orange">{o.damagedPackages} damaged</Tag>}
                                <PodStatusTag status={o.podStatus ?? 'Awaiting'} />
                                <Button size="small" onClick={() => setProofFor(o.orderId)}>Proof{o.podDocuments ? ` (${o.podDocuments})` : ''}</Button>
                              </Flex>
                            </Flex>
                          ) : canRespond && s.status === 'Dispatched' ? (
                            <Button size="small" type="primary" onClick={() => setRecording(o)}>Record delivery</Button>
                          ) : '—',
                      }]
                    : []),
                  ...(draft && canPlan
                    ? [{
                        title: '', key: 'edit', align: 'right' as const,
                        render: (_: unknown, o: ShipmentDto['orders'][number], index: number) => (
                          <Flex gap={4} justify="flex-end">
                            <Button size="small" aria-label={`Move ${o.orderNumber} up`} icon={<ArrowUpOutlined />} disabled={index === 0 || sequence.isPending} onClick={() => move(index, -1)} />
                            <Button size="small" aria-label={`Move ${o.orderNumber} down`} icon={<ArrowDownOutlined />} disabled={index === shipment.orders.length - 1 || sequence.isPending} onClick={() => move(index, 1)} />
                            <Button size="small" danger aria-label={`Remove ${o.orderNumber}`} icon={<DeleteOutlined />} disabled={shipment.orders.length === 1 || removeOrder.isPending} onClick={() => removeOrder.mutate(o.orderId)} />
                          </Flex>
                        ),
                      }]
                    : []),
                ]}
              />
            </Card>
            {draft && canPlan && <QuotesCard shipment={shipment} />}
            {!isVendor && shipment.estimateLines && shipment.estimateLines.length > 0 && (
              <Card title="Freight estimate" extra={<Typography.Text strong>{formatInrExact(s.freightEstimate)}</Typography.Text>}>
                <Table size="small" pagination={false} rowKey="code" dataSource={shipment.estimateLines}
                  columns={[{ title: 'Charge', dataIndex: 'description' }, { title: 'Amount', dataIndex: 'amount', align: 'right', render: formatInrExact }]} />
                {shipment.overrideReason && <Alert style={{ marginTop: 12 }} type="info" showIcon title={`Not the lowest quote: ${shipment.overrideReason}`} />}
              </Card>
            )}
          </Flex>
        </Col>
        <Col xs={24} xl={8}>
          <Flex vertical gap={16}>
            <Card title="Load">
              <Descriptions column={1} size="small">
                <Descriptions.Item label="Mode">{modeLabel[s.mode]}</Descriptions.Item>
                <Descriptions.Item label="Vehicle type">{shipment.vehicleTypeName ?? 'Any'}</Descriptions.Item>
                <Descriptions.Item label="Pickup date">{s.plannedPickupDate}</Descriptions.Item>
                <Descriptions.Item label="Weight">{formatKg(s.totalWeightKg)}</Descriptions.Item>
                {shipment.totalVolumeCbm !== null && <Descriptions.Item label="Volume">{shipment.totalVolumeCbm} CBM</Descriptions.Item>}
                {shipment.distanceKm !== null && <Descriptions.Item label="Distance">{shipment.distanceKm} km</Descriptions.Item>}
              </Descriptions>
            </Card>
            {s.transporterId && (
              <Card title="Transporter">
                <Descriptions column={1} size="small">
                  <Descriptions.Item label="Company">{s.transporterName ?? '—'}</Descriptions.Item>
                  {!isVendor && shipment.contractReference && <Descriptions.Item label="Contract">{shipment.contractReference}</Descriptions.Item>}
                  <Descriptions.Item label="Offered">{formatDateTime(shipment.tenderedAt)}</Descriptions.Item>
                  {shipment.vehicleId && (
                    <>
                      <Descriptions.Item label="Vehicle">{s.vehicleRegistration}</Descriptions.Item>
                      <Descriptions.Item label="Fill"><UtilizationBar value={s.utilization} /></Descriptions.Item>
                      <Descriptions.Item label="Driver">{shipment.driverName} · {shipment.driverPhone}</Descriptions.Item>
                      <Descriptions.Item label="Accepted">{formatDateTime(shipment.acceptedAt)}</Descriptions.Item>
                    </>
                  )}
                  {shipment.dispatchedAt && <Descriptions.Item label="Dispatched">{formatDateTime(shipment.dispatchedAt)}</Descriptions.Item>}
                  {shipment.deliveredAt && <Descriptions.Item label="Delivered">{formatDateTime(shipment.deliveredAt)}</Descriptions.Item>}
                </Descriptions>
              </Card>
            )}
          </Flex>
        </Col>
      </Row>

      {recording && (
        <RecordDeliveryModal
          shipmentId={id}
          line={recording}
          onClose={() => setRecording(null)}
          onDone={(updated) => { queryClient.setQueryData(queryKeys.shipments.detail(id), updated); void queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all }); void queryClient.invalidateQueries({ queryKey: queryKeys.pod.all }); setRecording(null) }}
        />
      )}
      {proofFor && shipment.orders.find((o) => o.orderId === proofFor) && (
        <PodDrawer
          shipmentId={id}
          line={shipment.orders.find((o) => o.orderId === proofFor)!}
          canUpload={canRespond}
          canReview={!isVendor && can('shipments.pod.verify')}
          onClose={() => setProofFor(null)}
          onChanged={(updated) => { if (updated) queryClient.setQueryData(queryKeys.shipments.detail(id), updated); else void queryClient.invalidateQueries({ queryKey: queryKeys.shipments.detail(id) }) }}
        />
      )}
      {dialog === 'plan' && <PlanModal shipment={shipment} onClose={close} />}
      {(dialog === 'accept' || dialog === 'reassign') && <FleetModal shipment={shipment} mode={dialog} onClose={close} />}
      {dialog === 'reject' && <ReasonModal title="Decline this load?" okText="Decline" label="Why are you declining?" danger pending={reject.isPending} onOk={(r) => reject.mutate(r)} onClose={close} />}
      {dialog === 'cancel' && <ReasonModal title="Cancel this shipment?" okText="Cancel shipment" label="Why is it being cancelled?" danger pending={cancel.isPending} onOk={(r) => cancel.mutate(r)} onClose={close} />}
    </>
  )
}
