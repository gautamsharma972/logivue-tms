import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { Alert, Button, Drawer, Flex, Form, InputNumber, Radio, Select, Switch, Typography } from 'antd'
import { useEffect } from 'react'
import type { ContractType, PlaceDto, RateInputDto, ZoneDto } from '@/lib/api/types'
import { PlaceInput } from './PlaceInput'
import { type RateForm, type SlabRow, toForm, toPricing } from './rateForm'

const money = { min: 0, precision: 2, style: { width: '100%' }, controls: false, prefix: '₹' } as const

function SlabEditor() {
  const form = Form.useFormInstance<RateForm>()
  const slabs = (Form.useWatch('slabs', form) ?? []) as SlabRow[]

  return (
    <Form.List name="slabs">
      {(fields, { add, remove }) => (
        <Flex vertical gap={8}>
          {fields.map((field, i) => {
            const last = i === fields.length - 1
            const from = i === 0 ? 0 : slabs[i - 1]?.toKg ?? 0
            return (
              <Flex key={field.key} gap={8} align="center" wrap>
                <Typography.Text style={{ width: 90 }} type="secondary">{last ? `Above ${from} kg` : `${from} – `}</Typography.Text>
                {!last && (
                  <Form.Item name={[field.name, 'toKg']} noStyle rules={[{ required: true, message: 'Upper limit' }, { type: 'number', min: from + 0.01, message: `More than ${from}` }]}>
                    <InputNumber placeholder="up to kg" min={from + 0.01} precision={2} style={{ width: 120 }} suffix="kg" />
                  </Form.Item>
                )}
                <Form.Item name={[field.name, 'ratePerKg']} noStyle rules={[{ required: true, message: 'Rate' }]}>
                  <InputNumber placeholder="rate" min={0.0001} precision={4} style={{ width: 140 }} prefix="₹" suffix="/kg" />
                </Form.Item>
                {fields.length > 1 && !last && <Button type="text" danger aria-label="Remove slab" icon={<DeleteOutlined />} onClick={() => remove(field.name)} />}
              </Flex>
            )
          })}
          <Button
            type="dashed"
            icon={<PlusOutlined />}
            disabled={fields.length >= 30}
            onClick={() => {
              // New slabs go before the open-ended last one, continuing from the previous upper limit.
              const rows = form.getFieldValue('slabs') as SlabRow[]
              const previousTo = rows[rows.length - 2]?.toKg ?? 0
              add({ toKg: previousTo + 100, ratePerKg: rows[rows.length - 1]?.ratePerKg ?? null }, fields.length - 1)
            }}
          >
            Add slab
          </Button>
        </Flex>
      )}
    </Form.List>
  )
}

interface Props {
  open: boolean
  type: ContractType
  rate: RateInputDto | null
  zones: ZoneDto[]
  vehicleTypes: { id: string; name: string }[]
  onClose: () => void
  onSave: (rate: RateInputDto) => void
}

export function RateDrawer({ open, type, rate, zones, vehicleTypes, onClose, onSave }: Props) {
  const [form] = Form.useForm<RateForm>()
  const basis = Form.useWatch('ftlBasis', form) as RateForm['ftlBasis'] | undefined
  const needsVehicle = type !== 'Ptl'

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(toForm(rate))
  }, [open, rate, form])

  const submit = (v: RateForm) =>
    onSave({
      origin: v.origin,
      destination: v.destination,
      bothWays: v.bothWays,
      vehicleTypeId: needsVehicle ? (v.vehicleTypeId ?? null) : null,
      minDistanceKm: v.minDistanceKm,
      maxDistanceKm: v.maxDistanceKm,
      pricing: toPricing(type, v),
    })

  const placeRule = (label: string) => ({
    validator: (_: unknown, p: PlaceDto) => {
      if (p.kind === 'City' && (!p.state || !p.city?.trim())) return Promise.reject(new Error(`${label}: choose a state and enter the city`))
      if (p.kind === 'State' && !p.state) return Promise.reject(new Error(`${label}: choose the state`))
      if (p.kind === 'Zone' && !p.zoneCode) return Promise.reject(new Error(`${label}: choose the zone`))
      return Promise.resolve()
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={640}
      destroyOnHidden
      title={rate ? 'Edit rate' : 'Add rate'}
      footer={<Flex justify="flex-end" gap={8}><Button onClick={onClose}>Cancel</Button><Button type="primary" onClick={() => form.submit()}>{rate ? 'Update rate' : 'Add rate'}</Button></Flex>}
    >
      <Form<RateForm> form={form} layout="vertical" requiredMark="optional" onFinish={submit}>
        <Alert type="info" showIcon style={{ marginBottom: 16 }} title="When several rates fit a shipment the most specific wins: city → zone → state → anywhere, then vehicle-specific, then distance-banded." />
        <Form.Item label="From" name="origin" required validateTrigger="onSubmit" rules={[placeRule('From')]}><PlaceInput zones={zones} /></Form.Item>
        <Form.Item label="To" name="destination" required validateTrigger="onSubmit" rules={[placeRule('To')]}><PlaceInput zones={zones} /></Form.Item>
        <Form.Item name="bothWays" valuePropName="checked" extra="Use the same rate for loads going the other way."><Switch checkedChildren="Both ways" unCheckedChildren="One way" /></Form.Item>

        {needsVehicle && (
          <Form.Item label="Vehicle type" name="vehicleTypeId" rules={[{ required: true, message: 'Choose the vehicle type' }]}>
            <Select showSearch optionFilterProp="label" options={vehicleTypes.map((t) => ({ value: t.id, label: t.name }))} />
          </Form.Item>
        )}

        <Flex gap={16} wrap>
          <Form.Item label="Distance from (km)" name="minDistanceKm" extra="Optional: limit this rate to a distance band." style={{ flex: '1 1 180px' }}><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
          <Form.Item label="Distance to (km)" name="maxDistanceKm" style={{ flex: '1 1 180px' }}><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
        </Flex>

        <Typography.Title level={5}>Price</Typography.Title>
        {type === 'Ftl' && (
          <>
            <Form.Item name="ftlBasis"><Radio.Group optionType="button" options={[{ value: 'flatTrip', label: 'Fixed price per trip' }, { value: 'perKm', label: 'Per kilometre' }]} /></Form.Item>
            {basis === 'perKm' ? (
              <Flex gap={16} wrap>
                <Form.Item label="Rate per km" name="ratePerKm" rules={[{ required: true, message: 'Enter the rate' }]} style={{ flex: '1 1 150px' }}><InputNumber {...money} /></Form.Item>
                <Form.Item label="Minimum km billed" name="minKm" style={{ flex: '1 1 150px' }}><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
                <Form.Item label="Minimum charge" name="minCharge" style={{ flex: '1 1 150px' }}><InputNumber {...money} /></Form.Item>
              </Flex>
            ) : (
              <Form.Item label="Price per trip" name="amountPerTrip" rules={[{ required: true, message: 'Enter the trip price' }]}><InputNumber {...money} /></Form.Item>
            )}
          </>
        )}
        {type === 'Ptl' && (
          <>
            <Form.Item label="How slabs apply" name="slabMode" extra="“Whole weight” charges all the weight at the rate of the slab it falls in; “slab by slab” charges each slice at its own rate.">
              <Radio.Group optionType="button" options={[{ value: 'Whole', label: 'Whole weight' }, { value: 'Incremental', label: 'Slab by slab' }]} />
            </Form.Item>
            <Form.Item label="Weight slabs" required extra="A weight belongs to a slab if it is above the lower limit and up to the upper limit."><SlabEditor /></Form.Item>
            <Flex gap={16} wrap>
              <Form.Item label="Minimum charge per consignment" name="slabMinCharge" style={{ flex: '1 1 200px' }}><InputNumber {...money} /></Form.Item>
              <Form.Item label="Minimum chargeable weight (kg)" name="minChargeableKg" style={{ flex: '1 1 200px' }}><InputNumber min={0} precision={2} style={{ width: '100%' }} /></Form.Item>
            </Flex>
          </>
        )}
        {type === 'Dedicated' && (
          <>
            <Form.Item label="Monthly rental" name="monthlyRental" rules={[{ required: true, message: 'Enter the monthly rental' }]}><InputNumber {...money} /></Form.Item>
            <Flex gap={16} wrap>
              <Form.Item label="Kilometres included per month" name="includedKmPerMonth" style={{ flex: '1 1 200px' }}><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item label="Extra km rate" name="extraKmRate" style={{ flex: '1 1 200px' }}><InputNumber {...money} /></Form.Item>
            </Flex>
            <Flex gap={16} wrap>
              <Form.Item label="Hours included per month" name="includedHoursPerMonth" style={{ flex: '1 1 200px' }}><InputNumber min={0} precision={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item label="Extra hour rate" name="extraHourRate" style={{ flex: '1 1 200px' }}><InputNumber {...money} /></Form.Item>
            </Flex>
          </>
        )}
      </Form>
    </Drawer>
  )
}
