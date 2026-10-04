import { DeleteOutlined, DownloadOutlined, InboxOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, DatePicker, Form, Input, Modal, Popconfirm, Select, Table, Typography, Upload, type TableColumnsType } from 'antd'
import { useEffect, useMemo } from 'react'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DocumentDto, DocumentKind, OwnerKind } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { formatDateTime } from '@/lib/format'
import { ExpiryTag } from './tags'

const MAX_BYTES = 10 * 1024 * 1024

const kindsByOwner: Record<OwnerKind, { value: DocumentKind; label: string; needsExpiry: boolean }[]> = {
  Transporter: [
    { value: 'PanCard', label: 'PAN card', needsExpiry: false },
    { value: 'GstCertificate', label: 'GST certificate', needsExpiry: false },
    { value: 'CancelledCheque', label: 'Cancelled cheque', needsExpiry: false },
    { value: 'MsmeCertificate', label: 'MSME certificate', needsExpiry: false },
    { value: 'TransportLicense', label: 'Transport licence', needsExpiry: false },
  ],
  Vehicle: [
    { value: 'RegistrationCertificate', label: 'Registration certificate (RC)', needsExpiry: false },
    { value: 'Insurance', label: 'Insurance', needsExpiry: true },
    { value: 'Fitness', label: 'Fitness certificate', needsExpiry: true },
    { value: 'Permit', label: 'Permit', needsExpiry: true },
    { value: 'Puc', label: 'PUC certificate', needsExpiry: true },
  ],
  Driver: [{ value: 'DrivingLicense', label: 'Driving licence', needsExpiry: true }],
}

interface FormValues {
  owner: string // "Kind:id"
  kind: DocumentKind
  number?: string
  issuedOn?: import('dayjs').Dayjs
  expiresOn?: import('dayjs').Dayjs
  files: { originFileObj?: File }[]
}

interface Owner {
  kind: OwnerKind
  id: string
  label: string
}

interface UploadProps {
  transporterId: string
  transporterName: string
  owners: Owner[]
  open: boolean
  preset: { kind: OwnerKind; id: string } | null
  onClose: () => void
}

function UploadModal({ transporterId, transporterName, owners, open, preset, onClose }: UploadProps) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const ownerKey = Form.useWatch('owner', form) as string | undefined
  const kind = Form.useWatch('kind', form) as DocumentKind | undefined
  const ownerKind = (ownerKey?.split(':')[0] ?? 'Transporter') as OwnerKind
  const needsExpiry = kindsByOwner[ownerKind].find((k) => k.value === kind)?.needsExpiry ?? false

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue({ owner: preset ? `${preset.kind}:${preset.id}` : `Transporter:${transporterId}`, files: [] })
  }, [open, preset, transporterId, form])

  const upload = useMutation({
    mutationFn: (v: FormValues) => {
      const [kindPart, id] = v.owner.split(':') as [OwnerKind, string]
      return transportersApi.uploadDocument(transporterId, {
        ownerKind: kindPart,
        ownerId: id,
        kind: v.kind,
        number: v.number?.trim() || undefined,
        issuedOn: v.issuedOn?.format('YYYY-MM-DD'),
        expiresOn: v.expiresOn?.format('YYYY-MM-DD'),
        file: v.files[0]!.originFileObj!,
      })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success('Document uploaded')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Modal open={open} title="Upload document" okText="Upload" confirmLoading={upload.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => upload.mutate(v)} disabled={upload.isPending}>
        <Form.Item label="Belongs to" name="owner" rules={[{ required: true }]}>
          <Select
            showSearch
            optionFilterProp="label"
            onChange={() => form.setFieldValue('kind', undefined)}
            options={[{ value: `Transporter:${transporterId}`, label: `${transporterName} (company)` }, ...owners.map((o) => ({ value: `${o.kind}:${o.id}`, label: `${o.kind === 'Vehicle' ? 'Vehicle' : 'Driver'} · ${o.label}` }))]}
          />
        </Form.Item>
        <Form.Item label="Document type" name="kind" rules={[{ required: true, message: 'Choose the document type' }]}>
          <Select options={kindsByOwner[ownerKind].map((k) => ({ value: k.value, label: k.label }))} />
        </Form.Item>
        <Form.Item label="Document number" name="number"><Input /></Form.Item>
        <Form.Item label="Issued on" name="issuedOn"><DatePicker style={{ width: '100%' }} format="D MMM YYYY" /></Form.Item>
        <Form.Item label="Valid until" name="expiresOn" rules={[{ required: needsExpiry, message: 'An expiry date is required for this document' }]} extra={needsExpiry ? undefined : 'Leave empty if it does not expire.'}>
          <DatePicker style={{ width: '100%' }} format="D MMM YYYY" />
        </Form.Item>
        <Form.Item
          label="File"
          name="files"
          valuePropName="fileList"
          getValueFromEvent={(e: { fileList: unknown[] }) => e.fileList.slice(-1)}
          rules={[{ required: true, type: 'array', min: 1, message: 'Attach a file' }]}
          extra="PDF, JPG or PNG, up to 10 MB."
        >
          <Upload.Dragger
            maxCount={1}
            accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png"
            beforeUpload={(file) => {
              if (file.size > MAX_BYTES) {
                void message.error('Files can be at most 10 MB')
                return Upload.LIST_IGNORE
              }
              return false // keep it in the form; we send it ourselves with the auth header
            }}
          >
            <p className="ant-upload-drag-icon"><InboxOutlined /></p>
            <p className="ant-upload-text">Click or drag a file here</p>
          </Upload.Dragger>
        </Form.Item>
      </Form>
    </Modal>
  )
}

interface Props {
  transporterId: string
  transporterName: string
  /** Pre-selects the owner when arriving from the fleet tab. */
  uploadPreset: { kind: OwnerKind; id: string } | null
  uploadOpen: boolean
  onUploadOpen: (open: boolean) => void
}

export function DocumentsTab({ transporterId, transporterName, uploadPreset, uploadOpen, onUploadOpen }: Props) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const documents = useQuery({ queryKey: queryKeys.transporters.documents(transporterId), queryFn: () => transportersApi.documents(transporterId) })
  const vehicles = useQuery({ queryKey: queryKeys.transporters.vehicles(transporterId, 0, 'all'), queryFn: () => transportersApi.vehicles(transporterId, { pageSize: 200 }) })
  const drivers = useQuery({ queryKey: queryKeys.transporters.drivers(transporterId, 0, 'all'), queryFn: () => transportersApi.drivers(transporterId, { pageSize: 200 }) })

  const owners = useMemo<Owner[]>(
    () => [
      ...(vehicles.data?.items ?? []).map((v) => ({ kind: 'Vehicle' as const, id: v.id, label: v.registrationNumber })),
      ...(drivers.data?.items ?? []).map((d) => ({ kind: 'Driver' as const, id: d.id, label: d.fullName })),
    ],
    [vehicles.data, drivers.data],
  )
  const labelOf = (d: DocumentDto) => (d.ownerKind === 'Transporter' ? 'Company' : owners.find((o) => o.id === d.ownerId)?.label ?? d.ownerKind)

  const remove = useMutation({
    mutationFn: transportersApi.deleteDocument,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success('Document deleted')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const columns: TableColumnsType<DocumentDto> = [
    { title: 'Belongs to', key: 'owner', render: (_, d) => <><Typography.Text strong>{labelOf(d)}</Typography.Text><br /><Typography.Text type="secondary">{d.ownerKind}</Typography.Text></> },
    { title: 'Document', key: 'kind', render: (_, d) => <>{d.kindLabel}{d.number && <><br /><Typography.Text type="secondary">{d.number}</Typography.Text></>}</> },
    { title: 'Valid until', dataIndex: 'expiresOn', render: (v: string | null) => v ?? '—' },
    { title: 'Status', dataIndex: 'expiryStatus', render: (s: DocumentDto['expiryStatus']) => <ExpiryTag status={s} /> },
    { title: 'Uploaded', dataIndex: 'uploadedAt', responsive: ['lg'], render: formatDateTime },
    {
      title: '', key: 'actions', width: 110,
      render: (_, d) => (
        <>
          <Button type="text" icon={<DownloadOutlined />} aria-label={`Download ${d.fileName}`} onClick={() => void transportersApi.downloadDocument(d.id, d.fileName).catch((e) => message.error(toApiError(e).message))} />
          <Popconfirm title="Delete this document?" description="The file is removed permanently." okText="Delete" okButtonProps={{ danger: true }} onConfirm={() => remove.mutate(d.id)}>
            <Button type="text" danger icon={<DeleteOutlined />} aria-label={`Delete ${d.fileName}`} />
          </Popconfirm>
        </>
      ),
    },
  ]

  return (
    <>
      <Card title="Documents" extra={<Button type="primary" icon={<UploadOutlined />} onClick={() => onUploadOpen(true)}>Upload document</Button>} styles={{ body: { padding: 0 } }}>
        <Table<DocumentDto> rowKey="id" columns={columns} dataSource={documents.data} loading={documents.isLoading} pagination={false} scroll={{ x: 'max-content' }} locale={{ emptyText: 'No documents uploaded yet' }} />
      </Card>
      <UploadModal transporterId={transporterId} transporterName={transporterName} owners={owners} open={uploadOpen} preset={uploadPreset} onClose={() => onUploadOpen(false)} />
    </>
  )
}
