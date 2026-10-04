import { DeleteOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, DatePicker, Drawer, Flex, Form, Input, InputNumber, Modal, Space, Typography, Upload } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { deliveryApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { PodDocumentDto, ShipmentDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { formatDateTime } from '@/lib/format'
import { PodStatusTag } from './shared'

type Line = ShipmentDto['orders'][number]

interface DeliveryValues {
  receiverName: string
  deliveredAt: Dayjs
  deliveredPackages?: number | null
  damagedPackages?: number | null
  remarks?: string
}

/** Who received the goods and what arrived. Shortage or damage needs an explanation, and the server enforces that. */
export function RecordDeliveryModal({ shipmentId, line, onClose, onDone }: { shipmentId: string; line: Line; onClose: () => void; onDone: (s: ShipmentDto) => void }) {
  const [form] = Form.useForm<DeliveryValues>()
  const { message } = App.useApp()
  const shipped = line.packagesShipped ?? null
  const delivered = Form.useWatch('deliveredPackages', form) as number | null | undefined
  const damaged = Form.useWatch('damagedPackages', form) as number | null | undefined
  const exception = shipped !== null && ((delivered ?? shipped) < shipped || (damaged ?? 0) > 0)

  const save = useMutation({
    mutationFn: (v: DeliveryValues) =>
      deliveryApi.record(shipmentId, line.orderId, {
        deliveredAt: v.deliveredAt.toISOString(),
        receiverName: v.receiverName.trim(),
        deliveredPackages: shipped === null ? null : (v.deliveredPackages ?? shipped),
        damagedPackages: shipped === null ? null : (v.damagedPackages ?? 0),
        remarks: v.remarks?.trim() || null,
      }),
    onSuccess: (shipment) => {
      void message.success(`${line.orderNumber} delivered`)
      onDone(shipment)
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Modal open title={`Record delivery: ${line.orderNumber}`} okText="Confirm delivery" confirmLoading={save.isPending} onOk={() => form.submit()} onCancel={onClose} destroyOnHidden>
      <Form<DeliveryValues> form={form} layout="vertical" requiredMark="optional" initialValues={{ deliveredAt: dayjs(), deliveredPackages: shipped, damagedPackages: 0 }} onFinish={(v) => save.mutate(v)}>
        <Form.Item name="receiverName" label="Received by" rules={[{ required: true, message: 'Enter who received the goods' }]}>
          <Input aria-label="Received by" maxLength={150} />
        </Form.Item>
        <Form.Item name="deliveredAt" label="Delivered at" rules={[{ required: true, message: 'Choose when' }]}>
          <DatePicker aria-label="Delivered at" showTime={{ format: 'HH:mm' }} format="DD MMM YYYY HH:mm" style={{ width: '100%' }} />
        </Form.Item>
        {shipped !== null ? (
          <Flex gap={12} wrap>
            <Form.Item name="deliveredPackages" label={`Packages received (of ${shipped})`} style={{ flex: 1, minWidth: 170 }}>
              <InputNumber aria-label="Packages received" min={0} max={shipped} precision={0} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item name="damagedPackages" label="Of which damaged" style={{ flex: 1, minWidth: 150 }}>
              <InputNumber aria-label="Packages damaged" min={0} max={delivered ?? shipped} precision={0} style={{ width: '100%' }} />
            </Form.Item>
          </Flex>
        ) : (
          <Typography.Paragraph type="secondary">This order has no package count, so quantities are not recorded.</Typography.Paragraph>
        )}
        {exception && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="Goods are short or damaged. This is reported for a claim; say what happened." />}
        <Form.Item name="remarks" label={exception ? 'What happened' : 'Remarks'} rules={exception ? [{ required: true, message: 'Say what happened' }] : []}>
          <Input.TextArea aria-label="Remarks" rows={3} maxLength={500} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** The proof of delivery for one order: files, and the verify / reject decision for staff. */
export function PodDrawer({
  shipmentId, line, canUpload, canReview, onClose, onChanged,
}: { shipmentId: string; line: Line; canUpload: boolean; canReview: boolean; onClose: () => void; onChanged: (s?: ShipmentDto) => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')
  const documents = useQuery({ queryKey: queryKeys.pod.documents(shipmentId, line.orderId), queryFn: () => deliveryApi.documents(shipmentId, line.orderId) })
  const status = line.podStatus ?? 'Awaiting'
  const final = status === 'Verified'

  const refresh = async (shipment?: ShipmentDto) => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.pod.all })
    onChanged(shipment)
  }
  const fail = (e: unknown) => void message.error(toApiError(e).message)

  const upload = useMutation({ mutationFn: (file: File) => deliveryApi.upload(shipmentId, line.orderId, file), onSuccess: async () => { await refresh(); void message.success('Proof uploaded') }, onError: fail })
  const remove = useMutation({ mutationFn: (id: string) => deliveryApi.remove(id), onSuccess: () => refresh(), onError: fail })
  const verify = useMutation({ mutationFn: () => deliveryApi.verify(shipmentId, line.orderId), onSuccess: async (s) => { await refresh(s); void message.success('Proof verified') }, onError: fail })
  const reject = useMutation({ mutationFn: () => deliveryApi.reject(shipmentId, line.orderId, reason.trim()), onSuccess: async (s) => { await refresh(s); setRejecting(false); setReason('') }, onError: fail })

  return (
    <Drawer open onClose={onClose} size={520} destroyOnHidden title={`Proof of delivery: ${line.orderNumber}`}>
      <Flex vertical gap={16}>
        <Flex gap={8} align="center" wrap>
          <PodStatusTag status={status} />
          <Typography.Text type="secondary">
            Delivered {formatDateTime(line.deliveredAt)}{line.receiverName ? ` · received by ${line.receiverName}` : ''}
          </Typography.Text>
        </Flex>
        {(line.shortagePackages ?? 0) > 0 || (line.damagedPackages ?? 0) > 0 ? (
          <Alert type="warning" showIcon title={`${line.shortagePackages ?? 0} short, ${line.damagedPackages ?? 0} damaged`} description={line.deliveryRemarks} />
        ) : null}
        {status === 'Rejected' && line.podRejectionReason && <Alert type="error" showIcon title="Proof rejected" description={line.podRejectionReason} />}

        <Flex vertical gap={8} aria-label="Proof files">
          {documents.isLoading && <Typography.Text type="secondary">Loading…</Typography.Text>}
          {documents.data?.length === 0 && <Typography.Text type="secondary">No files yet</Typography.Text>}
          {documents.data?.map((d: PodDocumentDto) => (
            <Flex key={d.id} justify="space-between" align="center" gap={8} style={{ padding: '8px 12px', border: '1px solid var(--ant-color-border, #d9d9d9)', borderRadius: 8 }}>
              <div style={{ minWidth: 0 }}>
                <Typography.Text strong ellipsis style={{ display: 'block' }}>{d.fileName}</Typography.Text>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>{formatDateTime(d.uploadedAt)} · {(d.sizeBytes / 1024).toFixed(0)} KB</Typography.Text>
              </div>
              <Space>
                <Button size="small" icon={<DownloadOutlined />} aria-label={`Download ${d.fileName}`} onClick={() => void deliveryApi.download(d).catch(fail)} />
                {canUpload && !final && (
                  <Button size="small" danger icon={<DeleteOutlined />} aria-label={`Remove ${d.fileName}`} loading={remove.isPending && remove.variables === d.id} onClick={() => remove.mutate(d.id)} />
                )}
              </Space>
            </Flex>
          ))}
        </Flex>

        {canUpload && !final && (
          <Upload accept=".pdf,.jpg,.jpeg,.png" showUploadList={false} beforeUpload={(file) => { upload.mutate(file); return false }}>
            <Button icon={<UploadOutlined />} loading={upload.isPending}>Upload signed copy or photo</Button>
          </Upload>
        )}
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>PDF, JPG or PNG, up to 10 MB each, at most 5 files.</Typography.Text>

        {canReview && status === 'Uploaded' && (
          <Space>
            <Button type="primary" loading={verify.isPending} onClick={() => verify.mutate()}>Verify proof</Button>
            <Button danger onClick={() => setRejecting(true)}>Reject…</Button>
          </Space>
        )}
      </Flex>
      <Modal open={rejecting} title="Reject this proof?" okText="Reject" okButtonProps={{ danger: true, disabled: reason.trim().length === 0 }} confirmLoading={reject.isPending}
        onOk={() => reject.mutate()} onCancel={() => { setRejecting(false); setReason('') }} destroyOnHidden>
        <Input.TextArea rows={3} maxLength={500} aria-label="Why is the proof rejected?" placeholder="What is wrong with it? The transporter sees this." value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </Drawer>
  )
}
