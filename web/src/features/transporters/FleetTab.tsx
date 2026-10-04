import { EditOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Badge, Button, Card, DatePicker, Drawer, Flex, Form, Input, InputNumber, Select, Switch, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DriverDto, FleetAvailability, OwnerKind, VehicleDto, VehicleOwnership } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { ComplianceTag } from './tags'

interface VehicleForm {
  registrationNumber: string
  vehicleTypeId: string
  ownership: VehicleOwnership
  make?: string
  yearOfManufacture?: number | null
  isActive: boolean
  availability: FleetAvailability
  window?: [Dayjs | null, Dayjs | null] | null
  availabilityNote?: string
}

const availabilityOptions: { value: FleetAvailability; label: string }[] = [
  { value: 'Available', label: 'Available' },
  { value: 'InMaintenance', label: 'In maintenance' },
  { value: 'OffRoad', label: 'Off the road' },
]

function availabilityText(v: VehicleDto) {
  const from = v.availableFrom ? dayjs(v.availableFrom).format('DD MMM') : null
  const to = v.availableTo ? dayjs(v.availableTo).format('DD MMM') : null
  if (!v.availability || v.availability === 'Available') return from || to ? `Available ${from ? `from ${from} ` : ''}${to ? `until ${to}` : ''}`.trim() : 'Available'
  return `${v.availability === 'InMaintenance' ? 'In maintenance' : 'Off the road'}${from ? ` — back ${from}` : ''}`
}

function VehicleDrawer({ transporterId, vehicle, open, onClose }: { transporterId: string; vehicle: VehicleDto | null; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<VehicleForm>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const types = useQuery({ queryKey: queryKeys.transporters.vehicleTypes, queryFn: transportersApi.vehicleTypes, enabled: open })

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      vehicle
        ? { registrationNumber: vehicle.registrationNumber, vehicleTypeId: vehicle.vehicleTypeId, ownership: vehicle.ownership, make: vehicle.make ?? undefined, yearOfManufacture: vehicle.yearOfManufacture, isActive: vehicle.isActive,
            availability: vehicle.availability ?? 'Available', window: [vehicle.availableFrom ? dayjs(vehicle.availableFrom) : null, vehicle.availableTo ? dayjs(vehicle.availableTo) : null], availabilityNote: vehicle.availabilityNote ?? '' }
        : { ownership: 'Owned', isActive: true, availability: 'Available' },
    )
  }, [open, vehicle, form])

  const save = useMutation({
    mutationFn: (v: VehicleForm) => {
      const { window: range, availabilityNote, ...rest } = v
      const body = {
        ...rest,
        make: v.make?.trim() || null,
        yearOfManufacture: v.yearOfManufacture ?? null,
        version: vehicle?.version ?? null,
        availableFrom: range?.[0] ? range[0].format('YYYY-MM-DD') : null,
        availableTo: range?.[1] ? range[1].format('YYYY-MM-DD') : null,
        availabilityNote: availabilityNote?.trim() || null,
      }
      return vehicle ? transportersApi.updateVehicle(vehicle.id, body) : transportersApi.createVehicle(transporterId, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success(vehicle ? 'Vehicle updated' : 'Vehicle added')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Drawer open={open} onClose={onClose} size={480} destroyOnHidden title={vehicle ? 'Edit vehicle' : 'Add vehicle'}
      footer={<Flex justify="flex-end" gap={8}><Button onClick={onClose}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{vehicle ? 'Save' : 'Add vehicle'}</Button></Flex>}>
      <Form<VehicleForm> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)} disabled={save.isPending}>
        <Form.Item label="Registration number" name="registrationNumber" normalize={(v?: string) => v?.toUpperCase()} rules={[{ required: true, message: 'Enter the registration number' }]} extra="e.g. MH12AB1234 — spaces and dashes are ignored.">
          <Input disabled={vehicle !== null} autoFocus />
        </Form.Item>
        <Form.Item label="Vehicle type" name="vehicleTypeId" rules={[{ required: true, message: 'Choose a vehicle type' }]}>
          <Select showSearch optionFilterProp="label" loading={types.isLoading} options={types.data?.filter((t) => t.isActive || t.id === vehicle?.vehicleTypeId).map((t) => ({ value: t.id, label: t.name }))} />
        </Form.Item>
        <Form.Item label="Ownership" name="ownership">
          <Select options={[{ value: 'Owned', label: 'Owned by transporter' }, { value: 'Attached', label: 'Attached (owner-operator)' }, { value: 'Market', label: 'Market hired' }]} />
        </Form.Item>
        <Flex gap={16}>
          <Form.Item label="Make" name="make" style={{ flex: 1 }}><Input placeholder="e.g. Tata" /></Form.Item>
          <Form.Item label="Year" name="yearOfManufacture" style={{ width: 120 }}><InputNumber min={1980} max={new Date().getFullYear() + 1} controls={false} style={{ width: '100%' }} /></Form.Item>
        </Flex>
        <Form.Item label="Availability" name="availability" extra="Vehicles in maintenance or off the road are not planned until the date they return.">
          <Select aria-label="Availability" options={availabilityOptions} virtual={false} />
        </Form.Item>
        <Form.Item label="Available from – until" name="window" extra="Optional. For a vehicle that is out of service, the first date it is back.">
          <DatePicker.RangePicker allowEmpty={[true, true]} aria-label="Availability window" style={{ width: '100%' }} format="DD MMM YYYY" />
        </Form.Item>
        <Form.Item label="Note" name="availabilityNote"><Input maxLength={200} placeholder="e.g. Gearbox repair" /></Form.Item>
        {vehicle && <Form.Item label="Active" name="isActive" valuePropName="checked" extra="Inactive vehicles are not offered for allocation."><Switch /></Form.Item>}
      </Form>
    </Drawer>
  )
}

interface DriverForm {
  fullName: string
  phone: string
  licenseNumber?: string
  isActive: boolean
}

function DriverDrawer({ transporterId, driver, open, onClose }: { transporterId: string; driver: DriverDto | null; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<DriverForm>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(driver ? { fullName: driver.fullName, phone: driver.phone, licenseNumber: driver.licenseNumber ?? undefined, isActive: driver.isActive } : { isActive: true })
  }, [open, driver, form])

  const save = useMutation({
    mutationFn: (v: DriverForm) => {
      const body = { ...v, licenseNumber: v.licenseNumber?.trim() || null, version: driver?.version ?? null }
      return driver ? transportersApi.updateDriver(driver.id, body) : transportersApi.createDriver(transporterId, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success(driver ? 'Driver updated' : 'Driver added')
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Drawer open={open} onClose={onClose} size={480} destroyOnHidden title={driver ? 'Edit driver' : 'Add driver'}
      footer={<Flex justify="flex-end" gap={8}><Button onClick={onClose}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{driver ? 'Save' : 'Add driver'}</Button></Flex>}>
      <Form<DriverForm> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)} disabled={save.isPending}>
        <Form.Item label="Full name" name="fullName" rules={[{ required: true, whitespace: true, message: 'Enter the driver’s name' }]}><Input autoFocus /></Form.Item>
        <Form.Item label="Mobile" name="phone" rules={[{ required: true, message: 'Enter a mobile number' }]}><Input inputMode="tel" placeholder="98765 43210" /></Form.Item>
        <Form.Item label="Driving licence number" name="licenseNumber" normalize={(v?: string) => v?.toUpperCase()}><Input /></Form.Item>
        {driver && <Form.Item label="Active" name="isActive" valuePropName="checked"><Switch /></Form.Item>}
      </Form>
    </Drawer>
  )
}

interface Props {
  transporterId: string
  /** Opens the upload dialog on the Documents tab for this vehicle or driver. */
  onUploadFor: (kind: OwnerKind, ownerId: string) => void
}

export function FleetTab({ transporterId, onUploadFor }: Props) {
  const [vehicleDrawer, setVehicleDrawer] = useState<{ open: boolean; vehicle: VehicleDto | null }>({ open: false, vehicle: null })
  const [driverDrawer, setDriverDrawer] = useState<{ open: boolean; driver: DriverDto | null }>({ open: false, driver: null })
  const [vPage, setVPage] = useState(1)
  const [dPage, setDPage] = useState(1)

  const vehicles = useQuery({ queryKey: queryKeys.transporters.vehicles(transporterId, vPage), queryFn: () => transportersApi.vehicles(transporterId, { page: vPage, pageSize: 10 }), placeholderData: (p) => p })
  const drivers = useQuery({ queryKey: queryKeys.transporters.drivers(transporterId, dPage), queryFn: () => transportersApi.drivers(transporterId, { page: dPage, pageSize: 10 }), placeholderData: (p) => p })

  const vehicleColumns: TableColumnsType<VehicleDto> = [
    { title: 'Registration', dataIndex: 'registrationNumber', render: (r: string) => <Typography.Text strong>{r}</Typography.Text> },
    { title: 'Type', dataIndex: 'vehicleTypeName' },
    { title: 'Ownership', dataIndex: 'ownership', responsive: ['lg'] },
    { title: 'Papers', key: 'compliance', render: (_, v) => <ComplianceTag compliance={v.compliance} /> },
    { title: 'Status', dataIndex: 'isActive', render: (a: boolean) => <Badge status={a ? 'success' : 'default'} text={a ? 'Active' : 'Inactive'} /> },
    { title: 'Availability', key: 'availability', render: (_, v) => <Tag color={!v.availability || v.availability === 'Available' ? 'green' : 'orange'}>{availabilityText(v)}</Tag> },
    {
      title: '', key: 'actions', width: 110,
      render: (_, v) => (
        <Flex gap={4}>
          <Button type="text" icon={<UploadOutlined />} aria-label={`Upload papers for ${v.registrationNumber}`} onClick={() => onUploadFor('Vehicle', v.id)} />
          <Button type="text" icon={<EditOutlined />} aria-label={`Edit ${v.registrationNumber}`} onClick={() => setVehicleDrawer({ open: true, vehicle: v })} />
        </Flex>
      ),
    },
  ]

  const driverColumns: TableColumnsType<DriverDto> = [
    { title: 'Driver', dataIndex: 'fullName', render: (n: string) => <Typography.Text strong>{n}</Typography.Text> },
    { title: 'Mobile', dataIndex: 'phone' },
    { title: 'Licence', dataIndex: 'licenseNumber', responsive: ['lg'], render: (l: string | null) => l ?? '—' },
    { title: 'Papers', key: 'compliance', render: (_, d) => <ComplianceTag compliance={d.compliance} /> },
    { title: 'Status', dataIndex: 'isActive', render: (a: boolean) => <Badge status={a ? 'success' : 'default'} text={a ? 'Active' : 'Inactive'} /> },
    {
      title: '', key: 'actions', width: 110,
      render: (_, d) => (
        <Flex gap={4}>
          <Button type="text" icon={<UploadOutlined />} aria-label={`Upload licence for ${d.fullName}`} onClick={() => onUploadFor('Driver', d.id)} />
          <Button type="text" icon={<EditOutlined />} aria-label={`Edit ${d.fullName}`} onClick={() => setDriverDrawer({ open: true, driver: d })} />
        </Flex>
      ),
    },
  ]

  return (
    <Flex vertical gap={16}>
      <Card title="Vehicles" extra={<Button icon={<PlusOutlined />} onClick={() => setVehicleDrawer({ open: true, vehicle: null })}>Add vehicle</Button>} styles={{ body: { padding: 0 } }}>
        <Table<VehicleDto> rowKey="id" columns={vehicleColumns} dataSource={vehicles.data?.items} loading={vehicles.isFetching} scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'No vehicles yet' }}
          pagination={{ current: vPage, pageSize: 10, total: vehicles.data?.totalCount ?? 0, onChange: setVPage, hideOnSinglePage: true }} />
      </Card>
      <Card title="Drivers" extra={<Button icon={<PlusOutlined />} onClick={() => setDriverDrawer({ open: true, driver: null })}>Add driver</Button>} styles={{ body: { padding: 0 } }}>
        <Table<DriverDto> rowKey="id" columns={driverColumns} dataSource={drivers.data?.items} loading={drivers.isFetching} scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'No drivers yet' }}
          pagination={{ current: dPage, pageSize: 10, total: drivers.data?.totalCount ?? 0, onChange: setDPage, hideOnSinglePage: true }} />
      </Card>
      <VehicleDrawer transporterId={transporterId} vehicle={vehicleDrawer.vehicle} open={vehicleDrawer.open} onClose={() => setVehicleDrawer((s) => ({ ...s, open: false }))} />
      <DriverDrawer transporterId={transporterId} driver={driverDrawer.driver} open={driverDrawer.open} onClose={() => setDriverDrawer((s) => ({ ...s, open: false }))} />
    </Flex>
  )
}
