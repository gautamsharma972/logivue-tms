import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, DatePicker, Flex, Form, Input, InputNumber, Modal, Select, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { contractsApi, freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AccessorialCalc, AccessorialSpec, CapacitySpec, ContractDto, DphRuleSpec, SlaSpec } from '@/lib/api/types'
import { formatInr } from '@/lib/format'

const date = (v: dayjs.Dayjs | null | undefined) => (v ? v.format('YYYY-MM-DD') : null)
const parse = (v: string | null | undefined) => (v ? dayjs(v) : null)
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

function useSaver<T>(contractId: string, save: (items: T[], version: number) => Promise<ContractDto>, key: readonly unknown[]) {
  const { message } = App.useApp()
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ items, version }: { items: T[]; version: number }) => save(items, version),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.contracts.detail(contractId) })
      void client.invalidateQueries({ queryKey: key })
      void message.success('Saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
}

function LockedNote({ editable }: { editable: boolean }) {
  return editable ? null : <Alert type="info" showIcon style={{ marginBottom: 12 }} title="This contract is approved, so its terms are fixed. To change them, make a revision." />
}

function RowActions({ editable, onEdit, onRemove }: { editable: boolean; onEdit: () => void; onRemove: () => void }) {
  return editable ? (
    <Flex gap={4}>
      <Button size="small" onClick={onEdit}>Edit</Button>
      <Button size="small" danger icon={<DeleteOutlined />} aria-label="Remove" onClick={onRemove} />
    </Flex>
  ) : null
}

// ---- DPH

const FORMULAS = [
  { value: 'PercentageVariation', label: 'Percentage of the diesel variation' },
  { value: 'ThresholdSteps', label: 'Whole steps, each moving freight by a percentage' },
  { value: 'FixedAdjustment', label: 'A fixed ₹ per step' },
  { value: 'PerKmAdjustment', label: '₹ per km per step' },
  { value: 'Indexed', label: 'Indexed (no threshold)' },
]

export function DphTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const id = contract.summary.id
  const rules = useQuery({ queryKey: queryKeys.freight.contractDph(id), queryFn: () => freightApi.dphRules(id) })
  const save = useSaver<DphRuleSpec>(id, (items, version) => freightApi.saveDphRules(id, items, version), queryKeys.freight.contractDph(id))
  const [editing, setEditing] = useState<{ index: number; spec: Partial<DphRuleSpec> } | null>(null)
  const list = rules.data ?? []

  const submit = (v: Record<string, unknown>) => {
    const spec: DphRuleSpec = {
      code: String(v.code).toUpperCase(), name: String(v.name), formula: v.formula as DphRuleSpec['formula'], region: String(v.region), baseDieselPrice: Number(v.baseDieselPrice),
      baseDate: date(v.baseDate as dayjs.Dayjs) ?? dayjs().format('YYYY-MM-DD'), fuelComponentPercent: Number(v.fuelComponentPercent ?? 0), thresholdPercent: Number(v.thresholdPercent ?? 0),
      stepPercent: Number(v.stepPercent ?? 0), fixedAmountPerStep: Number(v.fixedAmountPerStep ?? 0), perKmPerStep: Number(v.perKmPerStep ?? 0), impactPercentPerStep: Number(v.impactPercentPerStep ?? 0),
      capPercent: v.capPercent == null ? null : Number(v.capPercent), onExcessOnly: !!v.onExcessOnly, direction: (v.direction as DphRuleSpec['direction']) ?? 'Both',
      frequency: (v.frequency as DphRuleSpec['frequency']) ?? 'Shipment', adjustmentDecimals: 2, effectiveFrom: date(v.effectiveFrom as dayjs.Dayjs), effectiveTo: date(v.effectiveTo as dayjs.Dayjs), isDefault: !!v.isDefault,
    }
    const next = editing!.index >= 0 ? list.map((r, i) => (i === editing!.index ? spec : r)) : [...list, spec]
    save.mutate({ items: next, version: contract.version }, { onSuccess: () => setEditing(null) })
  }

  return (
    <Flex vertical gap={12}>
      <LockedNote editable={editable} />
      <Typography.Text type="secondary">Several versions of one rule can sit in a contract, each for its own dates (for example one a month). The version in force on the shipment date adjusts it.</Typography.Text>
      <Table size="small" rowKey={(r) => `${r.code}${r.effectiveFrom}`} loading={rules.isLoading} dataSource={list} pagination={false}
        locale={{ emptyText: contract.fuel ? 'No DPH rules. The older diesel clause on the Overview applies.' : 'No DPH rules: freight is not adjusted for diesel.' }}
        columns={[
          { title: 'Rule', render: (_, r) => <span>{r.code}{r.isDefault && <Tag color="blue" style={{ marginLeft: 6 }}>Default</Tag>}</span> },
          { title: 'Name', dataIndex: 'name' },
          { title: 'Formula', render: (_, r) => FORMULAS.find((f) => f.value === r.formula)?.label },
          { title: 'Index', dataIndex: 'region' },
          { title: 'Base', align: 'right', render: (_, r) => `₹${r.baseDieselPrice}` },
          { title: 'Fuel share', align: 'right', render: (_, r) => `${r.fuelComponentPercent}%` },
          { title: 'Threshold', align: 'right', render: (_, r) => `±${r.thresholdPercent}%` },
          { title: 'Priced', dataIndex: 'frequency' },
          { title: 'Effective', render: (_, r) => `${r.effectiveFrom ?? contract.summary.effectiveFrom} → ${r.effectiveTo ?? contract.summary.effectiveTo}` },
          { title: '', render: (_, r, i) => <RowActions editable={editable} onEdit={() => setEditing({ index: i, spec: r })} onRemove={() => save.mutate({ items: list.filter((_x, j) => j !== i), version: contract.version })} /> },
        ]} />
      {editable && (
        <Button icon={<PlusOutlined />} style={{ alignSelf: 'start' }} onClick={() => setEditing({ index: -1, spec: { formula: 'PercentageVariation', fuelComponentPercent: 30, thresholdPercent: 0, direction: 'Both', frequency: 'Shipment', isDefault: list.length === 0 } })}>
          Add a DPH rule
        </Button>
      )}
      <Modal open={!!editing} title={editing && editing.index >= 0 ? 'Edit DPH rule' : 'New DPH rule'} footer={null} onCancel={() => setEditing(null)} destroyOnHidden width={680}>
        {editing && (
          <Form layout="vertical" initialValues={{ ...editing.spec, baseDate: parse(editing.spec.baseDate) ?? dayjs(), effectiveFrom: parse(editing.spec.effectiveFrom), effectiveTo: parse(editing.spec.effectiveTo) }} onFinish={submit}>
            <Flex gap={8}>
              <Form.Item name="code" label="Code" rules={[{ required: true }]} style={{ width: 140 }}><Input /></Form.Item>
              <Form.Item name="name" label="Name" rules={[{ required: true }]} style={{ flex: 1 }}><Input /></Form.Item>
            </Flex>
            <Form.Item name="formula" label="Formula"><Select options={FORMULAS} /></Form.Item>
            <Flex gap={8} wrap>
              <Form.Item name="region" label="Diesel index region" rules={[{ required: true }]} style={{ width: 160 }}><Input /></Form.Item>
              <Form.Item name="baseDieselPrice" label="Base diesel (₹/L)" rules={[{ required: true }]} style={{ width: 140 }}><InputNumber min={1} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="baseDate" label="Base date" style={{ width: 150 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="fuelComponentPercent" label="Fuel share of freight (%)" style={{ width: 170 }}><InputNumber min={0} max={100} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="thresholdPercent" label="Dead band (±%)" style={{ width: 130 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Flex gap={8} wrap>
              <Form.Item name="stepPercent" label="Step (% of diesel)" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="impactPercentPerStep" label="Freight % per step" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="fixedAmountPerStep" label="₹ per step" style={{ width: 120 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="perKmPerStep" label="₹/km per step" style={{ width: 130 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="capPercent" label="Cap (%)" style={{ width: 110 }}><InputNumber min={0} max={100} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Flex gap={8} wrap>
              <Form.Item name="direction" label="Moves freight" style={{ width: 200 }}><Select options={[{ value: 'Both', label: 'Up and down' }, { value: 'EscalationOnly', label: 'Up only' }, { value: 'DeEscalationOnly', label: 'Down only' }]} /></Form.Item>
              <Form.Item name="frequency" label="Diesel price taken" style={{ width: 220 }}>
                <Select options={[{ value: 'Shipment', label: 'On the shipment day' }, { value: 'Weekly', label: 'At the start of the week' }, { value: 'Fortnightly', label: 'At the start of the fortnight' }, { value: 'Monthly', label: 'At the start of the month' }, { value: 'Quarterly', label: 'At the start of the quarter' }]} />
              </Form.Item>
              <Form.Item name="effectiveFrom" label="Effective from" style={{ width: 150 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="effectiveTo" label="Effective to" style={{ width: 150 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Flex gap={16}>
              <Form.Item name="onExcessOnly" valuePropName="checked"><Checkbox>Count only the part beyond the dead band</Checkbox></Form.Item>
              <Form.Item name="isDefault" valuePropName="checked"><Checkbox>Default rule for rates that name none</Checkbox></Form.Item>
            </Flex>
            <Button type="primary" htmlType="submit" loading={save.isPending}>Save rule</Button>
          </Form>
        )}
      </Modal>
    </Flex>
  )
}

// ---- Extra charges

export function ChargesTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const id = contract.summary.id
  const charges = useQuery({ queryKey: queryKeys.freight.contractCharges(id), queryFn: () => freightApi.charges(id) })
  const catalogue = useQuery({ queryKey: queryKeys.freight.accessorials, queryFn: freightApi.accessorials })
  const save = useSaver<AccessorialSpec>(id, (items, version) => freightApi.saveCharges(id, items, version), queryKeys.freight.contractCharges(id))
  const [editing, setEditing] = useState<{ index: number; spec: Partial<AccessorialSpec> } | null>(null)
  const list = charges.data ?? []

  const submit = (v: Record<string, unknown>) => {
    const type = (catalogue.data ?? []).find((t) => t.code === (v.code ?? editing?.spec.code))
    const calc = (v.calc as AccessorialCalc) ?? type?.calc ?? 'Fixed'
    const tiers = (v.tiers as { from: number; to: number | null; rate: number }[] | undefined) ?? []
    const spec: AccessorialSpec = {
      code: String(v.code ?? editing?.spec.code), name: type?.name ?? editing?.spec.name ?? String(v.code), calc, unit: type?.unit ?? editing?.spec.unit ?? 'TRIP', rate: Number(v.rate ?? 0),
      minimumCharge: v.minimumCharge == null ? null : Number(v.minimumCharge), maximumCharge: v.maximumCharge == null ? null : Number(v.maximumCharge), includedQuantity: Number(v.includedQuantity ?? 0),
      tiers: calc === 'Tiered' ? tiers.map((t) => ({ from: Number(t.from), to: t.to == null ? null : Number(t.to), rate: Number(t.rate) })) : null,
      trigger: editing?.spec.trigger ?? null, validFrom: date(v.validFrom as dayjs.Dayjs), validTo: date(v.validTo as dayjs.Dayjs), autoApply: !!v.autoApply,
    }
    const next = editing!.index >= 0 ? list.map((r, i) => (i === editing!.index ? spec : r)) : [...list, spec]
    save.mutate({ items: next, version: contract.version }, { onSuccess: () => setEditing(null) })
  }

  return (
    <Flex vertical gap={12}>
      <LockedNote editable={editable} />
      <Typography.Text type="secondary">What the contract lets the carrier add to base freight. The quantities that happened (hours kept waiting, kilometres over) are supplied when a shipment is rated.</Typography.Text>
      <Table size="small" rowKey={(r) => `${r.code}${r.validFrom}`} loading={charges.isLoading} dataSource={list} pagination={false}
        locale={{ emptyText: 'No extra charges are defined. The loading, unloading, drop and detention terms on the Overview still apply.' }}
        columns={[
          { title: 'Charge', render: (_, r) => <span>{r.name} <Tag>{r.code}</Tag></span> },
          { title: 'Worked out as', dataIndex: 'calc' },
          { title: 'Rate', render: (_, r) => (r.calc === 'Tiered' ? `${r.tiers?.length ?? 0} bands` : r.calc === 'Reimbursed' ? 'At cost' : r.calc === 'PercentOfFreight' ? `${r.rate}%` : formatInr(r.rate)) },
          { title: 'Included', render: (_, r) => (r.includedQuantity ? `${r.includedQuantity} ${r.unit.toLowerCase()}` : '—') },
          { title: 'Min / max', render: (_, r) => (r.minimumCharge == null && r.maximumCharge == null ? '—' : `${r.minimumCharge ?? '—'} / ${r.maximumCharge ?? '—'}`) },
          { title: 'Applies', render: (_, r) => (r.autoApply ? 'Every shipment' : 'When it happens') },
          { title: 'Valid', render: (_, r) => (r.validFrom || r.validTo ? `${r.validFrom ?? '…'} → ${r.validTo ?? '…'}` : 'Whole term') },
          { title: '', render: (_, r, i) => <RowActions editable={editable} onEdit={() => setEditing({ index: i, spec: r })} onRemove={() => save.mutate({ items: list.filter((_x, j) => j !== i), version: contract.version })} /> },
        ]} />
      {editable && <Button icon={<PlusOutlined />} style={{ alignSelf: 'start' }} onClick={() => setEditing({ index: -1, spec: { calc: 'PerUnit' } })}>Add a charge</Button>}
      <Modal open={!!editing} title={editing && editing.index >= 0 ? 'Edit charge' : 'New charge'} footer={null} onCancel={() => setEditing(null)} destroyOnHidden width={620}>
        {editing && (
          <Form layout="vertical" initialValues={{ ...editing.spec, validFrom: parse(editing.spec.validFrom), validTo: parse(editing.spec.validTo), tiers: editing.spec.tiers ?? [{ from: 0, to: 2, rate: 0 }, { from: 2, to: null, rate: 500 }] }} onFinish={submit}>
            <Form.Item name="code" label="Charge" rules={[{ required: true }]}>
              <Select disabled={editing.index >= 0} showSearch optionFilterProp="label" options={(catalogue.data ?? []).filter((t) => t.isActive).map((t) => ({ value: t.code, label: `${t.name} (${t.code})` }))}
                onChange={(code: string) => { const t = (catalogue.data ?? []).find((x) => x.code === code); if (t) setEditing((e) => (e ? { ...e, spec: { ...e.spec, calc: t.calc } } : e)) }} />
            </Form.Item>
            <Form.Item name="calc" label="Worked out as">
              <Select onChange={(calc: AccessorialCalc) => setEditing((e) => (e ? { ...e, spec: { ...e.spec, calc } } : e))}
                options={[{ value: 'Fixed', label: 'One amount' }, { value: 'PerUnit', label: 'A rate per unit' }, { value: 'Tiered', label: 'Bands of quantity' }, { value: 'PercentOfFreight', label: 'A share of the freight' }, { value: 'Reimbursed', label: 'Passed through at cost' }]} />
            </Form.Item>
            {editing.spec.calc !== 'Reimbursed' && editing.spec.calc !== 'Tiered' && (
              <Form.Item name="rate" label={editing.spec.calc === 'PercentOfFreight' ? 'Share of freight (%)' : 'Rate (₹)'}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            )}
            {editing.spec.calc === 'Tiered' && (
              <Form.List name="tiers">
                {(fields, { add, remove }) => (
                  <Flex vertical gap={4} style={{ marginBottom: 12 }}>
                    <Typography.Text strong>Bands of quantity (from zero; a rate of 0 means included)</Typography.Text>
                    {fields.map((f) => (
                      <Flex key={f.key} gap={8} align="baseline">
                        <Form.Item name={[f.name, 'from']} noStyle><InputNumber min={0} placeholder="From" /></Form.Item>
                        <Form.Item name={[f.name, 'to']} noStyle><InputNumber min={0} placeholder="To (blank = no limit)" /></Form.Item>
                        <Form.Item name={[f.name, 'rate']} noStyle><InputNumber min={0} placeholder="₹ per unit" /></Form.Item>
                        <Button type="link" onClick={() => remove(f.name)}>Remove</Button>
                      </Flex>
                    ))}
                    <Button type="dashed" onClick={() => add({ from: 0, to: null, rate: 0 })}>Add a band</Button>
                  </Flex>
                )}
              </Form.List>
            )}
            <Flex gap={8} wrap>
              <Form.Item name="includedQuantity" label="Included quantity" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="minimumCharge" label="Minimum (₹)" style={{ width: 130 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="maximumCharge" label="Maximum (₹)" style={{ width: 130 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="validFrom" label="From" style={{ width: 150 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="validTo" label="To" style={{ width: 150 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Form.Item name="autoApply" valuePropName="checked"><Checkbox>Charge it on every shipment, without waiting for it to happen</Checkbox></Form.Item>
            <Button type="primary" htmlType="submit" loading={save.isPending}>Save charge</Button>
          </Form>
        )}
      </Modal>
    </Flex>
  )
}

// ---- Capacity and service levels

export function CapacityTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const id = contract.summary.id
  const rows = useQuery({ queryKey: queryKeys.freight.capacity(id), queryFn: () => freightApi.capacity(id) })
  const types = useQuery({ queryKey: queryKeys.contracts.vehicleTypes, queryFn: contractsApi.vehicleTypes })
  const save = useSaver<CapacitySpec>(id, (items, version) => freightApi.saveCapacity(id, items, version), queryKeys.freight.capacity(id))
  const [editing, setEditing] = useState<{ index: number; spec: Partial<CapacitySpec> } | null>(null)
  const list = rows.data ?? []
  const submit = (v: Record<string, unknown>) => {
    const spec: CapacitySpec = {
      vehicleTypeId: (v.vehicleTypeId as string | undefined) ?? null, committedVehicleCount: Number(v.committedVehicleCount ?? 0), committedCapacityKg: v.committedCapacityKg == null ? null : Number(v.committedCapacityKg),
      minimumMonthlyTrips: v.minimumMonthlyTrips == null ? null : Number(v.minimumMonthlyTrips), minimumMonthlyTonnage: v.minimumMonthlyTonnage == null ? null : Number(v.minimumMonthlyTonnage),
      targetBusinessSharePct: v.targetBusinessSharePct == null ? null : Number(v.targetBusinessSharePct), validFrom: date(v.validFrom as dayjs.Dayjs), validTo: date(v.validTo as dayjs.Dayjs),
    }
    const specs = list.map((r) => r.spec)
    save.mutate({ items: editing!.index >= 0 ? specs.map((r, i) => (i === editing!.index ? spec : r)) : [...specs, spec], version: contract.version }, { onSuccess: () => setEditing(null) })
  }
  return (
    <Flex vertical gap={12}>
      <LockedNote editable={editable} />
      <Typography.Text type="secondary">What the carrier commits to keep available and what the shipper commits to give. Planning reads these when it chooses carriers.</Typography.Text>
      <Table size="small" rowKey="id" dataSource={list} loading={rows.isLoading} pagination={false} locale={{ emptyText: 'No commitments.' }}
        columns={[
          { title: 'Vehicle type', render: (_, r) => r.vehicleTypeName ?? 'Any' },
          { title: 'Committed vehicles', align: 'right', render: (_, r) => r.spec.committedVehicleCount },
          { title: 'Capacity (kg)', align: 'right', render: (_, r) => r.spec.committedCapacityKg ?? '—' },
          { title: 'Minimum trips / month', align: 'right', render: (_, r) => r.spec.minimumMonthlyTrips ?? '—' },
          { title: 'Minimum tonnage', align: 'right', render: (_, r) => r.spec.minimumMonthlyTonnage ?? '—' },
          { title: 'Target share', align: 'right', render: (_, r) => (r.spec.targetBusinessSharePct == null ? '—' : `${r.spec.targetBusinessSharePct}%`) },
          { title: 'Valid', render: (_, r) => (r.spec.validFrom || r.spec.validTo ? `${r.spec.validFrom ?? '…'} → ${r.spec.validTo ?? '…'}` : 'Whole term') },
          { title: '', render: (_, r, i) => <RowActions editable={editable} onEdit={() => setEditing({ index: i, spec: r.spec })} onRemove={() => save.mutate({ items: list.filter((_x, j) => j !== i).map((x) => x.spec), version: contract.version })} /> },
        ]} />
      {editable && <Button icon={<PlusOutlined />} style={{ alignSelf: 'start' }} onClick={() => setEditing({ index: -1, spec: {} })}>Add a commitment</Button>}
      <Modal open={!!editing} title="Capacity commitment" footer={null} onCancel={() => setEditing(null)} destroyOnHidden>
        {editing && (
          <Form layout="vertical" initialValues={{ ...editing.spec, validFrom: parse(editing.spec.validFrom), validTo: parse(editing.spec.validTo) }} onFinish={submit}>
            <Form.Item name="vehicleTypeId" label="Vehicle type"><Select allowClear options={(types.data ?? []).map((t) => ({ value: t.id, label: t.name }))} /></Form.Item>
            <Flex gap={8} wrap>
              <Form.Item name="committedVehicleCount" label="Committed vehicles" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="committedCapacityKg" label="Capacity (kg)" style={{ width: 130 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="minimumMonthlyTrips" label="Min trips / month" style={{ width: 140 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="minimumMonthlyTonnage" label="Min tonnage / month" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="targetBusinessSharePct" label="Target share (%)" style={{ width: 130 }}><InputNumber min={0} max={100} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Flex gap={8}>
              <Form.Item name="validFrom" label="From" style={{ flex: 1 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="validTo" label="To" style={{ flex: 1 }}><DatePicker style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Button type="primary" htmlType="submit" loading={save.isPending}>Save</Button>
          </Form>
        )}
      </Modal>
    </Flex>
  )
}

const hours = (m: number | null) => (m == null ? '—' : m % 60 === 0 ? `${m / 60} h` : `${m} min`)

export function SlaTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const id = contract.summary.id
  const rows = useQuery({ queryKey: queryKeys.freight.sla(id), queryFn: () => freightApi.sla(id) })
  const save = useSaver<SlaSpec>(id, (items, version) => freightApi.saveSla(id, items, version), queryKeys.freight.sla(id))
  const [editing, setEditing] = useState<{ index: number; spec: Partial<SlaSpec> } | null>(null)
  const list = rows.data ?? []
  const submit = (v: Record<string, unknown>) => {
    const h = (x: unknown) => (x == null ? null : Math.round(Number(x) * 60))
    const spec: SlaSpec = {
      service: v.service as SlaSpec['service'],
      origin: v.originState ? { kind: 'State', state: String(v.originState), city: null, zoneCode: null } : null,
      destination: v.destinationState ? { kind: 'State', state: String(v.destinationState), city: null, zoneCode: null } : null,
      pickupSlaMinutes: h(v.pickupHours), transitSlaMinutes: h(v.transitHours), deliverySlaMinutes: h(v.deliveryHours), tenderLeadTimeMinutes: h(v.tenderHours),
      operatingDays: (v.operatingDays as string[] | undefined) ?? null, cutoffTime: v.cutoffTime ? `${String(v.cutoffTime)}:00` : null,
    }
    save.mutate({ items: editing!.index >= 0 ? list.map((r, i) => (i === editing!.index ? spec : r)) : [...list, spec], version: contract.version }, { onSuccess: () => setEditing(null) })
  }
  const e = editing?.spec
  return (
    <Flex vertical gap={12}>
      <LockedNote editable={editable} />
      <Table size="small" rowKey={(r) => `${r.service}${r.origin?.state}${r.destination?.state}`} dataSource={list} loading={rows.isLoading} pagination={false} locale={{ emptyText: 'No service levels.' }}
        columns={[
          { title: 'Service', dataIndex: 'service' },
          { title: 'Lane', render: (_, r) => (r.origin || r.destination ? `${r.origin?.state ?? r.origin?.zoneCode ?? 'Anywhere'} → ${r.destination?.state ?? r.destination?.zoneCode ?? 'Anywhere'}` : 'Every lane') },
          { title: 'Pickup', render: (_, r) => hours(r.pickupSlaMinutes) },
          { title: 'Transit', render: (_, r) => hours(r.transitSlaMinutes) },
          { title: 'Delivery', render: (_, r) => hours(r.deliverySlaMinutes) },
          { title: 'Tender lead time', render: (_, r) => hours(r.tenderLeadTimeMinutes) },
          { title: 'Operating days', render: (_, r) => (r.operatingDays?.length ? r.operatingDays.map((d) => d.slice(0, 3)).join(', ') : 'Every day') },
          { title: 'Cut-off', dataIndex: 'cutoffTime', render: (v: string | null) => v?.slice(0, 5) ?? '—' },
          { title: '', render: (_, r, i) => <RowActions editable={editable} onEdit={() => setEditing({ index: i, spec: r })} onRemove={() => save.mutate({ items: list.filter((_x, j) => j !== i), version: contract.version })} /> },
        ]} />
      {editable && <Button icon={<PlusOutlined />} style={{ alignSelf: 'start' }} onClick={() => setEditing({ index: -1, spec: { service: contract.summary.type } })}>Add a service level</Button>}
      <Modal open={!!editing} title="Service level" footer={null} onCancel={() => setEditing(null)} destroyOnHidden>
        {e && (
          <Form layout="vertical" onFinish={submit} initialValues={{
            service: e.service, originState: e.origin?.state, destinationState: e.destination?.state, pickupHours: e.pickupSlaMinutes == null ? null : e.pickupSlaMinutes / 60,
            transitHours: e.transitSlaMinutes == null ? null : e.transitSlaMinutes / 60, deliveryHours: e.deliverySlaMinutes == null ? null : e.deliverySlaMinutes / 60,
            tenderHours: e.tenderLeadTimeMinutes == null ? null : e.tenderLeadTimeMinutes / 60, operatingDays: e.operatingDays, cutoffTime: e.cutoffTime?.slice(0, 5),
          }}>
            <Form.Item name="service" label="Service"><Select options={(contract.summary.services ?? [contract.summary.type]).map((s) => ({ value: s, label: s.toUpperCase() }))} /></Form.Item>
            <Flex gap={8}>
              <Form.Item name="originState" label="From state (blank = any)" style={{ flex: 1 }}><Input /></Form.Item>
              <Form.Item name="destinationState" label="To state (blank = any)" style={{ flex: 1 }}><Input /></Form.Item>
            </Flex>
            <Flex gap={8} wrap>
              <Form.Item name="pickupHours" label="Pickup (h)" style={{ width: 110 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="transitHours" label="Transit (h)" style={{ width: 110 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="deliveryHours" label="Delivery (h)" style={{ width: 110 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
              <Form.Item name="tenderHours" label="Tender lead time (h)" style={{ width: 150 }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
            </Flex>
            <Form.Item name="operatingDays" label="Operating days (none = every day)"><Select mode="multiple" options={DAYS.map((d) => ({ value: d, label: d }))} /></Form.Item>
            <Form.Item name="cutoffTime" label="Booking cut-off (HH:mm)"><Input placeholder="18:00" style={{ width: 120 }} /></Form.Item>
            <Button type="primary" htmlType="submit" loading={save.isPending}>Save</Button>
          </Form>
        )}
      </Modal>
    </Flex>
  )
}
