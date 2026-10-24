import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Form, Input, InputNumber, Select, Space, Switch, Table, Tag, message } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type CustomerDto, type LocationDto, type LocationKind, type OpsSettingsDto, type UomDto } from '@/lib/api/opsApi'
import { applyFieldErrors } from '@/lib/formErrors'
import { useAuth } from '@/features/auth/AuthContext'
import { ResponsiveDrawer, StatusTag } from './shared'
import { useCustomerOptions, useDebounced } from './hooks'

const statusOptions = [{ value: 'Active', label: 'Active' }, { value: 'Inactive', label: 'Inactive' }]
const requiredFieldOptions = [{ value: 'CustomerReference', label: 'Customer PO / reference' }, { value: 'Dimensions', label: 'Dimensions on every line' }, { value: 'Volume', label: 'Volume on every line' }]

export function CustomersPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [editing, setEditing] = useState<CustomerDto | 'new' | null>(null)
  const list = useQuery({ queryKey: ['ops', 'customers', q], queryFn: () => opsApi.customers({ search: q || undefined, pageSize: 100 }) })
  const [form] = Form.useForm()
  const save = useMutation({
    mutationFn: (v: Record<string, unknown>) => {
      const body = { ...v, requiredRequestFields: ((v.requiredRequestFields as string[] | undefined) ?? []).join(',') || null, version: editing && editing !== 'new' ? editing.version : 0 } as never
      return editing && editing !== 'new' ? opsApi.updateCustomer(editing.id, body) : opsApi.createCustomer(body)
    },
    onSuccess: () => { message.success('Customer saved'); setEditing(null); void qc.invalidateQueries({ queryKey: ['ops'] }) },
    onError: (e) => { const err = toApiError(e); if (!applyFieldErrors(form, err)) message.error(err.message) },
  })
  const open = (c: CustomerDto | 'new') => {
    setEditing(c)
    form.resetFields()
    form.setFieldsValue(c === 'new' ? { dueDays: 30, status: 'Active', requiredRequestFields: ['CustomerReference'] } : { ...c, requiredRequestFields: c.requiredRequestFields?.split(',').filter(Boolean) ?? [] })
  }
  return (
    <>
      <PageHeader title="Customers" description="The customers you bill. Their freight rates, detention and tax live under Customer commercial terms."
        actions={can('ops.masters.manage') && <Button type="primary" onClick={() => open('new')}>New customer</Button>} />
      <Card>
        <Space style={{ marginBottom: 12 }}><Input.Search allowClear placeholder="Search code, name or GSTIN" style={{ width: 300 }} onChange={(e) => setSearch(e.target.value)} /></Space>
        <Table<CustomerDto> scroll={{ x: 'max-content' }} rowKey="id" size="middle" loading={list.isLoading} dataSource={list.data?.items} pagination={false}
          columns={[
            { title: 'Code', dataIndex: 'code', width: 120 }, { title: 'Customer', dataIndex: 'name' }, { title: 'GSTIN', dataIndex: 'gstin', render: (v) => v ?? '—' },
            { title: 'Payment terms', dataIndex: 'dueDays', render: (v) => `${v} days` }, { title: 'Status', dataIndex: 'status', render: (v) => <StatusTag value={v} /> },
            { title: '', width: 80, render: (_, c) => can('ops.masters.manage') && <Button size="small" onClick={() => open(c)}>Edit</Button> },
          ]} />
      </Card>
      <ResponsiveDrawer open={editing !== null} onClose={() => setEditing(null)} desktopWidth={640} title={editing === 'new' ? 'New customer' : 'Edit customer'} destroyOnHidden
        footer={<Space><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save</Button></Space>}>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="code" label="Code" rules={[{ required: true }]} style={{ width: 180 }}><Input disabled={editing !== 'new'} maxLength={30} /></Form.Item>
            <Form.Item name="name" label="Name" rules={[{ required: true }]} style={{ flex: 1 }}><Input maxLength={200} /></Form.Item>
          </Space.Compact>
          <Form.Item name="gstin" label="GSTIN" extra="Checked for format and check digit. Its first two digits give the state used to split IGST from CGST + SGST."><Input maxLength={15} style={{ textTransform: 'uppercase' }} /></Form.Item>
          <Form.Item name="billingLine1" label="Billing address"><Input /></Form.Item>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="billingCity" label="City" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="billingState" label="State" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="billingPincode" label="Pincode" style={{ width: 120 }}><Input maxLength={6} /></Form.Item>
          </Space.Compact>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="billingContactName" label="Billing contact" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="billingContactEmail" label="Billing email" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="billingContactPhone" label="Phone" style={{ width: 160 }}><Input /></Form.Item>
          </Space.Compact>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="deliveryContactName" label="Delivery contact" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="deliveryContactPhone" label="Delivery phone" style={{ flex: 1 }}><Input /></Form.Item>
          </Space.Compact>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="commercialContactName" label="Commercial contact" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="commercialContactEmail" label="Commercial email" style={{ flex: 1 }}><Input /></Form.Item>
          </Space.Compact>
          <Form.Item name="dueDays" label="Invoice due after (days)" rules={[{ required: true }]}><InputNumber min={0} max={365} /></Form.Item>
          <Form.Item name="requiredRequestFields" label="Details this customer's requests must have" extra="A delivery request cannot become ready for dispatch without these."><Select mode="multiple" options={requiredFieldOptions} /></Form.Item>
          <Form.Item name="billingRequirements" label="Billing requirements" extra="For example: one invoice per PO; attach the signed POD."><Input.TextArea rows={2} /></Form.Item>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="portalMethod" label="Invoice submission" style={{ flex: 1 }}><Input placeholder="e.g. Customer portal, email" /></Form.Item>
            <Form.Item name="portalReference" label="Portal / vendor code" style={{ flex: 1 }}><Input /></Form.Item>
          </Space.Compact>
          <Form.Item name="status" label="Status"><Select options={statusOptions} /></Form.Item>
        </Form>
      </ResponsiveDrawer>
    </>
  )
}

export function LocationsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const customers = useCustomerOptions()
  const [search, setSearch] = useState('')
  const [kind, setKind] = useState<LocationKind>()
  const q = useDebounced(search)
  const [editing, setEditing] = useState<LocationDto | 'new' | null>(null)
  const list = useQuery({ queryKey: ['ops', 'locations', q, kind], queryFn: () => opsApi.locations({ search: q || undefined, kind, pageSize: 100 }) })
  const [form] = Form.useForm()
  const save = useMutation({
    mutationFn: (v: Record<string, unknown>) => {
      const body = { ...v, version: editing && editing !== 'new' ? editing.version : 0 } as never
      return editing && editing !== 'new' ? opsApi.updateLocation(editing.id, body) : opsApi.createLocation(body)
    },
    onSuccess: () => { message.success('Location saved'); setEditing(null); void qc.invalidateQueries({ queryKey: ['ops'] }) },
    onError: (e) => { const err = toApiError(e); if (!applyFieldErrors(form, err, { coordinates: 'latitude' })) message.error(err.message) },
  })
  const open = (l: LocationDto | 'new') => { setEditing(l); form.resetFields(); form.setFieldsValue(l === 'new' ? { kind: 'Distributor', geofenceRadiusM: 150, status: 'Active' } : l) }
  return (
    <>
      <PageHeader title="CFAs and distributor locations" description="Where goods are loaded and delivered. The coordinates and geofence radius are what the driver's arrival check uses."
        actions={can('ops.masters.manage') && <Button type="primary" onClick={() => open('new')}>New location</Button>} />
      <Card>
        <Space style={{ marginBottom: 12 }} wrap>
          <Input.Search allowClear placeholder="Search code, name or city" style={{ width: 300 }} onChange={(e) => setSearch(e.target.value)} />
          <Select allowClear placeholder="Type" style={{ width: 160 }} value={kind} onChange={setKind} options={[{ value: 'Cfa', label: 'CFA' }, { value: 'Distributor', label: 'Distributor' }]} />
        </Space>
        <Table<LocationDto> scroll={{ x: 'max-content' }} rowKey="id" size="middle" loading={list.isLoading} dataSource={list.data?.items} pagination={false}
          columns={[
            { title: 'Code', dataIndex: 'code', width: 130 }, { title: 'Name', dataIndex: 'name' }, { title: 'Type', dataIndex: 'kind', render: (v) => (v === 'Cfa' ? 'CFA' : 'Distributor') },
            { title: 'City', dataIndex: 'city' }, { title: 'Customer', dataIndex: 'customerName', render: (v) => v ?? '—' },
            { title: 'Map point', render: (_, l) => (l.latitude == null ? <Tag color="red">Missing: arrival cannot be verified</Tag> : `${l.latitude}, ${l.longitude}`) },
            { title: 'Radius', dataIndex: 'geofenceRadiusM', render: (v) => `${v} m` }, { title: 'Status', dataIndex: 'status', render: (v) => <StatusTag value={v} /> },
            { title: '', width: 80, render: (_, l) => can('ops.masters.manage') && <Button size="small" onClick={() => open(l)}>Edit</Button> },
          ]} />
      </Card>
      <ResponsiveDrawer open={editing !== null} onClose={() => setEditing(null)} desktopWidth={560} title={editing === 'new' ? 'New location' : 'Edit location'} destroyOnHidden
        footer={<Space><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save</Button></Space>}>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="code" label="Code" rules={[{ required: true }]} style={{ width: 170 }}><Input disabled={editing !== 'new'} maxLength={30} /></Form.Item>
            <Form.Item name="kind" label="Type" rules={[{ required: true }]} style={{ width: 170 }}><Select options={[{ value: 'Cfa', label: 'CFA' }, { value: 'Distributor', label: 'Distributor' }]} /></Form.Item>
          </Space.Compact>
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input maxLength={200} /></Form.Item>
          <Form.Item name="customerId" label="Customer"><Select allowClear showSearch optionFilterProp="label" options={customers} /></Form.Item>
          <Form.Item name="line1" label="Address" rules={[{ required: true }]}><Input /></Form.Item>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="city" label="City" rules={[{ required: true }]} style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="state" label="State" rules={[{ required: true }]} style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item name="pincode" label="Pincode" style={{ width: 120 }}><Input maxLength={6} /></Form.Item>
          </Space.Compact>
          <Space.Compact block style={{ gap: 12 }}>
            <Form.Item name="latitude" label="Latitude" style={{ flex: 1 }}><InputNumber style={{ width: '100%' }} step={0.0001} /></Form.Item>
            <Form.Item name="longitude" label="Longitude" style={{ flex: 1 }}><InputNumber style={{ width: '100%' }} step={0.0001} /></Form.Item>
            <Form.Item name="geofenceRadiusM" label="Geofence radius (m)" rules={[{ required: true }]} style={{ width: 170 }}><InputNumber min={20} max={5000} style={{ width: '100%' }} /></Form.Item>
          </Space.Compact>
          <Alert type="info" showIcon style={{ marginBottom: 16 }} title="Coordinates must be inside India. A driver's arrival is accepted only within the radius of this point; without coordinates it cannot be verified." />
          <Form.Item name="accessNotes" label="Access instructions"><Input.TextArea rows={2} /></Form.Item>
          <Form.Item name="status" label="Status"><Select options={statusOptions} /></Form.Item>
        </Form>
      </ResponsiveDrawer>
    </>
  )
}

export function OpsSettingsPage() {
  const qc = useQueryClient()
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['ops', 'settings'], queryFn: opsApi.settings })
  const [form] = Form.useForm<OpsSettingsDto>()
  const save = useMutation({
    mutationFn: (v: OpsSettingsDto) => opsApi.saveSettings(v),
    onSuccess: () => { message.success('Settings saved'); void qc.invalidateQueries({ queryKey: ['ops', 'settings'] }) },
    onError: (e) => { const err = toApiError(e); if (!applyFieldErrors(form, err)) message.error(err.message) },
  })
  const editable = can('ops.configure')
  return (
    <>
      <PageHeader title="Operations settings" description="Limits and numbering for this organisation." />
      <Card loading={q.isLoading}>
        {q.data && (
          <Form form={form} layout="vertical" initialValues={q.data} onFinish={(v) => save.mutate({ ...q.data!, ...v, fleet: q.data!.fleet })} disabled={!editable} style={{ maxWidth: 640 }}>
            <Card size="small" title="Driver arrival check and QR code" style={{ marginBottom: 16 }}>
              <Form.Item name={['arrival', 'maxAccuracyM']} label="Largest GPS error accepted (metres)" extra="A phone reporting a vaguer position than this is asked to retry."><InputNumber min={10} max={1000} /></Form.Item>
              <Form.Item name={['arrival', 'tokenValidityDays']} label="QR code valid for (days)"><InputNumber min={1} max={120} /></Form.Item>
              <Form.Item name={['pod', 'maxFiles']} label="Most POD files per upload"><InputNumber min={1} max={20} /></Form.Item>
            </Card>
            <Card size="small" title="Numbering" style={{ marginBottom: 16 }}>
              <Space wrap>
                <Form.Item name={['numbering', 'requestPrefix']} label="Requests"><Input style={{ width: 90 }} /></Form.Item>
                <Form.Item name={['numbering', 'lrPrefix']} label="LRs"><Input style={{ width: 90 }} /></Form.Item>
                <Form.Item name={['numbering', 'tripPrefix']} label="Trips"><Input style={{ width: 90 }} /></Form.Item>
                <Form.Item name={['numbering', 'digits']} label="Digits"><InputNumber min={3} max={10} style={{ width: 80 }} /></Form.Item>
              </Space>
            </Card>
            <Card size="small" title="Invoices" style={{ marginBottom: 16 }}>
              <Form.Item name={['invoice', 'seriesPrefix']} label="Invoice series" extra="Invoices are numbered SERIES/26-27/00001: gap-free, restarting each financial year."><Input style={{ width: 120 }} /></Form.Item>
              <Form.Item name={['invoice', 'supplierGstin']} label="Your GSTIN"><Input maxLength={15} /></Form.Item>
              <Form.Item name={['invoice', 'supplierStateCode']} label="Your state code (two digits)" extra="Used with the customer's GSTIN to choose IGST or CGST + SGST. Taken from your GSTIN when left empty."><Input style={{ width: 90 }} maxLength={2} /></Form.Item>
              <Form.Item name={['invoice', 'requireApproval']} label="Approval before an invoice is issued" valuePropName="checked" extra="Needs an approval policy for customer invoices (Administration → Approval policies)."><Switch /></Form.Item>
            </Card>
            {editable && <Button type="primary" htmlType="submit" loading={save.isPending}>Save settings</Button>}
          </Form>
        )}
      </Card>
    </>
  )
}

export function UomPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const manage = can('ops.masters.manage')
  const q = useQuery({ queryKey: ['ops', 'uoms'], queryFn: () => opsApi.uoms(false) })
  const [editing, setEditing] = useState<UomDto | 'new' | null>(null)
  const [form] = Form.useForm<{ code: string; name: string; isActive: boolean }>()
  const save = useMutation({
    mutationFn: (v: { code: string; name: string; isActive: boolean }) => (editing !== 'new' && editing?.id ? opsApi.updateUom(editing.id, v) : opsApi.createUom(v)),
    onSuccess: () => { setEditing(null); void qc.invalidateQueries({ queryKey: ['ops', 'uoms'] }); void qc.invalidateQueries({ queryKey: ['ops', 'uom-options'] }) },
    onError: (e) => { const err = toApiError(e); if (!applyFieldErrors(form, err)) message.error(err.message) },
  })
  const open = (u: UomDto | 'new') => { setEditing(u); form.setFieldsValue(u === 'new' ? { code: '', name: '', isActive: true } : u) }
  return (
    <>
      <PageHeader title="Units of measure" description="The units an LR line can use. Deactivate a unit to stop it being offered; LRs already created keep theirs."
        actions={manage && <Button type="primary" onClick={() => open('new')}>Add unit</Button>} />
      {q.data?.some((u) => u.isDefault) && <Alert type="info" showIcon style={{ marginBottom: 12 }} title="These are the built-in units. The first change you make copies them into your own list." />}
      <Card>
        <Table scroll={{ x: 'max-content' }} size="small" rowKey="code" loading={q.isLoading} dataSource={q.data ?? []} pagination={false}
          columns={[{ title: 'Code', dataIndex: 'code' }, { title: 'Name', dataIndex: 'name' }, { title: 'Status', dataIndex: 'isActive', render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? 'Active' : 'Inactive'}</Tag> },
            ...(manage ? [{ title: '', render: (_: unknown, u: UomDto) => (u.id ? <a onClick={() => open(u)}>Edit</a> : null) }] : [])]} />
      </Card>
      <ResponsiveDrawer open={editing !== null} onClose={() => setEditing(null)} desktopWidth={420} title={editing === 'new' ? 'Add unit' : 'Edit unit'} destroyOnHidden
        footer={<Space><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save</Button></Space>}>
        <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
          <Form.Item name="code" label="Code" rules={[{ required: true }]} extra="Short, for example BOX or CTN. It cannot be changed later."><Input maxLength={12} disabled={editing !== 'new'} style={{ textTransform: 'uppercase' }} /></Form.Item>
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input maxLength={60} /></Form.Item>
          <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>
        </Form>
      </ResponsiveDrawer>
    </>
  )
}
