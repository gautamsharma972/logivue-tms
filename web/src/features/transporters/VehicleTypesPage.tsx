import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Badge, Button, Card, Drawer, Flex, Form, Input, InputNumber, Switch, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { VehicleTypeDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

interface FormValues {
  code: string
  name: string
  payloadKg: number
  volumeCbm?: number | null
  lengthM?: number | null
  widthM?: number | null
  heightM?: number | null
  allowsHazardous: boolean
  supportsTemperatureControl: boolean
  isActive: boolean
}

const number = { style: { width: '100%' }, controls: false } as const

function VehicleTypeDrawer({ type, open, onClose }: { type: VehicleTypeDto | null; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      type
        ? { ...type, volumeCbm: type.volumeCbm, allowsHazardous: type.allowsHazardous ?? true, supportsTemperatureControl: type.supportsTemperatureControl ?? false }
        : { allowsHazardous: true, supportsTemperatureControl: false, isActive: true },
    )
  }, [open, type, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = {
        code: v.code.trim(),
        name: v.name.trim(),
        payloadKg: v.payloadKg,
        volumeCbm: v.volumeCbm ?? null,
        lengthM: v.lengthM ?? null,
        widthM: v.widthM ?? null,
        heightM: v.heightM ?? null,
        allowsHazardous: v.allowsHazardous,
        supportsTemperatureControl: v.supportsTemperatureControl,
        isActive: type ? v.isActive : true,
        version: type?.version ?? null,
      }
      return type ? transportersApi.updateVehicleType(type.id, body) : transportersApi.createVehicleType(body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.vehicleTypes })
      void message.success(type ? 'Vehicle type updated' : 'Vehicle type added')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Drawer open={open} onClose={onClose} size={480} destroyOnHidden title={type ? `Edit ${type.name}` : 'Add vehicle type'}
      footer={<Flex justify="flex-end" gap={8}><Button onClick={onClose}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{type ? 'Save' : 'Add type'}</Button></Flex>}>
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)} disabled={save.isPending}>
        <Flex gap={12}>
          <Form.Item label="Code" name="code" rules={[{ required: true, message: 'Enter a code' }]} style={{ width: 140 }}><Input maxLength={30} autoFocus /></Form.Item>
          <Form.Item label="Name" name="name" rules={[{ required: true, message: 'Enter a name' }]} style={{ flex: 1 }}><Input maxLength={100} /></Form.Item>
        </Flex>
        <Flex gap={12}>
          <Form.Item label="Payload (kg)" name="payloadKg" rules={[{ required: true, message: 'Enter the payload' }]} style={{ flex: 1 }}><InputNumber min={1} precision={0} {...number} /></Form.Item>
          <Form.Item label="Volume (CBM)" name="volumeCbm" style={{ flex: 1 }}><InputNumber min={0} precision={2} {...number} /></Form.Item>
        </Flex>
        <Typography.Text type="secondary">Body size in metres. Optional; when set, the planner will not put an item longer than the body on this vehicle.</Typography.Text>
        <Flex gap={12} style={{ marginTop: 8 }}>
          <Form.Item label="Length (m)" name="lengthM" style={{ flex: 1 }}><InputNumber min={0.1} max={30} precision={2} {...number} /></Form.Item>
          <Form.Item label="Width (m)" name="widthM" style={{ flex: 1 }}><InputNumber min={0.1} max={5} precision={2} {...number} /></Form.Item>
          <Form.Item label="Height (m)" name="heightM" style={{ flex: 1 }}><InputNumber min={0.1} max={6} precision={2} {...number} /></Form.Item>
        </Flex>
        <Form.Item label="Can carry hazardous goods" name="allowsHazardous" valuePropName="checked"><Switch /></Form.Item>
        <Form.Item label="Temperature controlled (refrigerated body)" name="supportsTemperatureControl" valuePropName="checked"><Switch /></Form.Item>
        {type && <Form.Item label="Active" name="isActive" valuePropName="checked" extra="Inactive types are not offered for planning."><Switch /></Form.Item>}
      </Form>
    </Drawer>
  )
}

export function VehicleTypesPage() {
  const [editing, setEditing] = useState<VehicleTypeDto | null>(null)
  const [creating, setCreating] = useState(false)
  const types = useQuery({ queryKey: queryKeys.transporters.vehicleTypes, queryFn: transportersApi.vehicleTypes })

  const columns: TableColumnsType<VehicleTypeDto> = [
    { title: 'Type', key: 't', render: (_, v) => <div><Typography.Text strong>{v.name}</Typography.Text> <Tag variant="filled">{v.code}</Tag></div> },
    { title: 'Payload', dataIndex: 'payloadKg', align: 'right', render: (p: number) => `${p.toLocaleString('en-IN')} kg` },
    { title: 'Volume', dataIndex: 'volumeCbm', align: 'right', render: (v: number | null) => (v === null ? '—' : `${v} CBM`) },
    {
      title: 'Body (L × W × H)',
      key: 'd',
      render: (_, v) => (v.lengthM || v.widthM || v.heightM ? [v.lengthM, v.widthM, v.heightM].map((x) => x ?? '?').join(' × ') + ' m' : <Typography.Text type="secondary">Not recorded</Typography.Text>),
    },
    {
      title: 'Suitable for',
      key: 's',
      render: (_, v) => (
        <Flex gap={4} wrap>
          {v.allowsHazardous === false ? <Tag color="red">No hazardous goods</Tag> : <Tag>Hazardous OK</Tag>}
          {v.supportsTemperatureControl && <Tag color="blue">Temperature controlled</Tag>}
        </Flex>
      ),
    },
    { title: 'Status', dataIndex: 'isActive', render: (a: boolean) => <Badge status={a ? 'success' : 'default'} text={a ? 'Active' : 'Inactive'} /> },
    { title: '', key: 'a', align: 'right', render: (_, v) => <Can permission="transporters.manage"><Button size="small" icon={<EditOutlined />} aria-label={`Edit ${v.name}`} onClick={() => setEditing(v)} /></Can> },
  ]

  return (
    <>
      <PageHeader
        title="Vehicle types"
        description="Payload, volume and body size of each truck type, and what it may carry. Planning uses these to decide what fits."
        actions={<Can permission="transporters.manage"><Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>Add vehicle type</Button></Can>}
      />
      <Card styles={{ body: { padding: 0 } }}>
        {types.isError && <Alert type="error" showIcon title={types.error.message} style={{ margin: 16 }} />}
        <Table<VehicleTypeDto> rowKey="id" columns={columns} dataSource={types.data} loading={types.isFetching} pagination={false} scroll={{ x: 'max-content' }} />
      </Card>
      <VehicleTypeDrawer open={creating || editing !== null} type={editing} onClose={() => { setCreating(false); setEditing(null) }} />
    </>
  )
}
