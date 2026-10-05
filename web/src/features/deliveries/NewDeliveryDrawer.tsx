import { MinusCircleOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, DatePicker, Drawer, Flex, Form, Input, InputNumber, Select } from 'antd'
import type { Dayjs } from 'dayjs'
import { useEffect } from 'react'
import { deliveriesApi, transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { applyFieldErrors } from '@/lib/formErrors'

interface Values {
  customerName: string
  customerPhone?: string
  customerEmail?: string
  shipmentReference?: string
  orderReference?: string
  transporterId?: string
  vehicleReference?: string
  driverName?: string
  destinationReference?: string
  destinationAddress?: string
  plannedDeliveryAt: Dayjs
  items: { sku: string; description: string; orderedQuantity: number; unitOfMeasure?: string }[]
}

/** Opens a delivery by hand, for work that did not come from a dispatched shipment (a customer collection, a transfer, a load planned elsewhere). */
export function NewDeliveryDrawer({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: string) => void }) {
  const [form] = Form.useForm<Values>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const transporters = useQuery({ queryKey: queryKeys.transporters.list({ page: 1, pageSize: 100, status: 'Active' }), queryFn: () => transportersApi.list({ page: 1, pageSize: 100, status: 'Active' }), enabled: open })

  useEffect(() => {
    if (open) form.resetFields()
  }, [open, form])

  const save = useMutation({
    mutationFn: (v: Values) => {
      const carrier = transporters.data?.items.find((t) => t.id === v.transporterId)
      return deliveriesApi.create({
        shipmentReference: v.shipmentReference?.trim() || null, orderReference: v.orderReference?.trim() || null, loadReference: null, tripReference: null, lrNumber: null, sequence: 1,
        transporterId: v.transporterId ?? null, transporterReference: carrier?.legalName ?? null, vehicleId: null, vehicleReference: v.vehicleReference?.trim().toUpperCase() || null, driverName: v.driverName?.trim() || null,
        customerReference: null, customerName: v.customerName.trim(), customerPhone: v.customerPhone?.trim() || null, customerEmail: v.customerEmail?.trim() || null,
        originReference: null, destinationReference: v.destinationReference?.trim() || null, destinationAddress: v.destinationAddress?.trim() || null,
        customerLatitude: null, customerLongitude: null, geofenceRadiusM: null,
        plannedDeliveryAt: v.plannedDeliveryAt.toISOString(), windowStart: null, windowEnd: null,
        items: v.items.map((i) => ({ sku: i.sku.trim(), description: i.description.trim(), orderedQuantity: i.orderedQuantity, dispatchedQuantity: i.orderedQuantity, unitOfMeasure: i.unitOfMeasure?.trim() || null })),
      })
    },
    onSuccess: async (d) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
      void message.success(`Delivery ${d.summary.number} created`)
      onClose()
      onCreated(d.summary.id)
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Drawer open={open} onClose={onClose} title="New delivery" size="large" destroyOnHidden
      extra={<Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Create</Button>}>
      <Form<Values> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)} initialValues={{ items: [{}] }}>
        <Form.Item label="Customer" name="customerName" rules={[{ required: true, whitespace: true, message: 'Enter the customer' }]}><Input autoFocus /></Form.Item>
        <Flex gap={16} wrap>
          <Form.Item label="Customer phone" name="customerPhone" style={{ flex: '1 1 180px' }}><Input inputMode="tel" /></Form.Item>
          <Form.Item label="Customer email" name="customerEmail" style={{ flex: '1 1 220px' }} extra="The one-time delivery code is sent here." rules={[{ type: 'email', message: 'Enter a valid email' }]}><Input inputMode="email" /></Form.Item>
        </Flex>
        <Flex gap={16} wrap>
          <Form.Item label="Shipment reference" name="shipmentReference" style={{ flex: '1 1 180px' }}><Input /></Form.Item>
          <Form.Item label="Order / invoice reference" name="orderReference" style={{ flex: '1 1 180px' }}><Input /></Form.Item>
        </Flex>
        <Form.Item label="Transporter" name="transporterId" extra="Leave empty to plan it first and assign it later.">
          <Select allowClear showSearch optionFilterProp="label" loading={transporters.isLoading} options={transporters.data?.items.map((t) => ({ value: t.id, label: t.legalName }))} />
        </Form.Item>
        <Flex gap={16} wrap>
          <Form.Item label="Vehicle" name="vehicleReference" style={{ flex: '1 1 160px' }}><Input placeholder="MH12AB1234" /></Form.Item>
          <Form.Item label="Driver" name="driverName" style={{ flex: '1 1 160px' }}><Input /></Form.Item>
        </Flex>
        <Form.Item label="Delivery place" name="destinationReference"><Input placeholder="City" /></Form.Item>
        <Form.Item label="Address" name="destinationAddress"><Input /></Form.Item>
        <Form.Item label="Planned delivery" name="plannedDeliveryAt" rules={[{ required: true, message: 'Choose when it is due' }]}><DatePicker showTime style={{ width: '100%' }} /></Form.Item>
        <Form.List name="items">
          {(fields, { add, remove }) => (
            <>
              {fields.map((field) => (
                <Flex key={field.key} gap={8} wrap align="start">
                  <Form.Item name={[field.name, 'sku']} rules={[{ required: true, message: 'SKU' }]} style={{ flex: '1 1 120px' }}><Input placeholder="SKU" /></Form.Item>
                  <Form.Item name={[field.name, 'description']} rules={[{ required: true, message: 'Describe it' }]} style={{ flex: '2 1 160px' }}><Input placeholder="Description" /></Form.Item>
                  <Form.Item name={[field.name, 'orderedQuantity']} rules={[{ required: true, message: 'Quantity' }]}><InputNumber min={0} placeholder="Qty" /></Form.Item>
                  <Form.Item name={[field.name, 'unitOfMeasure']}><Input placeholder="Unit" style={{ width: 80 }} /></Form.Item>
                  {fields.length > 1 && <Button aria-label="Remove item" type="text" icon={<MinusCircleOutlined />} onClick={() => remove(field.name)} />}
                </Flex>
              ))}
              <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>Add item</Button>
            </>
          )}
        </Form.List>
      </Form>
    </Drawer>
  )
}
