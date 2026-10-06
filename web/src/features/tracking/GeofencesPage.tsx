import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Form, Input, InputNumber, Modal, Popconfirm, Select, Table } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { applyFieldErrors } from '@/lib/formErrors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { GeofenceDto, GeofenceType, SaveGeofenceRequest } from '@/lib/api/types'
import { useAuth } from '@/features/auth/AuthContext'

const TYPES: GeofenceType[] = ['Origin', 'Destination', 'Customer', 'Warehouse', 'Hub', 'CrossDock', 'Depot', 'Toll', 'RestrictedArea', 'HighRiskZone', 'Custom']

export function GeofencesPage() {
  const { can } = useAuth()
  const { message } = App.useApp()
  const client = useQueryClient()
  const [form] = Form.useForm<SaveGeofenceRequest>()
  const [editing, setEditing] = useState<GeofenceDto | 'new' | null>(null)
  const q = useQuery({ queryKey: queryKeys.tracking.geofences, queryFn: trackingApi.geofences })
  const refresh = () => void client.invalidateQueries({ queryKey: queryKeys.tracking.geofences })
  const save = useMutation({
    mutationFn: (v: SaveGeofenceRequest) => (editing && editing !== 'new' ? trackingApi.updateGeofence(editing.id, { ...v, version: editing.version }) : trackingApi.createGeofence(v)),
    onSuccess: () => { refresh(); setEditing(null) },
    onError: (e) => { const api = toApiError(e)
      if (!applyFieldErrors(form, api)) void message.error(api.message) },
  })
  const remove = useMutation({ mutationFn: (id: string) => trackingApi.deleteGeofence(id), onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })
  const manage = can('tracking.geofences.manage')

  return (
    <>
      <PageHeader title="Geofences" description="Places the system recognises: warehouses, customers, hubs, toll plazas, restricted and high-risk areas. Entry and exit need several consecutive fixes, so one noisy GPS point does not trigger them."
        actions={manage ? <Button type="primary" onClick={() => { form.resetFields(); form.setFieldsValue({ type: 'Customer', radiusMeters: 300 }); setEditing('new') }}>New geofence</Button> : undefined} />
      <Table<GeofenceDto> size="small" rowKey="id" loading={q.isLoading} dataSource={q.data ?? []} pagination={{ pageSize: 20 }} columns={[
        { title: 'Code', dataIndex: 'code' }, { title: 'Name', dataIndex: 'name' }, { title: 'Type', dataIndex: 'type' },
        { title: 'Centre', render: (_, r) => `${r.centerLatitude.toFixed(4)}, ${r.centerLongitude.toFixed(4)}` }, { title: 'Radius', dataIndex: 'radiusMeters', render: (v: number) => `${v} m` },
        { title: 'Status', dataIndex: 'status' },
        { title: '', render: (_, r) => manage && <>
          <Button size="small" onClick={() => { form.setFieldsValue({ ...r, effectiveFrom: null, effectiveTo: null }); setEditing(r) }}>Edit</Button>
          <Popconfirm title="Delete this geofence?" onConfirm={() => remove.mutate(r.id)}><Button size="small" danger style={{ marginLeft: 6 }}>Delete</Button></Popconfirm></> },
      ]} />
      <Modal open={!!editing} title={editing === 'new' ? 'New geofence' : 'Edit geofence'} onCancel={() => setEditing(null)} onOk={() => form.submit()} confirmLoading={save.isPending} destroyOnHidden forceRender>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate({ ...v, polygon: null, effectiveFrom: null, effectiveTo: null, status: v.status ?? null, version: null })}>
          <Form.Item name="code" label="Code" rules={[{ required: true }]}><Input /></Form.Item>
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input /></Form.Item>
          <Form.Item name="type" label="Type" rules={[{ required: true }]}><Select options={TYPES.map((t) => ({ value: t, label: t }))} /></Form.Item>
          <Form.Item name="centerLatitude" label="Latitude" rules={[{ required: true }]}><InputNumber style={{ width: '100%' }} min={-90} max={90} /></Form.Item>
          <Form.Item name="centerLongitude" label="Longitude" rules={[{ required: true }]}><InputNumber style={{ width: '100%' }} min={-180} max={180} /></Form.Item>
          <Form.Item name="radiusMeters" label="Radius (metres)" rules={[{ required: true }]}><InputNumber style={{ width: '100%' }} min={20} max={20000} /></Form.Item>
          <Form.Item name="status" label="Status"><Select allowClear options={[{ value: 'Active', label: 'Active' }, { value: 'Inactive', label: 'Inactive' }]} /></Form.Item>
        </Form>
      </Modal>
    </>
  )
}
