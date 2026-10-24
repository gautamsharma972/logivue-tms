import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, DatePicker, Descriptions, Divider, Form, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type AgreementDto, type AgreementSummaryDto, type DetentionPolicy, type RateDto, type SaveAgreementRequest } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'
import { useAuth } from '@/features/auth/AuthContext'
import { day } from './fmt'
import { ResponsiveDrawer, StatusTag } from './shared'
import { useCustomerOptions, useLocationOptions } from './hooks'

const methodOptions = [{ value: 'PerKg', label: 'Per kg' }, { value: 'PerTonne', label: 'Per tonne' }, { value: 'FlatPerConsignment', label: 'Flat per consignment' }, { value: 'WeightSlabs', label: 'Weight bands' }]
const basisOptions = [{ value: 'Actual', label: 'Actual weight' }, { value: 'Volumetric', label: 'Volumetric weight' }, { value: 'HigherOfActualAndVolumetric', label: 'Higher of actual and volumetric' }]
const roundingOptions = [{ value: 'RoundUp', label: 'Each started unit counts in full' }, { value: 'RoundDown', label: 'Only complete units count' }, { value: 'Nearest', label: 'Round to the nearest unit' }, { value: 'Exact', label: 'Exact (two decimals)' }]
const noDetention: DetentionPolicy = { applicable: false, freeTime: 'Duration', freeAmount: 0, freeUnit: 'Hour', chargeUnit: 'Hour', rate: 0, rounding: 'RoundUp', maxCharge: null, note: null }

const emptyRate = (): RateDto => ({ originLocationId: null, originCity: null, originState: null, destinationLocationId: null, destinationCity: null, destinationState: null, material: null, method: 'PerKg', rate: 0, slabs: [], minimumCharge: null })

export function AgreementsPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const customers = useCustomerOptions()
  const [customerId, setCustomerId] = useState<string>()
  const list = useQuery({ queryKey: ['ops', 'agreements', customerId], queryFn: () => opsApi.agreements({ customerId }) })
  const [editing, setEditing] = useState<AgreementDto | 'new' | null>(null)
  const [viewing, setViewing] = useState<string | null>(null)
  const [revise, setRevise] = useState<AgreementSummaryDto | null>(null)
  const done = (text: string) => { message.success(text); void qc.invalidateQueries({ queryKey: ['ops'] }) }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const activate = useMutation({ mutationFn: (id: string) => opsApi.activateAgreement(id), onSuccess: () => done('Agreement activated'), onError: fail })
  const cancel = useMutation({ mutationFn: (id: string) => opsApi.cancelAgreement(id), onSuccess: () => done('Draft cancelled'), onError: fail })
  const doRevise = useMutation({
    mutationFn: ({ id, from }: { id: string; from: string }) => opsApi.reviseAgreement(id, from),
    onSuccess: (a) => { done('Draft revision created'); setRevise(null); setEditing(a) }, onError: fail,
  })
  const edit = async (id: string) => setEditing(await opsApi.agreement(id))
  return (
    <>
      <PageHeader title="Customer commercial terms" description="Freight rates, detention and tax agreed with each customer, effective-dated. An active agreement is never edited: a change is a new revision."
        actions={can('ops.commercial.manage') && <Button type="primary" onClick={() => setEditing('new')}>New agreement</Button>} />
      <Card>
        <Space style={{ marginBottom: 12 }}><Select allowClear showSearch optionFilterProp="label" placeholder="Customer" style={{ width: 300 }} options={customers} value={customerId} onChange={setCustomerId} /></Space>
        <Table<AgreementSummaryDto> scroll={{ x: 'max-content' }} rowKey="id" loading={list.isLoading} dataSource={list.data} pagination={false}
          columns={[
            { title: 'Agreement', render: (_, a) => <a onClick={() => setViewing(a.id)}>{a.number} · rev {a.revision}</a> }, { title: 'Customer', dataIndex: 'customerName' }, { title: 'Title', dataIndex: 'title' },
            { title: 'Valid', render: (_, a) => `${day(a.effectiveFrom)} → ${a.effectiveTo ? day(a.effectiveTo) : 'open'}` }, { title: 'Rates', dataIndex: 'rateCount' },
            { title: 'Status', dataIndex: 'status', render: (v) => <StatusTag value={v} /> },
            {
              title: '', render: (_, a) => can('ops.commercial.manage') && (
                <Space>
                  {a.status === 'Draft' && <><Button size="small" onClick={() => void edit(a.id)}>Edit</Button><Popconfirm title="Activate this agreement?" description="It is locked once active." onConfirm={() => activate.mutate(a.id)}><Button size="small" type="primary">Activate</Button></Popconfirm><Popconfirm title="Cancel this draft?" onConfirm={() => cancel.mutate(a.id)}><Button size="small" danger>Cancel</Button></Popconfirm></>}
                  {a.status === 'Active' && <Button size="small" onClick={() => setRevise(a)}>Revise</Button>}
                </Space>
              ),
            },
          ]} />
      </Card>
      <AgreementView id={viewing} onClose={() => setViewing(null)} />
      <AgreementEditor value={editing} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); done('Agreement saved') }} />
      <Modal open={revise !== null} title={`Revise ${revise?.number ?? ''}`} onCancel={() => setRevise(null)} okText="Create draft revision" confirmLoading={doRevise.isPending} destroyOnHidden
        onOk={() => { const el = document.getElementById('ops-revise-date') as HTMLInputElement | null; const v = el?.value; if (revise && v) doRevise.mutate({ id: revise.id, from: dayjs(v, 'DD MMM YYYY').format('YYYY-MM-DD') }) }}>
        <p>The revision copies today's terms. It starts on the date below; deliveries before that date keep being priced with the current terms.</p>
        <DatePicker id="ops-revise-date" format="DD MMM YYYY" defaultValue={dayjs().add(1, 'day')} allowClear={false} />
      </Modal>
    </>
  )
}

function AgreementView({ id, onClose }: { id: string | null; onClose: () => void }) {
  const q = useQuery({ queryKey: ['ops', 'agreement', id], queryFn: () => opsApi.agreement(id!), enabled: !!id })
  const a = q.data
  return (
    <ResponsiveDrawer open={!!id} onClose={onClose} desktopWidth={780} title={a ? `${a.number} · revision ${a.revision}` : 'Agreement'} loading={q.isLoading}>
      {a && (
        <>
          <Descriptions size="small" column={2} bordered>
            <Descriptions.Item label="Customer">{a.customerName}</Descriptions.Item><Descriptions.Item label="Status"><StatusTag value={a.status} /></Descriptions.Item>
            <Descriptions.Item label="Valid">{day(a.effectiveFrom)} → {a.effectiveTo ? day(a.effectiveTo) : 'open'}</Descriptions.Item>
            <Descriptions.Item label="Billable weight">{basisOptions.find((b) => b.value === a.weightBasis)?.label}{a.volumetricKgPerCbm ? ` (${a.volumetricKgPerCbm} kg/CBM)` : ''}</Descriptions.Item>
            <Descriptions.Item label="Minimum freight">{a.minimumFreight == null ? '—' : formatInrExact(a.minimumFreight)}</Descriptions.Item>
            <Descriptions.Item label="Tax">{a.tax.mode === 'None' ? 'None' : a.tax.mode === 'ReverseCharge' ? 'Reverse charge' : `${a.tax.ratePercent}% forward charge${a.tax.sac ? `, SAC ${a.tax.sac}` : ''}`}</Descriptions.Item>
            <Descriptions.Item label="Detention" span={2}>{a.detention.applicable ? `${a.detention.freeTime === 'EndOfDay' ? 'Free until the end of the arrival day' : `Free ${a.detention.freeAmount} ${a.detention.freeUnit.toLowerCase()}(s)`}; then ${formatInrExact(a.detention.rate)} per ${a.detention.chargeUnit.toLowerCase()}${a.detention.maxCharge ? `, up to ${formatInrExact(a.detention.maxCharge)}` : ''}` : 'Not applicable'}</Descriptions.Item>
            <Descriptions.Item label="POD for billing" span={2}>{a.billing.allowBillingWithoutPod ? <Tag color="orange">Billing without an accepted POD is allowed</Tag> : 'An accepted POD is required'}</Descriptions.Item>
          </Descriptions>
          <Divider titlePlacement="left">Rates</Divider>
          <Table<RateDto> scroll={{ x: 'max-content' }} rowKey={(r) => r.id ?? JSON.stringify(r)} size="small" pagination={false} dataSource={a.rates}
            columns={[
              { title: 'From', render: (_, r) => r.originLocationId ? 'Location' : [r.originCity, r.originState].filter(Boolean).join(', ') || 'Anywhere' },
              { title: 'To', render: (_, r) => r.destinationLocationId ? 'Location' : [r.destinationCity, r.destinationState].filter(Boolean).join(', ') || 'Anywhere' },
              { title: 'Material', dataIndex: 'material', render: (v) => v ?? 'Any' },
              { title: 'Rate', render: (_, r) => r.method === 'WeightSlabs' ? `${r.slabs.length} weight bands` : `${formatInrExact(r.rate)} ${r.method === 'PerKg' ? '/ kg' : r.method === 'PerTonne' ? '/ tonne' : '/ consignment'}` },
              { title: 'Minimum', dataIndex: 'minimumCharge', render: (v) => (v == null ? '—' : formatInrExact(v)) },
            ]} />
          {a.extraCharges.length > 0 && <><Divider titlePlacement="left">Other agreed charges</Divider>{a.extraCharges.map((c) => <div key={c.code}>{c.name}: {formatInrExact(c.amount)} per consignment</div>)}</>}
        </>
      )}
    </ResponsiveDrawer>
  )
}

interface FormShape {
  customerId: string; title: string; period: [dayjs.Dayjs, dayjs.Dayjs | null]; weightBasis: AgreementDto['weightBasis']; volumetricKgPerCbm: number | null; minimumFreight: number | null
  detention: DetentionPolicy; taxMode: AgreementDto['tax']['mode']; taxRate: number; sac: string | null; requireAcceptedPod: boolean; allowBillingWithoutPod: boolean; requiredDocuments: string | null
  extraCharges: { code: string; name: string; amount: number }[]; rates: RateDto[]
}

function AgreementEditor({ value, onClose, onSaved }: { value: AgreementDto | 'new' | null; onClose: () => void; onSaved: () => void }) {
  const customers = useCustomerOptions()
  const locations = useLocationOptions()
  const [form] = Form.useForm<FormShape>()
  const isNew = value === 'new'
  const existing = value && value !== 'new' ? value : null
  const save = useMutation({
    mutationFn: (v: FormShape) => {
      const body: SaveAgreementRequest = {
        customerId: v.customerId, title: v.title, effectiveFrom: v.period[0].format('YYYY-MM-DD'), effectiveTo: v.period[1]?.format('YYYY-MM-DD') ?? null, weightBasis: v.weightBasis,
        volumetricKgPerCbm: v.volumetricKgPerCbm ?? null, minimumFreight: v.minimumFreight ?? null, detention: { ...noDetention, ...v.detention, maxCharge: v.detention?.maxCharge ?? null },
        tax: { mode: v.taxMode, ratePercent: v.taxMode === 'None' ? 0 : v.taxRate ?? 0, sac: v.sac ?? null, note: null },
        billing: { requireAcceptedPod: !v.allowBillingWithoutPod, allowBillingWithoutPod: !!v.allowBillingWithoutPod, requiredDocuments: v.requiredDocuments ?? null },
        extraCharges: v.extraCharges ?? [], rates: (v.rates ?? []).map((r) => ({ ...emptyRate(), ...r, slabs: r.slabs ?? [], minimumCharge: r.minimumCharge ?? null, id: undefined })), version: existing?.version ?? 0,
      }
      return existing ? opsApi.updateAgreement(existing.id, body) : opsApi.createAgreement(body)
    },
    onSuccess: onSaved,
    onError: (e) => message.error(toApiError(e).message),
  })
  return (
    <ResponsiveDrawer open={value !== null} onClose={onClose} desktopWidth={980} title={isNew ? 'New agreement' : `Edit draft ${existing?.number ?? ''} · revision ${existing?.revision ?? ''}`} destroyOnHidden
      afterOpenChange={(open) => {
        if (!open) return
        form.resetFields()
        if (existing) form.setFieldsValue({
          customerId: existing.customerId, title: existing.title, period: [dayjs(existing.effectiveFrom), existing.effectiveTo ? dayjs(existing.effectiveTo) : null] as never, weightBasis: existing.weightBasis,
          volumetricKgPerCbm: existing.volumetricKgPerCbm, minimumFreight: existing.minimumFreight, detention: existing.detention, taxMode: existing.tax.mode, taxRate: existing.tax.ratePercent, sac: existing.tax.sac,
          requireAcceptedPod: existing.billing.requireAcceptedPod, allowBillingWithoutPod: existing.billing.allowBillingWithoutPod, requiredDocuments: existing.billing.requiredDocuments, extraCharges: existing.extraCharges, rates: existing.rates,
        })
        else form.setFieldsValue({ weightBasis: 'Actual', detention: noDetention, taxMode: 'Forward', taxRate: 0, rates: [emptyRate()], extraCharges: [], period: [dayjs(), null] as never })
      }}
      footer={<Space><Button onClick={onClose}>Cancel</Button><Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save draft</Button></Space>}>
      <Form form={form} layout="vertical" onFinish={(v) => save.mutate(v)} requiredMark="optional">
        <Space.Compact block style={{ gap: 12 }}>
          <Form.Item name="customerId" label="Customer" rules={[{ required: true }]} style={{ flex: 1 }}><Select showSearch optionFilterProp="label" options={customers} disabled={!!existing} /></Form.Item>
          <Form.Item name="title" label="Title" rules={[{ required: true }]} style={{ flex: 1 }}><Input maxLength={200} /></Form.Item>
          <Form.Item name="period" label="Valid from – to" rules={[{ required: true }]}><DatePicker.RangePicker allowEmpty={[false, true]} format="DD MMM YYYY" /></Form.Item>
        </Space.Compact>

        <Divider titlePlacement="left">Freight</Divider>
        <Space wrap align="start">
          <Form.Item name="weightBasis" label="Freight is charged on"><Select style={{ width: 260 }} options={basisOptions} /></Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.weightBasis !== b.weightBasis}>{({ getFieldValue }) => getFieldValue('weightBasis') !== 'Actual' && <Form.Item name="volumetricKgPerCbm" label="Kg per cubic metre" rules={[{ required: true }]}><InputNumber min={1} max={2000} /></Form.Item>}</Form.Item>
          <Form.Item name="minimumFreight" label="Minimum freight per consignment (₹)"><InputNumber min={0} style={{ width: 200 }} /></Form.Item>
        </Space>
        <Form.List name="rates">
          {(fields, { add, remove }) => (
            <>
              {fields.map((f) => (
                <Card key={f.key} size="small" style={{ marginBottom: 10 }} extra={<Button size="small" danger onClick={() => remove(f.name)}>Remove</Button>}>
                  <Space wrap align="start">
                    <Form.Item name={[f.name, 'originLocationId']} label="From (location)"><Select allowClear showSearch optionFilterProp="label" style={{ width: 230 }} options={locations} placeholder="Any" /></Form.Item>
                    <Form.Item name={[f.name, 'originCity']} label="or city"><Input style={{ width: 120 }} /></Form.Item>
                    <Form.Item name={[f.name, 'originState']} label="or state"><Input style={{ width: 120 }} /></Form.Item>
                    <Form.Item name={[f.name, 'destinationLocationId']} label="To (location)"><Select allowClear showSearch optionFilterProp="label" style={{ width: 230 }} options={locations} placeholder="Any" /></Form.Item>
                    <Form.Item name={[f.name, 'destinationCity']} label="or city"><Input style={{ width: 120 }} /></Form.Item>
                    <Form.Item name={[f.name, 'destinationState']} label="or state"><Input style={{ width: 120 }} /></Form.Item>
                  </Space>
                  <Space wrap align="start">
                    <Form.Item name={[f.name, 'material']} label="Material (optional)"><Input style={{ width: 160 }} /></Form.Item>
                    <Form.Item name={[f.name, 'method']} label="Charged"><Select style={{ width: 200 }} options={methodOptions} /></Form.Item>
                    <Form.Item noStyle shouldUpdate>{({ getFieldValue }) => getFieldValue(['rates', f.name, 'method']) !== 'WeightSlabs' && <Form.Item name={[f.name, 'rate']} label="Rate (₹)" rules={[{ required: true }]}><InputNumber min={0} step={0.1} style={{ width: 130 }} /></Form.Item>}</Form.Item>
                    <Form.Item name={[f.name, 'minimumCharge']} label="Minimum for this rate (₹)"><InputNumber min={0} style={{ width: 160 }} /></Form.Item>
                  </Space>
                  <Form.Item noStyle shouldUpdate>
                    {({ getFieldValue }) => getFieldValue(['rates', f.name, 'method']) === 'WeightSlabs' && (
                      <Form.List name={[f.name, 'slabs']}>
                        {(sl, { add: addSlab, remove: removeSlab }) => (
                          <>
                            {sl.map((s) => (
                              <Space key={s.key} align="baseline">
                                <Form.Item name={[s.name, 'fromKg']} label="Above (kg)" rules={[{ required: true }]}><InputNumber min={0} /></Form.Item>
                                <Form.Item name={[s.name, 'toKg']} label="Up to (kg)" extra="Empty = no limit"><InputNumber min={0} /></Form.Item>
                                <Form.Item name={[s.name, 'ratePerKg']} label="₹ per kg" rules={[{ required: true }]}><InputNumber min={0} step={0.1} /></Form.Item>
                                <Button size="small" onClick={() => removeSlab(s.name)}>×</Button>
                              </Space>
                            ))}
                            <Button size="small" onClick={() => addSlab({ fromKg: 0, toKg: null, ratePerKg: 0 })}>Add weight band</Button>
                          </>
                        )}
                      </Form.List>
                    )}
                  </Form.Item>
                </Card>
              ))}
              <Button onClick={() => add(emptyRate())}>Add rate</Button>
            </>
          )}
        </Form.List>
        <Alert type="info" showIcon style={{ margin: '12px 0' }} title="When several rates fit a delivery the most specific one wins (location, then city, then state, then anywhere; a material match counts too). Two equally specific rates are refused, never picked between." />
        <Form.List name="extraCharges">
          {(fields, { add, remove }) => (
            <>
              {fields.map((f) => (
                <Space key={f.key} align="baseline">
                  <Form.Item name={[f.name, 'code']} label="Code" rules={[{ required: true }]}><Input style={{ width: 110 }} /></Form.Item>
                  <Form.Item name={[f.name, 'name']} label="Other agreed charge (per consignment)" rules={[{ required: true }]}><Input style={{ width: 280 }} /></Form.Item>
                  <Form.Item name={[f.name, 'amount']} label="Amount (₹)" rules={[{ required: true }]}><InputNumber min={0} /></Form.Item>
                  <Button size="small" onClick={() => remove(f.name)}>×</Button>
                </Space>
              ))}
              <Button size="small" onClick={() => add({ code: '', name: '', amount: 0 })}>Add other charge</Button>
            </>
          )}
        </Form.List>

        <Divider titlePlacement="left">Detention</Divider>
        <Form.Item name={['detention', 'applicable']} label="Detention applies to this customer" valuePropName="checked"><Switch /></Form.Item>
        <Form.Item noStyle shouldUpdate={(a, b) => a.detention?.applicable !== b.detention?.applicable || a.detention?.freeTime !== b.detention?.freeTime}>
          {({ getFieldValue }) => getFieldValue(['detention', 'applicable']) && (
            <Space wrap align="start">
              <Form.Item name={['detention', 'freeTime']} label="Free time"><Select style={{ width: 280 }} options={[{ value: 'Duration', label: 'A set time after arrival' }, { value: 'EndOfDay', label: 'Until the end of the arrival day (IST)' }]} /></Form.Item>
              <Form.Item name={['detention', 'freeAmount']} label={getFieldValue(['detention', 'freeTime']) === 'EndOfDay' ? 'Extra free time' : 'Free time'}><InputNumber min={0} style={{ width: 100 }} /></Form.Item>
              <Form.Item name={['detention', 'freeUnit']} label="in"><Select style={{ width: 100 }} options={[{ value: 'Hour', label: 'hours' }, { value: 'Day', label: 'days' }]} /></Form.Item>
              <Form.Item name={['detention', 'chargeUnit']} label="Charged per"><Select style={{ width: 100 }} options={[{ value: 'Hour', label: 'hour' }, { value: 'Day', label: 'day' }]} /></Form.Item>
              <Form.Item name={['detention', 'rate']} label="Rate (₹)" rules={[{ required: true }]}><InputNumber min={0} style={{ width: 120 }} /></Form.Item>
              <Form.Item name={['detention', 'rounding']} label="Part of a unit"><Select style={{ width: 260 }} options={roundingOptions} /></Form.Item>
              <Form.Item name={['detention', 'maxCharge']} label="Maximum charge (₹)"><InputNumber min={0} style={{ width: 150 }} /></Form.Item>
            </Space>
          )}
        </Form.Item>

        <Divider titlePlacement="left">Tax and billing</Divider>
        <Space wrap align="start">
          <Form.Item name="taxMode" label="Tax"><Select style={{ width: 220 }} options={[{ value: 'None', label: 'No tax on the invoice' }, { value: 'Forward', label: 'Charged on the invoice' }, { value: 'ReverseCharge', label: 'Reverse charge (recipient pays)' }]} /></Form.Item>
          <Form.Item noStyle shouldUpdate={(a, b) => a.taxMode !== b.taxMode}>{({ getFieldValue }) => getFieldValue('taxMode') !== 'None' && <Form.Item name="taxRate" label="Tax rate (%)" rules={[{ required: true }]}><InputNumber min={0} max={100} step={0.5} /></Form.Item>}</Form.Item>
          <Form.Item name="sac" label="SAC / HSN code"><Input style={{ width: 130 }} maxLength={12} /></Form.Item>
        </Space>
        <Form.Item name="allowBillingWithoutPod" label="Allow billing without an accepted POD" valuePropName="checked" extra="Off by default. When on, an invoice can be raised before the POD is accepted, and the calculation records that it was."><Switch /></Form.Item>
        <Form.Item name="requiredDocuments" label="Documents the customer needs with the invoice"><Input.TextArea rows={2} /></Form.Item>
      </Form>
    </ResponsiveDrawer>
  )
}
