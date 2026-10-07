import { CalculatorOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Alert, App, Button, Card, DatePicker, Flex, Form, Input, InputNumber, Select, Space, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { contractsApi, freightApi, transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CompareRow, ContractType, RatingRequest, RatingResultDto, WhatIfRow } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { INDIAN_STATES } from '@/lib/indiaStates'
import { RatingResultView } from './ratingParts'

interface FormValues {
  date: Dayjs
  originState: string
  originCity?: string
  destinationState: string
  destinationCity?: string
  service: ContractType
  vehicleTypeId?: string
  transporterId?: string
  weightKg?: number
  volumeCbm?: number
  distanceKm?: number
  stopCount: number
  charges?: { code: string; quantity: number }[]
  shipmentReference?: string
}

const CHARGE_CODES = ['TOLL', 'DETENTION', 'WAITING', 'EXTRA_KM', 'NIGHT_HALT', 'PARKING', 'PERMIT', 'KM_RUN', 'HOURS_RUN']

/** Rate a shipment against the contracts, see exactly how, change one thing and see what moves, and compare transporters. Nothing here is kept unless "keep" is chosen. */
export function RateSimulatorPage() {
  const { message } = App.useApp()
  const { can } = useAuth()
  const [form] = Form.useForm<FormValues>()
  const [result, setResult] = useState<RatingResultDto | null>(null)
  const [whatIf, setWhatIf] = useState<{ base: WhatIfRow; variations: WhatIfRow[] } | null>(null)
  const [compare, setCompare] = useState<CompareRow[] | null>(null)
  const [last, setLast] = useState<RatingRequest | null>(null)
  const vehicles = useQuery({ queryKey: queryKeys.contracts.vehicleTypes, queryFn: contractsApi.vehicleTypes })
  const transporters = useQuery({ queryKey: queryKeys.transporters.lookup(), queryFn: () => transportersApi.list({ pageSize: 100, status: 'Active' }) })

  const toRequest = (v: FormValues, commit = false): RatingRequest => ({
    shipmentDate: v.date.format('YYYY-MM-DD'),
    origin: { state: v.originState, city: v.originCity || null },
    destination: { state: v.destinationState, city: v.destinationCity || null },
    service: v.service,
    transporterId: v.transporterId ?? null,
    vehicleTypeId: v.vehicleTypeId ?? null,
    weightKg: v.weightKg ?? null,
    volumeCbm: v.volumeCbm ?? null,
    distanceKm: v.distanceKm ?? null,
    stopCount: v.stopCount,
    accessorialInputs: Object.fromEntries((v.charges ?? []).filter((c) => c?.code && c.quantity != null).map((c) => [c.code, c.quantity])),
    shipmentReference: commit ? v.shipmentReference : null,
    commit,
  })

  const run = useMutation({
    mutationFn: async ({ values, commit }: { values: FormValues; commit: boolean }) => {
      const request = toRequest(values, commit)
      setLast(request)
      setWhatIf(null)
      setCompare(null)
      return commit ? freightApi.calculate(request) : freightApi.simulate(request)
    },
    onSuccess: (r) => setResult(r),
    onError: (e) => void message.error(toApiError(e).message),
  })

  const explore = useMutation({
    mutationFn: async (kind: 'whatif' | 'compare') => {
      if (!last) throw new Error('Rate the shipment first.')
      if (kind === 'whatif') {
        const w = last.weightKg ?? 1000
        const d = last.distanceKm ?? 100
        setWhatIf(await freightApi.whatIf(last, [
          { label: `Lighter (${Math.round(w * 0.8)} kg)`, weightKg: Math.round(w * 0.8) }, { label: `Heavier (${Math.round(w * 1.2)} kg)`, weightKg: Math.round(w * 1.2) },
          { label: `Longer (${Math.round(d * 1.25)} km)`, distanceKm: Math.round(d * 1.25) }, { label: 'One more stop', stopCount: (last.stopCount ?? 1) + 1 },
        ]))
      } else {
        const ids = (transporters.data?.items ?? []).map((t) => t.id).slice(0, 20)
        setCompare(await freightApi.compare({ ...last, transporterId: null }, ids))
      }
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  return (
    <>
      <PageHeader title="Freight rate simulator" description="Rate a shipment against the contracts and see exactly how the answer was reached. Simulations leave no trace unless you keep the rating." />
      <Flex gap={16} wrap align="start">
        <Card style={{ width: 420, flex: '0 0 420px' }} title="The shipment">
          <Form<FormValues> form={form} layout="vertical" initialValues={{ date: dayjs(), service: 'Ftl', stopCount: 1 }} onFinish={(values) => run.mutate({ values, commit: false })}>
            <Form.Item name="date" label="Shipment date" rules={[{ required: true }]}><DatePicker style={{ width: '100%' }} /></Form.Item>
            <Flex gap={8}>
              <Form.Item name="originState" label="From state" rules={[{ required: true }]} style={{ flex: 1 }}><Select showSearch options={INDIAN_STATES.map((s) => ({ value: s, label: s }))} /></Form.Item>
              <Form.Item name="originCity" label="City" style={{ flex: 1 }}><Input /></Form.Item>
            </Flex>
            <Flex gap={8}>
              <Form.Item name="destinationState" label="To state" rules={[{ required: true }]} style={{ flex: 1 }}><Select showSearch options={INDIAN_STATES.map((s) => ({ value: s, label: s }))} /></Form.Item>
              <Form.Item name="destinationCity" label="City" style={{ flex: 1 }}><Input /></Form.Item>
            </Flex>
            <Form.Item name="service" label="Service"><Select options={[{ value: 'Ftl', label: 'FTL' }, { value: 'Ptl', label: 'PTL' }, { value: 'Dedicated', label: 'Dedicated' }]} /></Form.Item>
            <Form.Item name="vehicleTypeId" label="Vehicle"><Select allowClear options={(vehicles.data ?? []).map((v) => ({ value: v.id, label: v.name }))} /></Form.Item>
            <Form.Item name="transporterId" label="Transporter (leave empty for every transporter)"><Select allowClear showSearch optionFilterProp="label" options={(transporters.data?.items ?? []).map((t) => ({ value: t.id, label: t.legalName }))} /></Form.Item>
            <Flex gap={8}>
              <Form.Item name="weightKg" label="Weight (kg)" style={{ flex: 1 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="volumeCbm" label="Volume (CBM)" style={{ flex: 1 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Flex gap={8}>
              <Form.Item name="distanceKm" label="Distance (km)" style={{ flex: 1 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="stopCount" label="Stops" style={{ flex: 1 }}><InputNumber min={1} max={100} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Form.List name="charges">
              {(fields, { add, remove }) => (
                <Flex vertical gap={4} style={{ marginBottom: 12 }}>
                  <Typography.Text strong>Extra charges that happened</Typography.Text>
                  {fields.map((f) => (
                    <Space key={f.key} align="baseline">
                      <Form.Item name={[f.name, 'code']} noStyle><Select style={{ width: 150 }} placeholder="Charge" options={CHARGE_CODES.map((c) => ({ value: c, label: c.replaceAll('_', ' ') }))} /></Form.Item>
                      <Form.Item name={[f.name, 'quantity']} noStyle><InputNumber min={0} placeholder="Qty / amount" /></Form.Item>
                      <Button type="link" onClick={() => remove(f.name)}>Remove</Button>
                    </Space>
                  ))}
                  <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({})}>Add a charge (hours, km, toll amount…)</Button>
                </Flex>
              )}
            </Form.List>
            <Button type="primary" htmlType="submit" icon={<CalculatorOutlined />} loading={run.isPending} block>Calculate</Button>
            {can('contracts.rate') || can('contracts.manage') ? (
              <Flex gap={8} style={{ marginTop: 12 }}>
                <Form.Item name="shipmentReference" noStyle><Input placeholder="Shipment reference, to keep this rating" /></Form.Item>
                <Button disabled={!form.getFieldValue('shipmentReference')} onClick={() => form.validateFields().then((values) => run.mutate({ values, commit: true }))}>Keep</Button>
              </Flex>
            ) : null}
          </Form>
        </Card>

        <div style={{ flex: 1, minWidth: 360 }}>
          {!result && <Alert type="info" showIcon title="Fill in the shipment and calculate." />}
          {result && (
            <Tabs
              items={[
                { key: 'result', label: 'Result', children: <>{result.committed && <Alert type="success" showIcon style={{ marginBottom: 12 }} title={`Kept as ${result.ratingReference}`} />}<RatingResultView result={result} /></> },
                {
                  key: 'whatif', label: 'What if', disabled: !result.qualified,
                  children: (
                    <Flex vertical gap={12}>
                      <Button onClick={() => explore.mutate('whatif')} loading={explore.isPending}>Try lighter, heavier, longer, one more stop</Button>
                      {whatIf && (
                        <Table size="small" pagination={false} rowKey="label" dataSource={[whatIf.base, ...whatIf.variations]} columns={[
                          { title: 'Scenario', dataIndex: 'label' },
                          { title: 'Freight', align: 'right', render: (_, r: WhatIfRow) => (r.qualified ? formatInrExact(r.totalFreight) : <Tag color="red">No rate</Tag>) },
                          { title: 'Change', align: 'right', render: (_, r: WhatIfRow) => (r.differenceFromBase == null ? '—' : <span style={{ color: r.differenceFromBase > 0 ? '#cf1322' : '#389e0d' }}>{r.differenceFromBase > 0 ? '+' : ''}{formatInrExact(r.differenceFromBase)}</span>) },
                        ]} />
                      )}
                    </Flex>
                  ),
                },
                {
                  key: 'compare', label: 'Compare transporters',
                  children: (
                    <Flex vertical gap={12}>
                      <Button onClick={() => explore.mutate('compare')} loading={explore.isPending}>Price this shipment with every active transporter</Button>
                      {compare && (
                        <Table size="small" pagination={false} rowKey="transporterId" dataSource={compare} columns={[
                          { title: 'Transporter', dataIndex: 'transporterName' },
                          { title: 'Base', align: 'right', render: (_, r: CompareRow) => (r.option ? formatInrExact(r.option.baseFreight) : '—') },
                          { title: 'DPH', align: 'right', render: (_, r: CompareRow) => (r.option ? formatInrExact(r.option.dphAdjustment) : '—') },
                          { title: 'Extras', align: 'right', render: (_, r: CompareRow) => (r.option ? formatInrExact(r.option.accessorialAmount) : '—') },
                          { title: 'Total', align: 'right', render: (_, r: CompareRow) => (r.option ? <strong>{formatInrExact(r.option.totalFreight)}</strong> : <Tag>No rate</Tag>) },
                          { title: 'Transit', render: (_, r: CompareRow) => (r.option?.transitSlaMinutes == null ? '—' : `${Math.round(r.option.transitSlaMinutes / 60)} h`) },
                          { title: 'Contract valid to', render: (_, r: CompareRow) => r.option?.contractValidTo ?? '—' },
                        ]} />
                      )}
                    </Flex>
                  ),
                },
              ]}
            />
          )}
        </div>
      </Flex>
    </>
  )
}
