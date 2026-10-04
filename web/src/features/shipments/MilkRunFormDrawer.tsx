import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, Drawer, Flex, Form, Input, InputNumber, Select, Switch, TimePicker, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect } from 'react'
import { locationsApi, milkRunsApi, planningApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DayName, MilkRunDto, MilkRunStopType } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

const days: DayName[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

interface StopValue {
  locationId?: string
  type: MilkRunStopType
  serviceMinutes: number
  window?: [Dayjs, Dayjs] | null
}

interface FormValues {
  code: string
  name: string
  depotLocationId: string
  vehicleTypeId?: string
  maxStops: number
  maxHours: number
  departure: Dayjs
  days: DayName[]
  stops: StopValue[]
  isActive: boolean
}

interface Props {
  open: boolean
  /** The milk run being edited, or null to create one. */
  milkRun: MilkRunDto | null
  onClose: () => void
}

export function MilkRunFormDrawer({ open, milkRun, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const editing = milkRun !== null

  const locations = useQuery({ queryKey: queryKeys.locations.active, queryFn: () => locationsApi.list({ active: true, pageSize: 200 }), enabled: open })
  const vehicleTypes = useQuery({ queryKey: queryKeys.shipments.vehicleTypes, queryFn: () => planningApi.vehicleTypes(), enabled: open })
  const depot = Form.useWatch('depotLocationId', form) as string | undefined
  const stopValues = Form.useWatch('stops', form) as StopValue[] | undefined

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      milkRun
        ? {
            code: milkRun.code,
            name: milkRun.name,
            depotLocationId: milkRun.depotLocationId,
            vehicleTypeId: milkRun.vehicleTypeId ?? undefined,
            maxStops: milkRun.maxStops,
            maxHours: milkRun.maxDurationMinutes / 60,
            departure: dayjs(milkRun.departureTime, 'HH:mm:ss'),
            days: milkRun.days,
            isActive: milkRun.isActive,
            stops: milkRun.stops.map((s) => ({
              locationId: s.locationId,
              type: s.type,
              serviceMinutes: s.serviceMinutes,
              window: s.windowFrom && s.windowTo ? [dayjs(s.windowFrom, 'HH:mm:ss'), dayjs(s.windowTo, 'HH:mm:ss')] : null,
            })),
          }
        : { maxStops: 8, maxHours: 10, departure: dayjs('07:00', 'HH:mm'), days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'], isActive: true, stops: [{ type: 'Pickup', serviceMinutes: 30 }] },
    )
  }, [open, milkRun, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = {
        code: v.code.trim(),
        name: v.name.trim(),
        depotLocationId: v.depotLocationId,
        vehicleTypeId: v.vehicleTypeId ?? null,
        maxStops: v.maxStops,
        maxDurationMinutes: Math.round(v.maxHours * 60),
        departureTime: v.departure.format('HH:mm:ss'),
        days: v.days,
        stops: v.stops.map((s) => ({
          locationId: s.locationId!,
          type: s.type,
          serviceMinutes: s.serviceMinutes,
          windowFrom: s.window ? s.window[0].format('HH:mm:ss') : null,
          windowTo: s.window ? s.window[1].format('HH:mm:ss') : null,
        })),
        isActive: v.isActive,
        version: milkRun?.version ?? null,
      }
      return milkRun ? milkRunsApi.update(milkRun.id, body) : milkRunsApi.create(body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.milkRuns.all })
      void message.success(editing ? 'Milk run updated' : `Milk run ${saved.code} created`)
      onClose()
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error, { maxDurationMinutes: 'maxHours', depotLocationId: 'depotLocationId' })) {
        const general = error.fieldErrors.stops?.[0]
        if (general) void message.error(general)
        return
      }
      if (error.code === 'concurrency.conflict') {
        await queryClient.invalidateQueries({ queryKey: queryKeys.milkRuns.all })
        onClose()
      }
      void message.error(error.message)
    },
  })

  const options = (locations.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.name} · ${l.city} (${l.code})` }))
  const used = new Set((stopValues ?? []).map((s) => s?.locationId).filter(Boolean))

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={720}
      destroyOnHidden
      maskClosable={!save.isPending}
      title={editing ? `Edit ${milkRun.code}` : 'New milk run'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{editing ? 'Save changes' : 'Create milk run'}</Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}>
        <Flex gap={12} wrap>
          <Form.Item name="code" label="Code" rules={[{ required: true, message: 'Enter a short code' }]} style={{ flex: 1, minWidth: 160 }}>
            <Input maxLength={30} />
          </Form.Item>
          <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Enter a name' }]} style={{ flex: 2, minWidth: 220 }}>
            <Input maxLength={200} />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="depotLocationId" label="Depot (start and end)" rules={[{ required: true, message: 'Choose the depot' }]} style={{ flex: 1, minWidth: 260 }}>
            <Select aria-label="Depot" showSearch virtual={false} optionFilterProp="label" options={options} />
          </Form.Item>
          <Form.Item name="vehicleTypeId" label="Usual vehicle" extra="Each day's plan may choose another." style={{ flex: 1, minWidth: 220 }}>
            <Select aria-label="Usual vehicle" allowClear virtual={false} placeholder="No preference" options={vehicleTypes.data?.map((t) => ({ value: t.id, label: t.name }))} />
          </Form.Item>
        </Flex>
        <Flex gap={12} wrap>
          <Form.Item name="maxStops" label="Max stops per vehicle" style={{ width: 170 }}>
            <InputNumber min={1} max={50} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="maxHours" label="Max duration (hours)" style={{ width: 170 }}>
            <InputNumber min={0.5} max={24} step={0.5} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="departure" label="Departs at" style={{ width: 140 }}>
            <TimePicker aria-label="Departure time" format="HH:mm" minuteStep={15} allowClear={false} style={{ width: '100%' }} />
          </Form.Item>
        </Flex>
        <Form.Item name="days" label="Runs on" rules={[{ required: true, type: 'array', min: 1, message: 'Choose at least one day' }]}>
          <Checkbox.Group options={days.map((d) => ({ value: d, label: d.slice(0, 3) }))} />
        </Form.Item>

        <Typography.Title level={5}>Stops, in planned order</Typography.Title>
        <Typography.Paragraph type="secondary">
          A pickup stop collects orders from that location to the depot; a delivery stop takes orders from the depot to that location. Orders are found for a stop when they are linked to its location.
        </Typography.Paragraph>
        <Form.List name="stops" rules={[{ validator: async (_, value: StopValue[] | undefined) => { if (!value || value.length === 0) throw new Error('Add at least one stop') } }]}>
          {(fields, { add, remove, move }, { errors }) => (
            <Flex vertical gap={12}>
              {fields.map((field, index) => (
                <Flex key={field.key} gap={8} wrap align="flex-start" style={{ padding: 12, border: '1px solid var(--ant-color-border, #d9d9d9)', borderRadius: 8 }}>
                  <Typography.Text strong style={{ width: 20, paddingTop: 6 }}>{index + 1}</Typography.Text>
                  <Form.Item name={[field.name, 'locationId']} rules={[{ required: true, message: 'Choose a location' }]} style={{ flex: 2, minWidth: 220, marginBottom: 0 }}>
                    <Select aria-label={`Stop ${index + 1} location`} showSearch virtual={false} optionFilterProp="label" placeholder="Location"
                      options={options.map((o) => ({ ...o, disabled: o.value === depot || (used.has(o.value) && stopValues?.[index]?.locationId !== o.value) }))} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'type']} style={{ width: 130, marginBottom: 0 }}>
                    <Select aria-label={`Stop ${index + 1} type`} virtual={false} options={[{ value: 'Pickup', label: 'Pickup' }, { value: 'Delivery', label: 'Delivery' }]} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'serviceMinutes']} style={{ width: 110, marginBottom: 0 }}>
                    <InputNumber aria-label={`Stop ${index + 1} minutes`} min={0} max={480} addonAfter="min" />
                  </Form.Item>
                  <Form.Item name={[field.name, 'window']} style={{ width: 180, marginBottom: 0 }}>
                    <TimePicker.RangePicker aria-label={`Stop ${index + 1} window`} format="HH:mm" minuteStep={15} placeholder={['Opens', 'Closes']} />
                  </Form.Item>
                  <Flex gap={4}>
                    <Button aria-label={`Move stop ${index + 1} up`} icon={<ArrowUpOutlined />} disabled={index === 0} onClick={() => move(index, index - 1)} />
                    <Button aria-label={`Move stop ${index + 1} down`} icon={<ArrowDownOutlined />} disabled={index === fields.length - 1} onClick={() => move(index, index + 1)} />
                    <Button aria-label={`Remove stop ${index + 1}`} danger icon={<DeleteOutlined />} disabled={fields.length === 1} onClick={() => remove(field.name)} />
                  </Flex>
                </Flex>
              ))}
              <Button icon={<PlusOutlined />} onClick={() => add({ type: 'Pickup', serviceMinutes: 30 })}>Add stop</Button>
              {errors.length > 0 && <Alert type="error" showIcon title={errors[0]} />}
            </Flex>
          )}
        </Form.List>

        {editing && (
          <Form.Item name="isActive" label="Active" valuePropName="checked" style={{ marginTop: 16 }}>
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Drawer>
  )
}
