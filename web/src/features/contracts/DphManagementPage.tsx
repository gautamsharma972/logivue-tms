import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, DatePicker, Descriptions, Flex, Form, Input, InputNumber, Modal, Select, Table, Tabs, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DphCalculationDto, DphOverviewDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'

const formulaLabel: Record<string, string> = {
  PercentageVariation: 'Percentage of variation', FixedAdjustment: 'Fixed ₹ per step', PerKmAdjustment: '₹ per km per step', Indexed: 'Indexed (no threshold)', ThresholdSteps: 'Whole steps with a threshold',
}

/** Diesel price variation across contracts: what each rule says against today's diesel, a calculator, and the price index the rules read. */
export function DphManagementPage() {
  const { can } = useAuth()
  const [calculating, setCalculating] = useState<DphOverviewDto | null>(null)
  const [inForce, setInForce] = useState(true)
  const rules = useQuery({ queryKey: queryKeys.freight.dph({ inForce }), queryFn: () => freightApi.dph({ inForceOnly: inForce }) })
  return (
    <>
      <PageHeader title="Diesel price adjustment (DPH)" description="Each rule moves freight when diesel moves away from its base price. A rule's versions are dated: a past shipment is always adjusted by the version that was in force then." />
      <Tabs items={[
        {
          key: 'rules', label: 'Rules',
          children: (
            <>
              <Flex gap={8} style={{ marginBottom: 12 }}><Select value={inForce} onChange={setInForce} style={{ width: 200 }} options={[{ value: true, label: 'In force today' }, { value: false, label: 'Every version' }]} /></Flex>
              <Table<DphOverviewDto> size="small" rowKey={(r) => r.rule.id} loading={rules.isLoading} dataSource={rules.data ?? []} pagination={{ pageSize: 15, hideOnSinglePage: true }} columns={[
                { title: 'Rule', render: (_, r) => <span>{r.rule.code} <Tag>V{r.rule.version}</Tag></span> },
                { title: 'Contract', render: (_, r) => <Link to={`/contracts/${r.rule.contractId}`}>{r.rule.contractNumber} V{r.rule.contractRevision}</Link> },
                { title: 'Formula', render: (_, r) => formulaLabel[r.rule.spec.formula] },
                { title: 'Index', render: (_, r) => r.rule.spec.region },
                { title: 'Base diesel', align: 'right', render: (_, r) => `₹${r.rule.spec.baseDieselPrice}` },
                { title: 'Current diesel', align: 'right', render: (_, r) => (r.currentPrice == null ? <Tag>No price</Tag> : `₹${r.currentPrice}`) },
                { title: 'Variation', align: 'right', render: (_, r) => (r.variationPercent == null ? '—' : `${r.variationPercent > 0 ? '+' : ''}${r.variationPercent}%`) },
                { title: 'Fuel share', align: 'right', render: (_, r) => `${r.rule.spec.fuelComponentPercent}%` },
                { title: 'Adjustment', align: 'right', render: (_, r) => (r.adjustmentPercent == null ? '—' : <strong>{r.adjustmentPercent > 0 ? '+' : ''}{r.adjustmentPercent}%</strong>) },
                { title: 'Effective', render: (_, r) => `${r.rule.effectiveFrom} → ${r.rule.effectiveTo}` },
                { title: 'Status', render: (_, r) => <Flex gap={4}>{r.rule.inForce ? <Tag color="green">In force</Tag> : <Tag>Not in force</Tag>}{r.revisionDue && <Tag color="gold">Revision due</Tag>}</Flex> },
                { title: '', render: (_, r) => <Button size="small" onClick={() => setCalculating(r)}>Calculate</Button> },
              ]} />
            </>
          ),
        },
        { key: 'index', label: 'Fuel price index', children: <PriceIndex canEdit={can('contracts.manage')} /> },
      ]} />
      <Calculator rule={calculating} canRecord={can('contracts.manage')} onClose={() => setCalculating(null)} />
    </>
  )
}

function Calculator({ rule, canRecord, onClose }: { rule: DphOverviewDto | null; canRecord: boolean; onClose: () => void }) {
  const { message } = App.useApp()
  const client = useQueryClient()
  const [result, setResult] = useState<DphCalculationDto | null>(null)
  const run = useMutation({
    mutationFn: (v: { date?: dayjs.Dayjs; baseFreight: number; distanceKm?: number; record?: boolean }) =>
      freightApi.calculateDph(rule!.rule.id, { date: v.date?.format('YYYY-MM-DD') ?? null, baseFreight: v.baseFreight, distanceKm: v.distanceKm ?? null, record: v.record }),
    onSuccess: (r) => { setResult(r); void client.invalidateQueries({ queryKey: queryKeys.freight.all }) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Modal open={!!rule} title={rule ? `Calculate ${rule.rule.code} V${rule.rule.version}` : ''} footer={null} onCancel={() => { setResult(null); onClose() }} destroyOnHidden>
      <Form layout="vertical" initialValues={{ baseFreight: 38_000, date: dayjs() }} onFinish={(v: { date: dayjs.Dayjs; baseFreight: number; distanceKm?: number; record?: boolean }) => run.mutate(v)}>
        <Flex gap={8}>
          <Form.Item name="date" label="Shipment date" style={{ flex: 1 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="baseFreight" label="Base freight (₹)" rules={[{ required: true }]} style={{ flex: 1 }}><InputNumber min={1} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="distanceKm" label="Distance (km)" style={{ flex: 1 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
        </Flex>
        {canRecord && <Form.Item name="record" valuePropName="checked"><Checkbox>Fix this period's diesel price, so the adjustment can always be reproduced</Checkbox></Form.Item>}
        <Button type="primary" htmlType="submit" loading={run.isPending}>Calculate</Button>
      </Form>
      {result && (
        <Descriptions size="small" bordered column={1} style={{ marginTop: 16 }}>
          <Descriptions.Item label="Diesel">{result.dieselPrice == null ? 'No price' : `₹${result.dieselPrice}`} against base ₹{result.baseDieselPrice} on {result.referenceDate}</Descriptions.Item>
          <Descriptions.Item label="Variation">{result.variationPercent}%</Descriptions.Item>
          <Descriptions.Item label="Adjustment">{result.applied ? `${result.adjustmentPercent}% = ${formatInrExact(result.amount)}` : 'None'}</Descriptions.Item>
          <Descriptions.Item label="How">{result.explanation}</Descriptions.Item>
          <Descriptions.Item label="Price fixed for the period">{result.recorded ? 'Yes' : 'No: it follows the index until a rating is kept'}</Descriptions.Item>
        </Descriptions>
      )}
    </Modal>
  )
}

function PriceIndex({ canEdit }: { canEdit: boolean }) {
  const { message } = App.useApp()
  const client = useQueryClient()
  const [region, setRegion] = useState<string | undefined>()
  const prices = useQuery({ queryKey: queryKeys.freight.priceIndex(region), queryFn: () => freightApi.priceIndex(region) })
  const add = useMutation({
    mutationFn: (v: { region: string; referenceDate: dayjs.Dayjs; price: number; source?: string }) => freightApi.addPrice({ region: v.region, referenceDate: v.referenceDate.format('YYYY-MM-DD'), price: v.price, source: v.source ?? null }),
    onSuccess: () => void client.invalidateQueries({ queryKey: queryKeys.freight.all }),
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Flex vertical gap={12}>
      <Typography.Text type="secondary">Pump prices by region and date. A rule reads the latest price on or before the day (or the first day of its period) it prices.</Typography.Text>
      <Flex gap={8} wrap>
        <Input.Search allowClear placeholder="Filter by region" style={{ width: 200 }} onSearch={(v) => setRegion(v || undefined)} />
        {canEdit && (
          <Form layout="inline" onFinish={(v: { region: string; referenceDate: dayjs.Dayjs; price: number; source?: string }) => add.mutate(v)}>
            <Form.Item name="region" rules={[{ required: true }]}><Input placeholder="Region" style={{ width: 130 }} /></Form.Item>
            <Form.Item name="referenceDate" rules={[{ required: true }]}><DatePicker /></Form.Item>
            <Form.Item name="price" rules={[{ required: true }]}><InputNumber min={1} max={500} placeholder="₹ / litre" /></Form.Item>
            <Form.Item name="source"><Input placeholder="Source" style={{ width: 140 }} /></Form.Item>
            <Button type="primary" htmlType="submit" loading={add.isPending}>Add price</Button>
          </Form>
        )}
      </Flex>
      {!canEdit && <Alert type="info" showIcon title="You can read the index but not change it." />}
      <Table size="small" rowKey="id" loading={prices.isLoading} dataSource={prices.data ?? []} pagination={{ pageSize: 15, hideOnSinglePage: true }} columns={[
        { title: 'Region', dataIndex: 'region' }, { title: 'From', dataIndex: 'referenceDate' }, { title: 'Price', dataIndex: 'price', align: 'right', render: (v: number) => `₹${v.toFixed(2)} / L` }, { title: 'Source', dataIndex: 'source', render: (v: string | null) => v ?? '—' },
      ]} />
    </Flex>
  )
}
