import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, DatePicker, Descriptions, Divider, Form, Input, InputNumber, List, Modal, Select, Space, Table, Tabs, Tag, Timeline, Typography, message } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type BillingRowDto, type CalculationDto, type PodQueueItemDto, type PodStatus } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'
import { useAuth } from '@/features/auth/AuthContext'
import { kg, mins, when } from './fmt'
import { PhotoPicker } from './PhotoPicker'
import { ResponsiveDrawer, StatusTag } from './shared'
import { useCustomerOptions, useDebounced, useFileOpener } from './hooks'

// ---------------------------------------------------------------- POD queue and review
export function PodPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const [view, setView] = useState('pending')
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['ops', 'pods', view, q, page], queryFn: () => opsApi.pods({ view, search: q || undefined, page, pageSize: 25 }) })
  return (
    <>
      <PageHeader title="POD management" description="Delivered consignments waiting for proof, and proofs waiting for a decision. Delivery and POD are separate: a delivery can be complete while its POD is still pending." />
      <Card>
        <Tabs activeKey={view} onChange={(k) => { setView(k); setPage(1) }} items={[{ key: 'pending', label: 'Pending POD' }, { key: 'review', label: 'To review' }, { key: 'all', label: 'All' }]} />
        <Input.Search allowClear placeholder="LR number or PO" style={{ width: 260, marginBottom: 12 }} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
        <Table<PodQueueItemDto> scroll={{ x: 'max-content' }} rowKey="podId" loading={list.isLoading} dataSource={list.data?.items} pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false }}
          columns={[
            { title: 'LR', dataIndex: 'lrNumber', render: (v, r) => <a onClick={() => setOpen(r.lrId)}>{v}</a> }, { title: 'Customer', dataIndex: 'customerName' }, { title: 'Destination', dataIndex: 'destinationName' },
            { title: 'Vehicle', dataIndex: 'vehicleRegistration', render: (v) => v ?? '—' }, { title: 'Unloaded', dataIndex: 'unloadedAt', render: when },
            { title: 'Waiting', dataIndex: 'ageDays', render: (v, r) => (['Accepted'].includes(r.status) ? '—' : <Tag color={v >= 7 ? 'red' : v >= 3 ? 'orange' : 'default'}>{v} day{v === 1 ? '' : 's'}</Tag>) },
            { title: 'POD', dataIndex: 'status', render: (v: PodStatus) => <StatusTag value={v} /> }, { title: 'Files', dataIndex: 'files' },
          ]} />
      </Card>
      <PodDrawer lrId={open} onClose={() => setOpen(null)} onChanged={() => void qc.invalidateQueries({ queryKey: ['ops'] })} canUpload={can('ops.pod.upload')} canReview={can('ops.pod.review')} />
    </>
  )
}

export function PodDrawer({ lrId, onClose, onChanged, canUpload, canReview }: { lrId: string | null; onClose: () => void; onChanged: () => void; canUpload: boolean; canReview: boolean }) {
  const open = useFileOpener()
  const q = useQuery({ queryKey: ['ops', 'pod', lrId], queryFn: () => opsApi.pod(lrId!), enabled: !!lrId })
  const p = q.data
  const refresh = () => { void q.refetch(); onChanged() }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const [files, setFiles] = useState<File[]>([])
  const [remarks, setRemarks] = useState('')
  const [reason, setReason] = useState<{ action: 'reject' | 'reopen' } | null>(null)
  const [text, setText] = useState('')
  const upload = useMutation({ mutationFn: () => opsApi.uploadPod(lrId!, files, remarks || undefined), onSuccess: () => { setFiles([]); setRemarks(''); message.success('POD uploaded for review'); refresh() }, onError: fail })
  const act = useMutation({ mutationFn: ({ action, why }: { action: 'start-review' | 'accept' | 'reject' | 'reopen'; why?: string }) => opsApi.podAction(lrId!, action, why), onSuccess: () => { setReason(null); setText(''); refresh() }, onError: fail })
  const bySubmission = new Map<number, NonNullable<typeof p>['documents']>()
  p?.documents.forEach((d) => bySubmission.set(d.submission, [...(bySubmission.get(d.submission) ?? []), d]))
  const delivered = p?.deliveryStatus === 'UnloadingCompleted'
  return (
    <ResponsiveDrawer open={!!lrId} onClose={onClose} desktopWidth={720} title={p ? `POD for LR ${p.lrNumber}` : 'POD'} loading={q.isLoading}>
      {p && (
        <>
          <Space wrap style={{ marginBottom: 12 }}>
            <span>Delivery:</span><StatusTag value={p.deliveryStatus} /><span>POD:</span><StatusTag value={p.status} />
            {canReview && p.status === 'Received' && <Button onClick={() => act.mutate({ action: 'start-review' })}>Start review</Button>}
            {canReview && ['Received', 'UnderReview'].includes(p.status) && <><Button type="primary" loading={act.isPending} onClick={() => act.mutate({ action: 'accept' })}>Accept</Button><Button danger onClick={() => setReason({ action: 'reject' })}>Reject</Button></>}
            {canReview && p.status === 'Accepted' && <Button onClick={() => setReason({ action: 'reopen' })}>Reopen</Button>}
          </Space>
          <Descriptions size="small" column={2} bordered>
            <Descriptions.Item label="Arrived">{when(p.arrivedAt)}</Descriptions.Item><Descriptions.Item label="Unloaded">{when(p.unloadedAt)}</Descriptions.Item>
            <Descriptions.Item label="Received">{when(p.receivedAt)}</Descriptions.Item><Descriptions.Item label="Reviewed">{p.reviewedAt ? when(p.reviewedAt) : '—'}</Descriptions.Item>
          </Descriptions>
          {p.status === 'Rejected' && <Alert style={{ marginTop: 12 }} type="warning" showIcon title={`Rejected: ${p.rejectionReason}`} description="The driver's page now asks for a corrected copy; the office can also upload one below." />}
          {!delivered && <Alert style={{ marginTop: 12 }} type="info" showIcon title="A POD can be uploaded once unloading has been confirmed for this LR." />}
          <Divider titlePlacement="left">Documents</Divider>
          {[...bySubmission.entries()].sort((a, b) => b[0] - a[0]).map(([n, docs]) => (
            <Card key={n} size="small" style={{ marginBottom: 8 }} title={`Submission ${n}${n === p.submission ? ' (current)' : ''}`}>
              <List size="small" dataSource={docs} renderItem={(d) => <List.Item actions={[<a key="v" onClick={() => void open(() => opsApi.podFile(d.id), d.fileName)}>Open</a>]}>{d.fileName} · {d.source === 'QrWeb' ? 'driver' : 'office'} · {when(d.uploadedAt)}{d.remarks ? ` · ${d.remarks}` : ''}</List.Item>} />
            </Card>
          ))}
          {p.documents.length === 0 && <Typography.Text type="secondary">No document yet.</Typography.Text>}
          {canUpload && delivered && p.status !== 'Accepted' && (
            <Card size="small" title="Upload a scan or photo" style={{ marginTop: 12 }}>
              <PhotoPicker files={files} onChange={setFiles} max={20} />
              <Input.TextArea rows={2} placeholder="Remarks (optional)" style={{ margin: '8px 0' }} value={remarks} onChange={(e) => setRemarks(e.target.value)} />
              <Button type="primary" disabled={files.length === 0} loading={upload.isPending} onClick={() => upload.mutate()}>Upload</Button>
            </Card>
          )}
          <Divider titlePlacement="left">Review history</Divider>
          <Timeline items={p.reviews.map((r) => ({ children: <span><b>{r.action}</b> · {when(r.at)}{r.reason ? ` · ${r.reason}` : ''}</span> }))} />
          <Modal open={reason !== null} title={reason?.action === 'reject' ? 'Reject the POD' : 'Reopen the POD'} onCancel={() => setReason(null)} okText={reason?.action === 'reject' ? 'Reject' : 'Reopen'} okButtonProps={{ danger: reason?.action === 'reject', disabled: !text.trim() }}
            onOk={() => act.mutate({ action: reason!.action, why: text })} confirmLoading={act.isPending} destroyOnHidden>
            <Input.TextArea rows={3} placeholder="Reason (required)" value={text} onChange={(e) => setText(e.target.value)} />
          </Modal>
        </>
      )}
    </ResponsiveDrawer>
  )
}

// ---------------------------------------------------------------- billing workbench
export function BillingPage() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const customers = useCustomerOptions()
  const [customerId, setCustomerId] = useState<string>()
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [podStatus, setPodStatus] = useState<PodStatus>()
  const [range, setRange] = useState<[dayjs.Dayjs, dayjs.Dayjs] | null>(null)
  const [onlyUnbilled, setOnlyUnbilled] = useState(true)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<React.Key[]>([])
  const [calc, setCalc] = useState<string | null>(null)
  const [invoicing, setInvoicing] = useState(false)
  const list = useQuery({ queryKey: ['ops', 'billing', customerId, q, podStatus, range?.[0]?.format(), range?.[1]?.format(), onlyUnbilled, page], queryFn: () => opsApi.billing({
    customerId, search: q || undefined, podStatus, from: range?.[0]?.format('YYYY-MM-DD'), to: range?.[1]?.format('YYYY-MM-DD'), invoiced: onlyUnbilled ? false : undefined, page, pageSize: 25 }) })
  const rows = list.data?.items ?? []
  const refresh = () => void qc.invalidateQueries({ queryKey: ['ops'] })
  const calculate = useMutation({
    mutationFn: (ids: string[]) => opsApi.calculate(ids),
    onSuccess: (r) => { r.failures.forEach((f) => message.warning(f.message)); if (r.calculations.length) message.success(`${r.calculations.length} calculated`); refresh() }, onError: (e) => message.error(toApiError(e).message),
  })
  const chosen = rows.filter((r) => selected.includes(r.lrId))
  const billable = chosen.filter((r) => r.eligible)
  return (
    <>
      <PageHeader title="Freight and detention billing" description="Delivered LRs, priced from each customer's agreement. Calculate, review the breakdown, then invoice." />
      <Card>
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear showSearch optionFilterProp="label" placeholder="Customer" style={{ width: 260 }} options={customers} value={customerId} onChange={(v) => { setCustomerId(v); setPage(1) }} />
          <Input.Search allowClear placeholder="LR or PO" style={{ width: 200 }} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="POD status" style={{ width: 160 }} value={podStatus} onChange={(v) => { setPodStatus(v); setPage(1) }} options={['Pending', 'Received', 'UnderReview', 'Accepted', 'Rejected'].map((v) => ({ value: v, label: v }))} />
          <DatePicker.RangePicker format="DD MMM YYYY" onChange={(v) => { setRange(v as never); setPage(1) }} />
          <Checkbox checked={onlyUnbilled} onChange={(e) => setOnlyUnbilled(e.target.checked)}>Not yet invoiced</Checkbox>
        </Space>
        <Space wrap style={{ marginBottom: 12, display: 'flex' }}>
          {can('ops.billing.calculate') && <Button disabled={chosen.length === 0} loading={calculate.isPending} onClick={() => calculate.mutate(chosen.map((r) => r.lrId))}>Calculate selected ({chosen.length})</Button>}
          {can('ops.invoice.manage') && <Button type="primary" disabled={billable.length === 0} onClick={() => setInvoicing(true)}>Create invoice ({billable.length} ready)</Button>}
        </Space>
        <Table<BillingRowDto> rowKey="lrId" loading={list.isLoading} dataSource={rows} scroll={{ x: 1300 }} rowSelection={{ selectedRowKeys: selected, onChange: setSelected, getCheckboxProps: (r) => ({ disabled: !!r.invoiceId }) }}
          pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false }}
          columns={[
            { title: 'LR', dataIndex: 'lrNumber' }, { title: 'Customer', dataIndex: 'customerName' }, { title: 'Destination', dataIndex: 'destinationName' }, { title: 'Arrived', dataIndex: 'arrivedAt', render: when }, { title: 'Unloaded', dataIndex: 'unloadedAt', render: when },
            { title: 'POD', dataIndex: 'podStatus', render: (v) => <StatusTag value={v} /> }, { title: 'Weight', dataIndex: 'billableWeightKg', render: kg },
            { title: 'Freight', dataIndex: 'basicFreight', align: 'right', render: (v) => (v == null ? '—' : formatInrExact(v)) }, { title: 'Detention', dataIndex: 'detention', align: 'right', render: (v) => (v == null ? '—' : formatInrExact(v)) },
            { title: 'Other', dataIndex: 'otherCharges', align: 'right', render: (v) => (v == null ? '—' : formatInrExact(v)) }, { title: 'Total (before tax)', dataIndex: 'totalPreTax', align: 'right', render: (v) => (v == null ? '—' : <b>{formatInrExact(v)}</b>) },
            { title: 'Status', render: (_, r) => (r.invoiceNumber ? <Tag color="purple">{r.invoiceNumber}</Tag> : r.calcStatus ? <StatusTag value={r.calcStatus} /> : <Tag>Not calculated</Tag>) },
            { title: 'Holds', dataIndex: 'holds', render: (h: string[]) => (h.length ? <Typography.Text type="danger">{h[0]}{h.length > 1 ? ` (+${h.length - 1})` : ''}</Typography.Text> : '—') },
            { title: '', render: (_, r) => r.calcId && <a onClick={() => setCalc(r.calcId)}>Breakdown</a> },
          ]} />
      </Card>
      <CalculationDrawer id={calc} onClose={() => setCalc(null)} onChanged={refresh} />
      <InvoiceCreateModal open={invoicing} ids={billable.map((r) => r.calcId!).filter(Boolean)} onClose={() => setInvoicing(false)} onDone={() => { setInvoicing(false); setSelected([]); refresh() }} />
    </>
  )
}

function CalculationDrawer({ id, onClose, onChanged }: { id: string | null; onClose: () => void; onChanged: () => void }) {
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['ops', 'calc', id], queryFn: () => opsApi.calculation(id!), enabled: !!id })
  const c: CalculationDto | undefined = q.data
  const [hold, setHold] = useState(false)
  const [form] = Form.useForm<{ amount: number; reason: string }>()
  const refresh = () => { void q.refetch(); onChanged() }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const review = useMutation({ mutationFn: () => opsApi.reviewCalculation(id!), onSuccess: refresh, onError: fail })
  const resolve = useMutation({ mutationFn: (v: { amount: number; reason: string }) => opsApi.resolveHold(id!, v.amount, v.reason), onSuccess: () => { setHold(false); refresh() }, onError: fail })
  const d = c?.detention
  return (
    <ResponsiveDrawer open={!!id} onClose={onClose} desktopWidth={720} title={c ? `Charges for ${c.lrNumber} · calculation ${c.version}` : 'Charges'} loading={q.isLoading}>
      {c && (
        <>
          <Space wrap style={{ marginBottom: 12 }}>
            <StatusTag value={c.status} />
            {can('ops.billing.calculate') && c.status === 'Calculated' && <Button type="primary" loading={review.isPending} onClick={() => review.mutate()}>Review and approve for billing</Button>}
            {can('ops.billing.calculate') && c.status === 'OnHold' && c.detention?.status === 'OnHold' && <Button onClick={() => setHold(true)}>Resolve detention hold</Button>}
          </Space>
          {c.holds.length > 0 && <Alert type="error" showIcon style={{ marginBottom: 12 }} title="On hold" description={<ul style={{ margin: 0, paddingLeft: 18 }}>{c.holds.map((h) => <li key={h}>{h}</li>)}</ul>} />}
          {c.billedWithoutPod && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title="Billed without an accepted POD, as this customer's agreement allows." />}
          <Table scroll={{ x: 'max-content' }} size="small" pagination={false} rowKey="code" dataSource={[...c.lines, ...(c.detentionOverride != null ? [{ code: 'DETENTION_MANUAL', description: `Detention (decided by reviewer: ${c.detentionOverrideReason})`, amount: c.detentionOverride, basis: 'manual' }] : [])]}
            columns={[{ title: 'Charge', dataIndex: 'description' }, { title: 'Worked out as', dataIndex: 'basis' }, { title: 'Amount', dataIndex: 'amount', align: 'right', render: (v) => formatInrExact(v) }]}
            summary={() => <Table.Summary.Row><Table.Summary.Cell index={0} colSpan={2}><b>Total before tax</b></Table.Summary.Cell><Table.Summary.Cell index={1} align="right"><b>{formatInrExact(c.totalPreTax)}</b></Table.Summary.Cell></Table.Summary.Row>} />
          <Divider titlePlacement="left">Detention</Divider>
          {d && d.status !== 'NotApplicable' ? (
            <Descriptions size="small" bordered column={{ xs: 1, md: 2 }}>
              <Descriptions.Item label="Arrival">{when(d.arrival)}</Descriptions.Item><Descriptions.Item label="Unloading completed">{when(d.unloading)}</Descriptions.Item>
              <Descriptions.Item label="Time at destination">{mins(d.elapsedHours * 60)}</Descriptions.Item><Descriptions.Item label="Free until">{when(d.freeUntil)}</Descriptions.Item>
              <Descriptions.Item label="Chargeable">{mins(d.chargeableHours * 60)} → {d.units} {d.chargeUnit?.toLowerCase()}(s)</Descriptions.Item><Descriptions.Item label="Rate">{formatInrExact(d.rate)} per {d.chargeUnit?.toLowerCase()} ({d.rounding})</Descriptions.Item>
              <Descriptions.Item label="Maximum">{d.cap == null ? 'None' : formatInrExact(d.cap)}{d.capApplied ? ' (applied)' : ''}</Descriptions.Item><Descriptions.Item label="Detention">{formatInrExact(d.amount)}</Descriptions.Item>
            </Descriptions>
          ) : <Typography.Text type="secondary">Detention does not apply to this customer.</Typography.Text>}
          <Divider titlePlacement="left">How this was worked out</Divider>
          <ol style={{ paddingLeft: 18 }}>{c.trace.map((t, i) => <li key={`${i}${t}`}>{t}</li>)}</ol>
          <Typography.Text type="secondary">Agreement revision {c.agreementRevision ?? '—'} · calculated {when(c.calculatedAt)} · billable weight {kg(c.billableWeightKg)} ({c.weightBasisUsed ?? '—'})</Typography.Text>
          <Modal open={hold} title="Resolve the detention hold" onCancel={() => setHold(false)} onOk={() => form.submit()} confirmLoading={resolve.isPending} okText="Save" destroyOnHidden>
            <Alert type="info" showIcon style={{ marginBottom: 12 }} title="A time needed for detention is missing or inconsistent. Correct the LR's times and recalculate, or decide the detention amount here (zero is allowed). The reason is kept." />
            <Form form={form} layout="vertical" onFinish={(v) => resolve.mutate(v)}>
              <Form.Item name="amount" label="Detention to charge (₹)" rules={[{ required: true }]}><InputNumber min={0} style={{ width: 180 }} /></Form.Item>
              <Form.Item name="reason" label="Reason" rules={[{ required: true }]}><Input.TextArea rows={2} /></Form.Item>
            </Form>
          </Modal>
        </>
      )}
    </ResponsiveDrawer>
  )
}

function InvoiceCreateModal({ open, ids, onClose, onDone }: { open: boolean; ids: string[]; onClose: () => void; onDone: () => void }) {
  const [form] = Form.useForm<{ date: dayjs.Dayjs; reference?: string; notes?: string }>()
  const create = useMutation({
    mutationFn: (v: { date: dayjs.Dayjs; reference?: string; notes?: string }) => opsApi.createInvoice({ calculationIds: ids, invoiceDate: v.date.format('YYYY-MM-DD'), customerReference: v.reference ?? null, notes: v.notes ?? null }),
    onSuccess: (i) => { message.success(`Invoice ${i.summary.number} created`); onDone() }, onError: (e) => message.error(toApiError(e).message),
  })
  return (
    <Modal open={open} title={`Create an invoice for ${ids.length} deliver${ids.length === 1 ? 'y' : 'ies'}`} onCancel={onClose} onOk={() => form.submit()} confirmLoading={create.isPending} okText="Create invoice" destroyOnHidden>
      <Form form={form} layout="vertical" initialValues={{ date: dayjs() }} onFinish={(v) => create.mutate(v)}>
        <Alert type="info" showIcon style={{ marginBottom: 12 }} title="Only reviewed charges of one customer can go on one invoice. Tax comes from the customer's agreement. Once issued, an invoice is not recalculated." />
        <Form.Item name="date" label="Invoice date" rules={[{ required: true }]}><DatePicker format="DD MMM YYYY" /></Form.Item>
        <Form.Item name="reference" label="Customer PO / reference on the invoice"><Input /></Form.Item>
        <Form.Item name="notes" label="Notes"><Input.TextArea rows={2} /></Form.Item>
      </Form>
    </Modal>
  )
}

