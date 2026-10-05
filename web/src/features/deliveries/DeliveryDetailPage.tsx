import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Empty, Flex, Input, Modal, Row, Skeleton, Table, Tag, Timeline, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { AuditPanel } from '@/features/audit/AuditPanel'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DeliveryDto, DeliveryEventType } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { DeliveryStatusTag, ExceptionStatusTag, ProofStatusTag, SeverityTag, exceptionTypeLabel, outcomeLabel, qty } from './shared'

const eventLabel: Record<DeliveryEventType, string> = {
  Created: 'Planned', Assigned: 'Assigned to a vehicle', Started: 'Left for the customer', Arrived: 'Arrived', AttemptFailed: 'Attempt failed', Delivered: 'Delivered', PartiallyDelivered: 'Delivered with a discrepancy',
  Failed: 'Could not be delivered', Refused: 'Refused by the customer', Rescheduled: 'Rescheduled', Cancelled: 'Cancelled', Closed: 'Closed', OtpIssued: 'Delivery code sent', OtpVerified: 'Delivery code confirmed',
}

type Dialog = 'cancel' | 'close' | 'reschedule' | null

export function DeliveryDetailPage() {
  const { id = '' } = useParams()
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const canManage = !isVendor && can('deliveries.manage')
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<Dialog>(null)
  const [reason, setReason] = useState('')
  const [when, setWhen] = useState<Dayjs | null>(null)

  const query = useQuery({ queryKey: queryKeys.deliveries.detail(id), queryFn: () => deliveriesApi.get(id) })
  const exceptions = useQuery({ queryKey: queryKeys.deliveries.exceptions({ deliveryId: id, pageSize: 50 }), queryFn: () => deliveriesApi.exceptions({ deliveryId: id, pageSize: 50 }) })

  const billing = useQuery({ queryKey: queryKeys.deliveries.billing(id), queryFn: () => deliveriesApi.billing(id), enabled: !isVendor && can('deliveries.read'), retry: false })
  const claims = useMutation({
    mutationFn: () => deliveriesApi.createClaims(id, null),
    onSuccess: async (made) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
      void message.success(made.length === 0 ? 'Nothing new to claim' : `Sent ${made.length} claim(s) with the evidence: ${made.map((c) => c.reference).join(', ')}`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const act = useMutation({
    mutationFn: async (kind: 'cancel' | 'close' | 'reschedule' | 'pod') => {
      if (kind === 'cancel') return deliveriesApi.cancel(id, reason.trim())
      if (kind === 'close') return deliveriesApi.close(id, reason.trim())
      if (kind === 'reschedule') return deliveriesApi.reschedule(id, when!.toISOString())
      await deliveriesApi.startPod(id)
      return deliveriesApi.get(id)
    },
    onSuccess: async (d) => {
      queryClient.setQueryData(queryKeys.deliveries.detail(id), d)
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
      setDialog(null)
      setReason('')
      void message.success('Saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  if (query.isLoading) return <Skeleton active />
  if (query.isError) return <Alert type="error" showIcon title={query.error.message} action={<Link to="/delivery">Back to deliveries</Link>} />
  const d = query.data as DeliveryDto
  const s = d.summary
  const completed = s.status === 'Delivered' || s.status === 'PartiallyDelivered'

  return (
    <>
      <PageHeader
        title={s.number}
        description={`${s.customerName}${s.destinationReference ? ` · ${s.destinationReference}` : ''}`}
        actions={
          <>
            <DeliveryStatusTag status={s.status} />
            <ProofStatusTag status={s.podStatus} />
            {s.outcome && <Tag>{outcomeLabel[s.outcome]}</Tag>}
            {billing.data && <Tag color={billing.data.invoiceHold ? 'orange' : billing.data.billingEligible ? 'green' : 'default'} title={billing.data.explanation}>{billing.data.invoiceHold ? 'Invoice on hold' : billing.data.billingEligible ? 'Ready to bill' : 'Billing not yet due'}</Tag>}
            {canManage && d.discrepancies.length > 0 && <Button loading={claims.isPending} onClick={() => claims.mutate()}>Create claim</Button>}
            {isVendor && ['Assigned', 'EnRoute', 'Arrived', 'Attempted'].includes(s.status) && <Link to={`/driver/${id}`}><Button type="primary">Open on the phone screen</Button></Link>}
            {canManage && completed && s.podStatus === 'Pending' && <Button loading={act.isPending} onClick={() => act.mutate('pod')}>Start proof of delivery</Button>}
            {canManage && ['Failed', 'Refused', 'Attempted', 'Arrived'].includes(s.status) && <Button onClick={() => setDialog('reschedule')}>Reschedule</Button>}
            {canManage && ['Delivered', 'PartiallyDelivered', 'Failed', 'Refused'].includes(s.status) && <Button onClick={() => setDialog('close')}>Close</Button>}
            {canManage && ['Planned', 'Assigned', 'EnRoute'].includes(s.status) && <Button danger onClick={() => setDialog('cancel')}>Cancel</Button>}
          </>
        }
      />

      {d.hasQuantityMismatch && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="The quantities reported do not add up to what was dispatched. The difference is shown below exactly as reported." />}

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          <Flex vertical gap={16}>
            <Card title="Items">
              <Table
                size="small" pagination={false} rowKey="id" dataSource={d.items} scroll={{ x: 'max-content' }}
                columns={[
                  { title: 'SKU', key: 'sku', render: (_, i) => <><Typography.Text strong>{i.sku}</Typography.Text><br /><Typography.Text type="secondary">{i.description}</Typography.Text></> },
                  { title: 'Ordered', dataIndex: 'orderedQuantity', align: 'right', render: qty },
                  { title: 'Dispatched', dataIndex: 'dispatchedQuantity', align: 'right', render: qty },
                  { title: 'Delivered', dataIndex: 'deliveredQuantity', align: 'right', render: qty },
                  { title: 'Short', dataIndex: 'shortQuantity', align: 'right', render: (v: number, i) => (i.deliveredQuantity == null ? '—' : qty(v)) },
                  { title: 'Damaged', dataIndex: 'damagedQuantity', align: 'right', render: (v: number, i) => (i.deliveredQuantity == null ? '—' : qty(v)) },
                  { title: 'Rejected', dataIndex: 'rejectedQuantity', align: 'right', render: (v: number, i) => (i.deliveredQuantity == null ? '—' : qty(v)) },
                  { title: 'Unaccounted', dataIndex: 'unaccounted', align: 'right', render: (v: number | null) => (v == null ? '—' : v === 0 ? '0' : <Typography.Text type="danger">{v > 0 ? `+${qty(v)}` : qty(v)}</Typography.Text>) },
                  { title: 'Unit', dataIndex: 'unitOfMeasure' },
                ]}
              />
              {d.reconciliation.filter((r) => !r.reconciled).map((r) => (
                <Alert key={r.itemId} type="warning" showIcon style={{ marginTop: 8 }} title={r.problems.join(' ')} />
              ))}
            </Card>

            {d.discrepancies.length > 0 && (
              <Card title="Shortages, damage and goods not accepted">
                <Table
                  size="small" pagination={false} rowKey="id" dataSource={d.discrepancies}
                  columns={[
                    { title: 'Type', dataIndex: 'type' }, { title: 'SKU', dataIndex: 'sku' }, { title: 'Quantity', dataIndex: 'quantity', align: 'right', render: qty },
                    { title: 'Reason', dataIndex: 'reasonCode', render: (v: string | null) => v ?? '—' }, { title: 'Detail', dataIndex: 'description', render: (v: string | null) => v ?? '—' },
                    { title: 'Customer acknowledged', dataIndex: 'customerAcknowledged', render: (v: boolean) => (v ? 'Yes' : 'No') },
                    { title: 'Claim', dataIndex: 'claimReference', render: (v: string | null) => v ?? '—' },
                  ]}
                />
                {d.remainingDisposition && <Typography.Paragraph style={{ marginTop: 8 }}>Goods not delivered are to be handled as: <strong>{d.remainingDisposition}</strong>.</Typography.Paragraph>}
              </Card>
            )}

            {d.attempts.length > 0 && (
              <Card title="Attempts">
                <Table
                  size="small" pagination={false} rowKey="attemptNumber" dataSource={d.attempts}
                  columns={[
                    { title: '#', dataIndex: 'attemptNumber', width: 50 }, { title: 'When', dataIndex: 'attemptedAt', render: formatDateTime }, { title: 'Result', dataIndex: 'result' },
                    { title: 'Reason', dataIndex: 'reasonCode', render: (v: string | null) => v ?? '—' }, { title: 'Driver remarks', dataIndex: 'driverRemarks', render: (v: string | null) => v ?? '—' },
                    { title: 'Customer remarks', dataIndex: 'customerRemarks', render: (v: string | null) => v ?? '—' },
                  ]}
                />
              </Card>
            )}

            <Card title="Exceptions">
              {exceptions.data?.items.length ? (
                <Table
                  size="small" pagination={false} rowKey="id" dataSource={exceptions.data.items}
                  columns={[
                    { title: 'Exception', key: 'e', render: (_, e) => <><Typography.Text strong>{exceptionTypeLabel[e.type]}</Typography.Text><br /><Typography.Text type="secondary">{e.number}</Typography.Text></> },
                    { title: 'Severity', dataIndex: 'severity', render: (v) => <SeverityTag severity={v} /> },
                    { title: 'Status', dataIndex: 'status', render: (v) => <ExceptionStatusTag status={v} /> },
                    { title: 'Raised', dataIndex: 'raisedAt', render: formatDateTime },
                  ]}
                />
              ) : (
                <Empty description="No exceptions" image={Empty.PRESENTED_IMAGE_SIMPLE} />
              )}
              {!isVendor && <Link to={`/delivery/exceptions?deliveryId=${id}`}>Manage exceptions</Link>}
            </Card>
            {!isVendor && (
              <AuditPanel subjects={[
                { entityType: 'Delivery', entityId: id },
                ...(s.podId ? [{ entityType: 'PodRecord', entityId: s.podId }] : []),
                ...(exceptions.data?.items ?? []).map((e) => ({ entityType: 'DeliveryException', entityId: e.id })),
                ...d.discrepancies.map((x) => ({ entityType: 'DeliveryDiscrepancy', entityId: x.id })),
              ]} />
            )}
          </Flex>
        </Col>
        <Col xs={24} xl={8}>
          <Flex vertical gap={16}>
            <Card title="Delivery">
              <Descriptions column={1} size="small">
                <Descriptions.Item label="Customer">{s.customerName}</Descriptions.Item>
                {d.destinationAddress && <Descriptions.Item label="Address">{d.destinationAddress}</Descriptions.Item>}
                <Descriptions.Item label="Shipment">{s.shipmentReference ?? '—'}</Descriptions.Item>
                {d.lrNumber && <Descriptions.Item label="LR number">{d.lrNumber}</Descriptions.Item>}
                {!isVendor && <Descriptions.Item label="Transporter">{s.transporterReference ?? '—'}</Descriptions.Item>}
                <Descriptions.Item label="Vehicle">{s.vehicleReference ?? '—'}{d.driverName ? ` · ${d.driverName}` : ''}</Descriptions.Item>
                <Descriptions.Item label="Planned">{formatDateTime(s.plannedDeliveryAt)}</Descriptions.Item>
                {d.windowEnd && <Descriptions.Item label="Window">{formatDateTime(d.windowStart)} – {formatDateTime(d.windowEnd)}</Descriptions.Item>}
                <Descriptions.Item label="Arrived">{formatDateTime(d.actualArrivalAt)}</Descriptions.Item>
                <Descriptions.Item label="Delivered">{formatDateTime(s.actualDeliveryAt)}</Descriptions.Item>
                {d.geofenceRadiusM && <Descriptions.Item label="Geofence">{d.geofenceRadiusM} m</Descriptions.Item>}
              </Descriptions>
              {s.podId ? <Link to={`/delivery/pods/${s.podId}`}>Open the proof of delivery</Link> : <Typography.Text type="secondary">No proof of delivery yet.</Typography.Text>}
            </Card>
            <Card title="Timeline">
              <Timeline
                items={d.events.map((e) => ({
                  color: e.type === 'Failed' || e.type === 'Refused' || e.type === 'AttemptFailed' ? 'red' : e.type === 'Delivered' || e.type === 'Closed' ? 'green' : 'blue',
                  content: (
                    <div>
                      <strong>{eventLabel[e.type]}</strong>
                      {e.remarks && <> · {e.remarks}</>}
                      <br />
                      <Typography.Text type="secondary">{formatDateTime(e.at)}{e.latitude != null ? ` · ${e.latitude.toFixed(4)}, ${e.longitude?.toFixed(4)}` : ''}{e.deviceReference ? ` · ${e.deviceReference}` : ''}</Typography.Text>
                    </div>
                  ),
                }))}
              />
            </Card>
          </Flex>
        </Col>
      </Row>

      <Modal open={dialog === 'cancel' || dialog === 'close'} title={dialog === 'cancel' ? 'Cancel this delivery?' : 'Close this delivery?'} okText={dialog === 'cancel' ? 'Cancel delivery' : 'Close'}
        okButtonProps={{ disabled: reason.trim().length === 0, danger: dialog === 'cancel' }} confirmLoading={act.isPending} onOk={() => act.mutate(dialog === 'cancel' ? 'cancel' : 'close')} onCancel={() => setDialog(null)} destroyOnHidden>
        <Input.TextArea rows={3} maxLength={500} aria-label="Reason" placeholder="Why?" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
      <Modal open={dialog === 'reschedule'} title="Reschedule" okText="Reschedule" okButtonProps={{ disabled: !when }} confirmLoading={act.isPending} onOk={() => act.mutate('reschedule')} onCancel={() => setDialog(null)} destroyOnHidden>
        <DatePicker showTime style={{ width: '100%' }} aria-label="New delivery time" value={when} onChange={setWhen} />
      </Modal>
    </>
  )
}
