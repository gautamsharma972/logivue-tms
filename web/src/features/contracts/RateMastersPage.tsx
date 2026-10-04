import { EditOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, AutoComplete, Button, Card, DatePicker, Drawer, Flex, Form, Input, InputNumber, Select, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { contractsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DieselPriceDto, ZoneDto, ZoneMember } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { INDIAN_STATES } from '@/lib/indiaStates'

interface ZoneForm {
  code: string
  name: string
  members: { state?: string; city?: string }[]
}

function ZoneDrawer({ zone, open, onClose }: { zone: ZoneDto | null; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<ZoneForm>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(zone ? { code: zone.code, name: zone.name, members: zone.members.map((m) => ({ state: m.state, city: m.city ?? undefined })) } : { members: [{}] })
  }, [open, zone, form])

  const save = useMutation({
    mutationFn: (v: ZoneForm) => {
      const members: ZoneMember[] = v.members.filter((m) => m.state).map((m) => ({ state: m.state!, city: m.city?.trim() || null }))
      return zone ? contractsApi.updateZone(zone.id, { code: zone.code, name: v.name, members, version: zone.version }) : contractsApi.createZone({ code: v.code, name: v.name, members })
    },
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.zones }); void message.success(zone ? 'Zone updated' : 'Zone created'); onClose() },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Drawer open={open} onClose={onClose} size={560} destroyOnHidden title={zone ? `Edit zone ${zone.code}` : 'New zone'}
      footer={<Flex justify="flex-end" gap={8}><Button onClick={onClose}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save zone</Button></Flex>}>
      <Form<ZoneForm> form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}>
        <Form.Item label="Code" name="code" normalize={(v?: string) => v?.toUpperCase()} rules={[{ required: true, message: 'Enter a short code' }]} extra="Rates refer to the zone by this code. It cannot change later.">
          <Input disabled={zone !== null} maxLength={32} placeholder="e.g. WEST-1" />
        </Form.Item>
        <Form.Item label="Name" name="name" rules={[{ required: true, whitespace: true, message: 'Enter a name' }]}><Input placeholder="e.g. Western India" /></Form.Item>
        <Typography.Text strong>Places in this zone</Typography.Text>
        <Typography.Paragraph type="secondary">Leave the city empty to include the whole state.</Typography.Paragraph>
        <Form.List name="members">
          {(fields, { add, remove }) => (
            <Flex vertical gap={8}>
              {fields.map((field) => (
                <Flex key={field.key} gap={8}>
                  <Form.Item name={[field.name, 'state']} noStyle><Select showSearch style={{ width: 220 }} placeholder="State" options={INDIAN_STATES.map((s) => ({ value: s.toUpperCase(), label: s }))} /></Form.Item>
                  <Form.Item name={[field.name, 'city']} noStyle><Input style={{ flex: 1 }} placeholder="City (optional)" /></Form.Item>
                  <Button type="text" danger disabled={fields.length === 1} onClick={() => remove(field.name)}>Remove</Button>
                </Flex>
              ))}
              <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>Add place</Button>
            </Flex>
          )}
        </Form.List>
      </Form>
    </Drawer>
  )
}

function ZonesTab() {
  const { can } = useAuth()
  const zones = useQuery({ queryKey: queryKeys.contracts.zones, queryFn: contractsApi.zones })
  const [drawer, setDrawer] = useState<{ open: boolean; zone: ZoneDto | null }>({ open: false, zone: null })
  return (
    <Card
      title="Zones"
      extra={<Can permission="contracts.manage"><Button icon={<PlusOutlined />} type="primary" onClick={() => setDrawer({ open: true, zone: null })}>New zone</Button></Can>}
      styles={{ body: { padding: 0 } }}
    >
      {zones.isError && <Alert type="error" showIcon title={zones.error.message} style={{ margin: 16 }} />}
      <Table<ZoneDto>
        rowKey="id"
        pagination={false}
        loading={zones.isLoading}
        dataSource={zones.data}
        locale={{ emptyText: 'No zones yet. Group states and cities so one rate can cover them all.' }}
        columns={[
          { title: 'Code', dataIndex: 'code', width: 140, render: (c: string) => <Typography.Text code>{c}</Typography.Text> },
          { title: 'Name', dataIndex: 'name' },
          { title: 'Places', key: 'm', render: (_, z) => z.members.slice(0, 6).map((m) => <Tag key={`${m.state}${m.city}`}>{m.city ? `${m.city}, ${m.state}` : m.state}</Tag>).concat(z.members.length > 6 ? [<Tag key="more">+{z.members.length - 6} more</Tag>] : []) },
          { title: '', key: 'e', width: 70, render: (_, z) => can('contracts.manage') && <Button type="text" aria-label={`Edit zone ${z.code}`} icon={<EditOutlined />} onClick={() => setDrawer({ open: true, zone: z })} /> },
        ]}
      />
      <ZoneDrawer open={drawer.open} zone={drawer.zone} onClose={() => setDrawer((d) => ({ ...d, open: false }))} />
    </Card>
  )
}

function DieselTab() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ region: string; effectiveFrom: Dayjs; pricePerLitre: number }>()
  const prices = useQuery({ queryKey: queryKeys.contracts.diesel, queryFn: () => contractsApi.dieselPrices() })
  const regions = [...new Set(prices.data?.map((p) => p.region) ?? [])]

  const add = useMutation({
    mutationFn: (v: { region: string; effectiveFrom: Dayjs; pricePerLitre: number }) =>
      contractsApi.addDieselPrice({ region: v.region.trim(), effectiveFrom: v.effectiveFrom.format('YYYY-MM-DD'), pricePerLitre: v.pricePerLitre }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.diesel }); void message.success('Price recorded'); form.resetFields(['pricePerLitre']) },
    onError: (e) => void message.error(toApiError(e).message),
  })

  return (
    <Flex vertical gap={16}>
      <Can permission="contracts.manage">
        <Card title="Record a diesel price">
          <Form form={form} layout="inline" requiredMark={false} initialValues={{ effectiveFrom: dayjs() }} onFinish={(v) => add.mutate(v)} style={{ rowGap: 12 }}>
            <Form.Item name="region" rules={[{ required: true, message: 'Region' }]}><AutoComplete style={{ width: 180 }} placeholder="Region, e.g. Delhi" options={regions.map((r) => ({ value: r }))} /></Form.Item>
            <Form.Item name="effectiveFrom" rules={[{ required: true }]}><DatePicker format="D MMM YYYY" /></Form.Item>
            <Form.Item name="pricePerLitre" rules={[{ required: true, message: 'Price' }]}><InputNumber min={1} precision={2} prefix="₹" suffix="/L" style={{ width: 150 }} /></Form.Item>
            <Button type="primary" htmlType="submit" loading={add.isPending}>Record price</Button>
          </Form>
          <Typography.Paragraph type="secondary" style={{ margin: '12px 0 0' }}>A price applies from its date until the next one. Contracts with a diesel clause use the price in force on the shipment date.</Typography.Paragraph>
        </Card>
      </Can>
      <Card title="Price history" styles={{ body: { padding: 0 } }}>
        <Table<DieselPriceDto>
          rowKey="id"
          loading={prices.isLoading}
          dataSource={prices.data}
          pagination={{ pageSize: 20, hideOnSinglePage: true }}
          locale={{ emptyText: 'No diesel prices recorded yet' }}
          columns={[
            { title: 'Region', dataIndex: 'region' },
            { title: 'Effective from', dataIndex: 'effectiveFrom' },
            { title: 'Price per litre', dataIndex: 'pricePerLitre', align: 'right', render: (p: number) => `₹${p.toFixed(2)}` },
          ]}
        />
      </Card>
    </Flex>
  )
}

export function RateMastersPage() {
  return (
    <>
      <PageHeader title="Rate masters" description="The reference data rates are built on: zones that group places, and the diesel prices that drive fuel clauses." />
      <Tabs items={[{ key: 'zones', label: 'Zones', children: <ZonesTab /> }, { key: 'diesel', label: 'Diesel prices', children: <DieselTab /> }]} />
    </>
  )
}
