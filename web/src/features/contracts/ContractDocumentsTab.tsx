import { DeleteOutlined, DownloadOutlined, InboxOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, Form, Input, Modal, Popconfirm, Select, Table, Upload, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Can } from '@/features/auth/AuthContext'
import { contractsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractDocumentDto, ContractDocumentKind } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { formatDateTime } from '@/lib/format'

const kinds: { value: ContractDocumentKind; label: string }[] = [
  { value: 'SignedContract', label: 'Signed contract' },
  { value: 'Annexure', label: 'Annexure' },
  { value: 'Amendment', label: 'Amendment' },
  { value: 'Correspondence', label: 'Correspondence' },
  { value: 'Other', label: 'Other' },
]

interface FormValues {
  kind: ContractDocumentKind
  title: string
  files: { originFileObj?: File }[]
}

export function ContractDocumentsTab({ contractId }: { contractId: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<FormValues>()
  const [open, setOpen] = useState(false)
  const documents = useQuery({ queryKey: queryKeys.contracts.documents(contractId), queryFn: () => contractsApi.documents(contractId) })

  const upload = useMutation({
    mutationFn: (v: FormValues) => contractsApi.uploadDocument(contractId, { kind: v.kind, title: v.title.trim(), file: v.files[0]!.originFileObj! }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.documents(contractId) })
      void message.success('Document uploaded')
      setOpen(false)
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })
  const remove = useMutation({
    mutationFn: contractsApi.deleteDocument,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.documents(contractId) })
      void message.success('Document deleted')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const columns: TableColumnsType<ContractDocumentDto> = [
    { title: 'Document', dataIndex: 'title' },
    { title: 'Type', dataIndex: 'kind', render: (k: ContractDocumentKind) => kinds.find((x) => x.value === k)?.label ?? k },
    { title: 'File', dataIndex: 'fileName', responsive: ['md'] },
    { title: 'Uploaded', dataIndex: 'uploadedAt', responsive: ['lg'], render: formatDateTime },
    {
      title: '', key: 'actions', width: 110,
      render: (_, d) => (
        <>
          <Button type="text" icon={<DownloadOutlined />} aria-label={`Download ${d.title}`} onClick={() => void contractsApi.downloadDocument(d.id, d.fileName).catch((e) => message.error(toApiError(e).message))} />
          <Can permission="contracts.manage">
            <Popconfirm title="Delete this document?" okText="Delete" okButtonProps={{ danger: true }} onConfirm={() => remove.mutate(d.id)}>
              <Button type="text" danger icon={<DeleteOutlined />} aria-label={`Delete ${d.title}`} />
            </Popconfirm>
          </Can>
        </>
      ),
    },
  ]

  return (
    <>
      <Card title="Documents" extra={<Can permission="contracts.manage"><Button icon={<UploadOutlined />} type="primary" onClick={() => { form.resetFields(); form.setFieldsValue({ kind: 'SignedContract', files: [] }); setOpen(true) }}>Upload</Button></Can>} styles={{ body: { padding: 0 } }}>
        <Table<ContractDocumentDto> rowKey="id" columns={columns} dataSource={documents.data} loading={documents.isLoading} pagination={false} scroll={{ x: 'max-content' }} locale={{ emptyText: 'No documents yet. Keep the signed agreement here.' }} />
      </Card>
      <Modal open={open} title="Upload contract document" okText="Upload" confirmLoading={upload.isPending} onCancel={() => setOpen(false)} onOk={() => form.submit()} destroyOnHidden>
        <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => upload.mutate(v)}>
          <Form.Item label="Type" name="kind"><Select options={kinds} /></Form.Item>
          <Form.Item label="Title" name="title" rules={[{ required: true, whitespace: true, message: 'Give the document a title' }]}><Input placeholder="e.g. Signed agreement, Annexure A" /></Form.Item>
          <Form.Item label="File" name="files" valuePropName="fileList" getValueFromEvent={(e: { fileList: unknown[] }) => e.fileList.slice(-1)} rules={[{ required: true, type: 'array', min: 1, message: 'Attach a file' }]} extra="PDF, JPG or PNG, up to 10 MB.">
            <Upload.Dragger maxCount={1} accept=".pdf,.jpg,.jpeg,.png" beforeUpload={(f) => (f.size > 10 * 1024 * 1024 ? (void message.error('Files can be at most 10 MB'), Upload.LIST_IGNORE) : false)}>
              <p className="ant-upload-drag-icon"><InboxOutlined /></p>
              <p className="ant-upload-text">Click or drag a file here</p>
            </Upload.Dragger>
          </Form.Item>
        </Form>
      </Modal>
    </>
  )
}
