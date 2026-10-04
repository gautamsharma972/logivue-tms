import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Button, Drawer, Flex, Form, Input, InputNumber, Select, Switch } from 'antd'
import { useEffect } from 'react'
import { locationsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { LocationDto, LocationType } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { INDIAN_STATES } from '@/lib/indiaStates'

export const locationTypes: LocationType[] = ['Depot', 'Plant', 'Warehouse', 'Customer', 'Supplier', 'Other']

interface FormValues {
  code: string
  name: string
  type: LocationType
  line1: string
  city: string
  state: string
  pincode: string
  latitude: number
  longitude: number
  isActive: boolean
}

interface Props {
  open: boolean
  /** The location being edited, or null to add one. */
  location: LocationDto | null
  onClose: () => void
}

export function LocationFormDrawer({ open, location, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const editing = location !== null

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(location ?? { type: 'Customer', isActive: true })
  }, [open, location, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = { ...v, code: v.code.trim(), name: v.name.trim(), line1: v.line1.trim(), city: v.city.trim(), version: location?.version ?? null }
      return location ? locationsApi.update(location.id, body) : locationsApi.create(body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.locations.all })
      void message.success(editing ? 'Location updated' : `Location ${saved.code} added`)
      onClose()
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      if (error.code === 'concurrency.conflict') {
        await queryClient.invalidateQueries({ queryKey: queryKeys.locations.all })
        onClose()
      }
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={560}
      destroyOnHidden
      maskClosable={!save.isPending}
      title={editing ? `Edit ${location.code}` : 'New location'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{editing ? 'Save changes' : 'Add location'}</Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}>
        <Flex gap={12} wrap>
          <Form.Item name="code" label="Code" rules={[{ required: true, message: 'Enter a short code' }]} extra="Unique, e.g. PUNE-DC" style={{ flex: 1, minWidth: 160 }}>
            <Input maxLength={30} />
          </Form.Item>
          <Form.Item name="type" label="Type" style={{ flex: 1, minWidth: 160 }}>
            <Select aria-label="Type" virtual={false} options={locationTypes.map((t) => ({ value: t, label: t }))} />
          </Form.Item>
        </Flex>
        <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Enter a name' }]}>
          <Input maxLength={200} />
        </Form.Item>
        <Form.Item name="line1" label="Address" rules={[{ required: true, message: 'Enter the address' }]}>
          <Input maxLength={200} />
        </Form.Item>
        <Flex gap={12} wrap>
          <Form.Item name="city" label="City" rules={[{ required: true, message: 'Enter the city' }]} style={{ flex: 1, minWidth: 150 }}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="state" label="State" rules={[{ required: true, message: 'Choose the state' }]} style={{ flex: 1, minWidth: 180 }}>
            <Select aria-label="State" showSearch virtual={false} options={INDIAN_STATES.map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Form.Item name="pincode" label="Pincode" rules={[{ required: true, pattern: /^[1-9]\d{5}$/, message: 'Enter a 6-digit pincode' }]} style={{ width: 130 }}>
            <Input maxLength={6} inputMode="numeric" />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="latitude" label="Latitude" rules={[{ required: true, message: 'Enter the latitude' }]} extra="e.g. 18.5204" style={{ flex: 1, minWidth: 150 }}>
            <InputNumber min={-90} max={90} precision={6} controls={false} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="longitude" label="Longitude" rules={[{ required: true, message: 'Enter the longitude' }]} extra="e.g. 73.8567" style={{ flex: 1, minWidth: 150 }}>
            <InputNumber min={-180} max={180} precision={6} controls={false} style={{ width: '100%' }} />
          </Form.Item>
        </Flex>
        {editing && (
          <Form.Item name="isActive" label="Active" valuePropName="checked" extra="Inactive locations cannot be chosen on new orders.">
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Drawer>
  )
}
