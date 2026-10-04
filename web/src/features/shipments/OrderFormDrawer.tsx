import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, AutoComplete, Button, Checkbox, DatePicker, Drawer, Flex, Form, Input, InputNumber, Radio, Select, TimePicker, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect } from 'react'
import { locationsApi, ordersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { HandlingType, LocationDto, OrderDirection, OrderDto, OrderPriority, PartyDto, ReturnType } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { INDIAN_STATES } from '@/lib/indiaStates'

interface FormValues {
  direction: OrderDirection
  pickupLocationId?: string | null
  dropLocationId?: string | null
  reference?: string
  pickup: PartyDto
  drop: PartyDto
  weightKg: number
  volumeCbm?: number | null
  packages?: number | null
  description: string
  readyDate: Dayjs
  deliverByDate?: Dayjs | null
  deliveryWindow?: [Dayjs, Dayjs] | null
  notes?: string
  priority: OrderPriority
  productCategory?: string
  handling: HandlingType
  isHazardous: boolean
  isStackable: boolean
  longestItemM?: number | null
  returnType?: ReturnType | null
  returnReason?: string
  pickupWindow?: [Dayjs, Dayjs] | null
}

interface Props {
  open: boolean
  /** The order being edited, or null to enter a new one. */
  order: OrderDto | null
  onClose: () => void
}

const priorityOptions: { value: OrderPriority; label: string }[] = [
  { value: 'Low', label: 'Low' }, { value: 'Normal', label: 'Normal' }, { value: 'High', label: 'High' }, { value: 'Urgent', label: 'Urgent' },
]
const handlingOptions: { value: HandlingType; label: string }[] = [
  { value: 'Standard', label: 'Standard' }, { value: 'Fragile', label: 'Fragile' }, { value: 'TemperatureControlled', label: 'Temperature controlled' },
]
const returnTypeOptions: { value: ReturnType; label: string }[] = [
  { value: 'CustomerReturn', label: 'Customer return' }, { value: 'DamagedMaterial', label: 'Damaged material' }, { value: 'RejectedMaterial', label: 'Rejected material' },
  { value: 'EmptyPackaging', label: 'Empty packaging' }, { value: 'SupplierReturn', label: 'Supplier return' }, { value: 'ReplacementPickup', label: 'Replacement pickup' },
]
const categorySuggestions = ['FOOD', 'CHEMICALS', 'PHARMA', 'ELECTRONICS', 'AUTO PARTS', 'FMCG', 'TEXTILES'].map((c) => ({ value: c }))

const stateOptions = INDIAN_STATES.map((s) => ({ value: s, label: s }))
const emptyParty = { name: '', line1: '', city: '', state: undefined, pincode: '', contactName: '', contactPhone: '' }

function PartyFields({ name, title, locations }: { name: 'pickup' | 'drop'; title: string; locations: LocationDto[] }) {
  const form = Form.useFormInstance<FormValues>()
  const locationField = name === 'pickup' ? 'pickupLocationId' : 'dropLocationId'
  const linked = Form.useWatch(locationField, form) as string | null | undefined
  const label = (text: string) => `${title} ${text}`

  const choose = (id: string | undefined) => {
    const found = locations.find((l) => l.id === id)
    form.setFieldValue(locationField, id ?? null)
    if (found) {
      form.setFieldValue([name], { ...form.getFieldValue([name]), name: found.name, line1: found.line1, city: found.city, state: found.state, pincode: found.pincode })
    }
  }

  return (
    <>
      <Typography.Title level={5}>{title}</Typography.Title>
      <Form.Item name={locationField} label="Saved location" extra="Pick one to fill the address and give planning its coordinates, or leave empty to type an address.">
        <Select
          aria-label={label('location')}
          allowClear
          showSearch
          virtual={false}
          optionFilterProp="label"
          placeholder="Choose a location"
          options={locations.map((l) => ({ value: l.id, label: `${l.name} · ${l.city} (${l.code})` }))}
          onChange={choose}
        />
      </Form.Item>
      <Form.Item name={[name, 'name']} label="Name" rules={[{ required: true, message: 'Enter a name' }]}>
        <Input aria-label={label('name')} maxLength={200} disabled={!!linked} />
      </Form.Item>
      <Form.Item name={[name, 'line1']} label="Address" rules={[{ required: true, message: 'Enter the address' }]}>
        <Input aria-label={label('address')} maxLength={200} disabled={!!linked} />
      </Form.Item>
      <Flex gap={12} wrap>
        <Form.Item name={[name, 'city']} label="City" rules={[{ required: true, message: 'Enter the city' }]} style={{ flex: 1, minWidth: 160 }}>
          <Input aria-label={label('city')} maxLength={100} disabled={!!linked} />
        </Form.Item>
        <Form.Item name={[name, 'state']} label="State" rules={[{ required: true, message: 'Choose the state' }]} style={{ flex: 1, minWidth: 180 }}>
          <Select aria-label={label('state')} showSearch virtual={false} options={stateOptions} disabled={!!linked} />
        </Form.Item>
        <Form.Item name={[name, 'pincode']} label="Pincode" rules={[{ required: true, pattern: /^[1-9]\d{5}$/, message: 'Enter a 6-digit pincode' }]} style={{ width: 130 }}>
          <Input aria-label={label('pincode')} maxLength={6} inputMode="numeric" disabled={!!linked} />
        </Form.Item>
      </Flex>
      <Flex gap={12} wrap>
        <Form.Item name={[name, 'contactName']} label="Contact" style={{ flex: 1, minWidth: 160 }}>
          <Input maxLength={100} />
        </Form.Item>
        <Form.Item name={[name, 'contactPhone']} label="Contact mobile" style={{ flex: 1, minWidth: 160 }}>
          <Input maxLength={15} inputMode="tel" />
        </Form.Item>
      </Flex>
    </>
  )
}

export function OrderFormDrawer({ open, order, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const editing = order !== null
  const direction = Form.useWatch('direction', form)
  const locations = useQuery({ queryKey: queryKeys.locations.active, queryFn: () => locationsApi.list({ active: true, pageSize: 200 }), enabled: open })

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      order
        ? {
            direction: order.direction,
            pickupLocationId: order.pickupLocationId ?? null,
            dropLocationId: order.dropLocationId ?? null,
            reference: order.reference ?? '',
            pickup: { ...order.pickup, contactName: order.pickup.contactName ?? '', contactPhone: order.pickup.contactPhone ?? '' },
            drop: { ...order.drop, contactName: order.drop.contactName ?? '', contactPhone: order.drop.contactPhone ?? '' },
            weightKg: order.weightKg,
            volumeCbm: order.volumeCbm,
            packages: order.packages,
            description: order.description,
            readyDate: dayjs(order.readyDate),
            deliverByDate: order.deliverByDate ? dayjs(order.deliverByDate) : null,
            deliveryWindow: order.deliveryWindowFrom && order.deliveryWindowTo ? [dayjs(order.deliveryWindowFrom, 'HH:mm:ss'), dayjs(order.deliveryWindowTo, 'HH:mm:ss')] : null,
            notes: order.notes ?? '',
            priority: order.priority ?? 'Normal',
            productCategory: order.productCategory ?? '',
            handling: order.handling ?? 'Standard',
            isHazardous: order.isHazardous ?? false,
            isStackable: order.isStackable ?? true,
            longestItemM: order.longestItemM ?? null,
            returnType: order.returnType ?? null,
            returnReason: order.returnReason ?? '',
            pickupWindow: order.pickupWindowFrom && order.pickupWindowTo ? [dayjs(order.pickupWindowFrom, 'HH:mm:ss'), dayjs(order.pickupWindowTo, 'HH:mm:ss')] : null,
          }
        : { direction: 'Forward', priority: 'Normal', handling: 'Standard', isHazardous: false, isStackable: true, pickup: emptyParty as unknown as PartyDto, drop: emptyParty as unknown as PartyDto, readyDate: dayjs() },
    )
  }, [open, order, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const party = (p: PartyDto): PartyDto => ({
        ...p,
        name: p.name.trim(),
        line1: p.line1.trim(),
        city: p.city.trim(),
        contactName: p.contactName?.trim() || null,
        contactPhone: p.contactPhone?.trim() || null,
      })
      const body = {
        direction: v.direction,
        pickupLocationId: v.pickupLocationId ?? null,
        dropLocationId: v.dropLocationId ?? null,
        reference: v.reference?.trim() || null,
        pickup: party(v.pickup),
        drop: party(v.drop),
        weightKg: v.weightKg,
        volumeCbm: v.volumeCbm ?? null,
        packages: v.packages ?? null,
        description: v.description.trim(),
        readyDate: v.readyDate.format('YYYY-MM-DD'),
        deliverByDate: v.deliverByDate ? v.deliverByDate.format('YYYY-MM-DD') : null,
        deliveryWindowFrom: v.deliveryWindow ? v.deliveryWindow[0].format('HH:mm:ss') : null,
        deliveryWindowTo: v.deliveryWindow ? v.deliveryWindow[1].format('HH:mm:ss') : null,
        notes: v.notes?.trim() || null,
        priority: v.priority,
        productCategory: v.productCategory?.trim() || null,
        handling: v.handling,
        isHazardous: v.isHazardous,
        isStackable: v.isStackable,
        longestItemM: v.longestItemM ?? null,
        returnType: v.direction === 'Reverse' ? (v.returnType ?? null) : null,
        returnReason: v.direction === 'Reverse' ? (v.returnReason?.trim() || null) : null,
        pickupWindowFrom: v.pickupWindow ? v.pickupWindow[0].format('HH:mm:ss') : null,
        pickupWindowTo: v.pickupWindow ? v.pickupWindow[1].format('HH:mm:ss') : null,
      }
      return order ? ordersApi.update(order.id, body) : ordersApi.create(body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.suggestions })
      void message.success(editing ? 'Order updated' : `Order ${saved.number} created`)
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={640}
      destroyOnHidden
      maskClosable={!save.isPending}
      title={editing ? `Edit ${order.number}` : 'New order'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{editing ? 'Save changes' : 'Create order'}</Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}>
        <Flex gap={12} wrap>
          <Form.Item name="direction" label="Direction" style={{ flex: 1, minWidth: 220 }}>
            <Radio.Group optionType="button" options={[{ value: 'Forward', label: 'Outbound' }, { value: 'Reverse', label: 'Return / reverse' }]} />
          </Form.Item>
          <Form.Item name="reference" label="Your reference (PO / invoice)" style={{ flex: 1, minWidth: 200 }}>
            <Input maxLength={64} />
          </Form.Item>
        </Flex>
        <PartyFields name="pickup" title="Pickup" locations={locations.data?.items ?? []} />
        <PartyFields name="drop" title="Delivery" locations={locations.data?.items ?? []} />
        <Typography.Title level={5}>Goods</Typography.Title>
        <Form.Item name="description" label="Description" rules={[{ required: true, message: 'Describe the goods' }]}>
          <Input maxLength={300} />
        </Form.Item>
        <Flex gap={12} wrap>
          <Form.Item name="weightKg" label="Weight (kg)" rules={[{ required: true, message: 'Enter the weight' }]} style={{ flex: 1, minWidth: 130 }}>
            <InputNumber min={0.01} precision={2} style={{ width: '100%' }} controls={false} />
          </Form.Item>
          <Form.Item name="volumeCbm" label="Volume (CBM)" style={{ flex: 1, minWidth: 130 }}>
            <InputNumber min={0} precision={3} style={{ width: '100%' }} controls={false} />
          </Form.Item>
          <Form.Item name="packages" label="Packages" style={{ flex: 1, minWidth: 130 }}>
            <InputNumber min={1} precision={0} style={{ width: '100%' }} controls={false} />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="readyDate" label="Ready for pickup" rules={[{ required: true, message: 'Choose a date' }]} style={{ flex: 1, minWidth: 180 }}>
            <DatePicker style={{ width: '100%' }} format="DD MMM YYYY" />
          </Form.Item>
          <Form.Item name="deliverByDate" label="Deliver by" style={{ flex: 1, minWidth: 180 }}>
            <DatePicker style={{ width: '100%' }} format="DD MMM YYYY" />
          </Form.Item>
        </Flex>
        <Form.Item name="deliveryWindow" label="Consignee accepts deliveries between" extra="Optional. The truck waits if it arrives early; if it arrives after closing, the delivery moves to the next morning.">
          <TimePicker.RangePicker aria-label="Delivery window" format="HH:mm" minuteStep={15} style={{ width: '100%' }} />
        </Form.Item>
        <Typography.Title level={5}>Planning attributes</Typography.Title>
        <Flex gap={12} wrap>
          <Form.Item name="priority" label="Priority" extra="Urgent orders are planned first and keep their deadline." style={{ flex: 1, minWidth: 160 }}>
            <Select aria-label="Priority" options={priorityOptions} virtual={false} />
          </Form.Item>
          <Form.Item name="handling" label="Handling" style={{ flex: 1, minWidth: 200 }}>
            <Select aria-label="Handling" options={handlingOptions} virtual={false} />
          </Form.Item>
          <Form.Item name="productCategory" label="Product category" extra="Used by compatibility rules (e.g. FOOD never with CHEMICALS)." style={{ flex: 1, minWidth: 200 }}>
            <AutoComplete aria-label="Product category" options={categorySuggestions} maxLength={50} filterOption />
          </Form.Item>
        </Flex>
        <Flex gap={24} wrap align="center">
          <Form.Item name="isHazardous" valuePropName="checked"><Checkbox>Hazardous goods</Checkbox></Form.Item>
          <Form.Item name="isStackable" valuePropName="checked"><Checkbox>Can be stacked</Checkbox></Form.Item>
          <Form.Item name="longestItemM" label="Longest item (m)" style={{ width: 160 }}>
            <InputNumber min={0.1} max={30} precision={2} style={{ width: '100%' }} controls={false} />
          </Form.Item>
        </Flex>
        {direction === 'Reverse' && (
          <>
            <Typography.Title level={5}>Return details</Typography.Title>
            <Flex gap={12} wrap>
              <Form.Item name="returnType" label="Return type" rules={[{ required: true, message: 'Choose the return type' }]} style={{ flex: 1, minWidth: 200 }}>
                <Select aria-label="Return type" options={returnTypeOptions} virtual={false} />
              </Form.Item>
              <Form.Item name="pickupWindow" label="Can be collected between" extra="Optional." style={{ flex: 1, minWidth: 220 }}>
                <TimePicker.RangePicker aria-label="Pickup window" format="HH:mm" minuteStep={15} style={{ width: '100%' }} />
              </Form.Item>
            </Flex>
            <Form.Item name="returnReason" label="Reason for return" rules={[{ required: true, message: 'Say why the goods are being returned' }]}>
              <Input aria-label="Reason for return" maxLength={300} />
            </Form.Item>
          </>
        )}
        <Form.Item name="notes" label="Notes">
          <Input.TextArea rows={2} maxLength={1000} />
        </Form.Item>
      </Form>
    </Drawer>
  )
}
