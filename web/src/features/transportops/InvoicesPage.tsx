import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, DatePicker, Descriptions, Divider, Form, Input, InputNumber, Modal, Select, Space, Statistic, Table, Tabs, Tag, Timeline, Typography, message } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type InvoiceDto, type InvoiceSummaryDto, type PaymentState, type ReceivableRowDto, type SubmissionStatus } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'
import { useAuth } from '@/features/auth/AuthContext'
import { day, when } from './fmt'
import { ResponsiveDrawer, StatusTag } from './shared'
import { useCustomerOptions, useDebounced } from './hooks'

const money = (v: number) => formatInrExact(v)

export function InvoicesPage() {
  const qc = useQueryClient()
  const customers = useCustomerOptions()
  const [tab, setTab] = useState('invoices')
  const [search, setSearch] = useState('')
  const q = useDebounced(search)
  const [customerId, setCustomerId] = useState<string>()
  const [submission, setSubmission] = useState<SubmissionStatus>()
  const [payment, setPayment] = useState<PaymentState>()
  const [page, setPage] = useState(1)
  const [open, setOpen] = useState<string | null>(null)
  const list = useQuery({ queryKey: ['ops', 'invoices', q, customerId, submission, payment, page], queryFn: () => opsApi.invoices({ search: q || undefined, customerId, submission, payment, page, pageSize: 25 }) })
  const recv = useQuery({ queryKey: ['ops', 'receivables'], queryFn: opsApi.receivables, enabled: tab === 'receivables' })
  return (
    <>
      <PageHeader title="Invoices" description="Customer invoices raised from reviewed freight and detention. Track submission to the customer's portal and payments." />
      <Card>
        <Tabs activeKey={tab} onChange={setTab} items={[{ key: 'invoices', label: 'Invoices' }, { key: 'receivables', label: 'Receivables ageing' }]} />
        {tab === 'invoices' ? (
          <>
            <Space wrap style={{ marginBottom: 12 }}>
              <Input.Search allowClear placeholder="Invoice, customer or PO" style={{ width: 260 }} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
              <Select allowClear showSearch optionFilterProp="label" placeholder="Customer" style={{ width: 240 }} options={customers} value={customerId} onChange={(v) => { setCustomerId(v); setPage(1) }} />
              <Select allowClear placeholder="Portal submission" style={{ width: 200 }} value={submission} onChange={(v) => { setSubmission(v); setPage(1) }} options={['NotSubmitted', 'Submitted', 'Acknowledged', 'ResubmissionRequired'].map((v) => ({ value: v, label: v.replace(/([a-z])([A-Z])/g, '$1 $2') }))} />
              <Select allowClear placeholder="Payment" style={{ width: 160 }} value={payment} onChange={(v) => { setPayment(v); setPage(1) }} options={['Unpaid', 'PartiallyPaid', 'Paid'].map((v) => ({ value: v, label: v.replace(/([a-z])([A-Z])/g, '$1 $2') }))} />
            </Space>
            <Table<InvoiceSummaryDto> rowKey="id" loading={list.isLoading} dataSource={list.data?.items} scroll={{ x: 1100 }} pagination={{ current: page, pageSize: 25, total: list.data?.totalCount, onChange: setPage, showSizeChanger: false }}
              columns={[
                { title: 'Invoice', dataIndex: 'number', render: (v, r) => <a onClick={() => setOpen(r.id)}>{v}</a> }, { title: 'Customer', dataIndex: 'customerName' }, { title: 'Date', dataIndex: 'invoiceDate', render: day }, { title: 'Due', dataIndex: 'dueDate', render: day },
                { title: 'Total', dataIndex: 'grandTotal', align: 'right', render: money }, { title: 'Outstanding', dataIndex: 'outstanding', align: 'right', render: (v, r) => (r.status === 'Issued' ? money(v) : '—') },
                { title: 'Status', dataIndex: 'status', render: (v) => <StatusTag value={v} /> }, { title: 'Portal', dataIndex: 'submission', render: (v) => <StatusTag value={v} /> }, { title: 'Payment', dataIndex: 'paymentState', render: (v) => <StatusTag value={v} /> },
                { title: 'Overdue', dataIndex: 'daysOverdue', render: (v) => (v > 0 ? <Tag color="red">{v} days</Tag> : '—') },
              ]} />
          </>
        ) : (
          <Table<ReceivableRowDto> scroll={{ x: 'max-content' }} rowKey="customerId" loading={recv.isLoading} dataSource={recv.data} pagination={false}
            columns={[{ title: 'Customer', dataIndex: 'customerName' }, { title: 'Not yet due', dataIndex: 'notDue', align: 'right', render: money }, { title: '1–30 days', dataIndex: 'days1To30', align: 'right', render: money }, { title: '31–60 days', dataIndex: 'days31To60', align: 'right', render: money },
              { title: '61–90 days', dataIndex: 'days61To90', align: 'right', render: money }, { title: 'Over 90 days', dataIndex: 'over90', align: 'right', render: money }, { title: 'Total outstanding', dataIndex: 'total', align: 'right', render: (v) => <b>{money(v)}</b> }, { title: 'Invoices', dataIndex: 'invoices' }]} />
        )}
      </Card>
      <InvoiceDrawer id={open} onClose={() => setOpen(null)} onChanged={() => void qc.invalidateQueries({ queryKey: ['ops'] })} />
    </>
  )
}

function InvoiceDrawer({ id, onClose, onChanged }: { id: string | null; onClose: () => void; onChanged: () => void }) {
  const { can } = useAuth()
  const q = useQuery({ queryKey: ['ops', 'invoice', id], queryFn: () => opsApi.invoice(id!), enabled: !!id })
  const i: InvoiceDto | undefined = q.data
  const refresh = () => { void q.refetch(); onChanged() }
  const fail = (e: unknown) => message.error(toApiError(e).message)
  const nav = useNavigate()
  const [modal, setModal] = useState<'submit' | 'reject' | 'cancel' | 'payment' | 'ack' | null>(null)
  const [form] = Form.useForm()
  const close = () => { setModal(null); form.resetFields() }
  const run = useMutation({
    mutationFn: (v: Record<string, never>) => {
      const x = v as Record<string, unknown>
      switch (modal) {
        case 'submit': return opsApi.submitInvoice(id!, { portalReference: (x.portal as string) ?? null, documentReference: (x.doc as string) ?? null, notes: (x.notes as string) ?? null })
        case 'ack': return opsApi.acknowledgeInvoice(id!, (x.portal as string) ?? null)
        case 'reject': return opsApi.rejectSubmission(id!, x.reason as string)
        case 'cancel': return opsApi.cancelInvoice(id!, x.reason as string)
        default: return opsApi.recordPayment(id!, { amount: x.amount as number, receivedOn: (x.on as dayjs.Dayjs).format('YYYY-MM-DD'), mode: (x.mode as string) ?? null, reference: (x.reference as string) ?? null, remarks: (x.remarks as string) ?? null, tdsAmount: (x.tds as number | undefined) ?? 0 })
      }
    },
    onSuccess: () => { close(); refresh() }, onError: fail,
  })
  const s = i?.summary
  const manage = can('ops.invoice.manage')
  return (
    <ResponsiveDrawer open={!!id} onClose={onClose} desktopWidth={860} title={s ? `Invoice ${s.number}` : 'Invoice'} loading={q.isLoading}>
      {i && s && (
        <>
          <Space wrap style={{ marginBottom: 12 }}>
            <StatusTag value={s.status} /><StatusTag value={s.submission} /><StatusTag value={s.paymentState} />
            {s.status === 'Issued' && <Button onClick={() => nav(`/ops/invoices/${s.id}/print`)}>Print invoice</Button>}
            {manage && s.status === 'Issued' && ['NotSubmitted', 'ResubmissionRequired'].includes(s.submission) && <Button type="primary" onClick={() => setModal('submit')}>{s.submission === 'ResubmissionRequired' ? 'Resubmit to customer portal' : 'Record submission to portal'}</Button>}
            {manage && s.submission === 'Submitted' && <><Button onClick={() => setModal('ack')}>Customer accepted</Button><Button danger onClick={() => setModal('reject')}>Customer rejected</Button></>}
            {can('ops.payment.manage') && s.status === 'Issued' && s.outstanding > 0 && <Button onClick={() => { setModal('payment'); form.setFieldsValue({ on: dayjs(), amount: s.outstanding }) }}>Record payment</Button>}
            {manage && s.status !== 'Cancelled' && s.amountPaid === 0 && !['Submitted', 'Acknowledged'].includes(s.submission) && <Button danger onClick={() => setModal('cancel')}>Cancel invoice</Button>}
          </Space>
          {s.status === 'PendingApproval' && <Alert type="info" showIcon style={{ marginBottom: 12 }} title="Waiting for approval (Workflow → Approvals). It is issued as soon as it is approved." />}
          {s.status === 'Cancelled' && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title={`Cancelled: ${i.cancelReason ?? ''}`} />}
          {s.submission === 'ResubmissionRequired' && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={`The customer rejected it: ${[...i.submissions].reverse().find((x) => x.rejectionReason)?.rejectionReason ?? ''}`} />}
          <Descriptions size="small" bordered column={{ xs: 1, md: 2 }}>
            <Descriptions.Item label="Customer">{s.customerName}</Descriptions.Item><Descriptions.Item label="GSTIN">{i.customerGstin ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Billing address" span={2}>{i.billingAddress ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="Invoice date">{day(s.invoiceDate)}</Descriptions.Item><Descriptions.Item label="Due">{day(s.dueDate)}</Descriptions.Item>
            <Descriptions.Item label="SAC">{i.sac ?? '—'}</Descriptions.Item>
          </Descriptions>
          <Table size="small" style={{ marginTop: 12 }} pagination={false} rowKey="id" dataSource={i.lines} scroll={{ x: 900 }}
            columns={[{ title: '#', dataIndex: 'lineNumber', width: 40 }, { title: 'LR No.', dataIndex: 'lrNumber' }, { title: 'Freight', dataIndex: 'basicFreight', align: 'right', render: money }, { title: 'Detention', dataIndex: 'detention', align: 'right', render: money },
              { title: 'Other', dataIndex: 'otherCharges', align: 'right', render: money }, { title: 'Taxable', dataIndex: 'taxable', align: 'right', render: money }, { title: 'CGST', dataIndex: 'cgst', align: 'right', render: money },
              { title: 'SGST', dataIndex: 'sgst', align: 'right', render: money }, { title: 'IGST', dataIndex: 'igst', align: 'right', render: money }]} />
          <Space size={32} style={{ margin: '16px 0' }} wrap>
            <Statistic title="Subtotal" value={money(s.subtotal)} />
            <Statistic title={i.taxMode === 'ReverseCharge' ? 'Tax (reverse charge)' : 'Tax'} value={i.taxMode === 'ReverseCharge' ? 'Payable by recipient' : money(s.tax)} />
            <Statistic title="Grand total" value={money(s.grandTotal)} />
            <Statistic title="Received in bank" value={money(s.amountPaid - (s.tdsDeducted ?? 0))} />
            {(s.tdsDeducted ?? 0) > 0 && <Statistic title="TDS deducted" value={money(s.tdsDeducted ?? 0)} />}
            <Statistic title="Outstanding" value={money(s.outstanding)} />
          </Space>
          <Divider titlePlacement="left">Portal submissions</Divider>
          <Timeline items={i.submissions.map((x) => ({ color: x.status === 'Rejected' ? 'red' : x.status === 'Acknowledged' ? 'green' : 'blue', children: (
            <div><b>Attempt {x.attempt}</b> · {when(x.submittedAt)} · {x.portalReference ?? 'no reference'} · <StatusTag value={x.status} />{x.rejectionReason && <div><Typography.Text type="danger">{x.rejectionReason}</Typography.Text></div>}{x.notes && <div><Typography.Text type="secondary">{x.notes}</Typography.Text></div>}</div>) }))} />
          {i.submissions.length === 0 && <Typography.Text type="secondary">Not submitted yet.</Typography.Text>}
          <Divider titlePlacement="left">Payments</Divider>
          <Table scroll={{ x: 'max-content' }} size="small" pagination={false} rowKey="id" dataSource={i.payments} locale={{ emptyText: 'No payments yet' }}
            columns={[{ title: 'Received', dataIndex: 'receivedOn', render: day }, { title: 'Received in bank', dataIndex: 'amount', align: 'right', render: money }, { title: 'TDS', dataIndex: 'tdsAmount', align: 'right', render: (v: number | undefined) => ((v ?? 0) > 0 ? money(v ?? 0) : '—') }, { title: 'Mode', dataIndex: 'mode', render: (v) => v ?? '—' }, { title: 'Reference', dataIndex: 'reference', render: (v) => v ?? '—' }, { title: 'Remarks', dataIndex: 'remarks', render: (v) => v ?? '—' }]} />
          <Modal open={modal !== null} onCancel={close} onOk={() => form.submit()} confirmLoading={run.isPending} destroyOnHidden okText="Save" okButtonProps={{ danger: modal === 'reject' || modal === 'cancel' }}
            title={{ submit: 'Record submission to the customer portal', ack: 'The customer accepted the invoice', reject: 'The customer rejected the invoice', cancel: 'Cancel the invoice', payment: 'Record a payment' }[modal ?? 'submit']}>
            <Form form={form} layout="vertical" onFinish={(v) => run.mutate(v)}
              onValuesChange={(changed) => { if (modal === 'payment' && 'tds' in changed) form.setFieldValue('amount', Math.max(0, Math.round((s.outstanding - (changed.tds ?? 0)) * 100) / 100)) }}>
              {(modal === 'submit' || modal === 'ack') && <Form.Item name="portal" label="Portal / acknowledgement reference"><Input /></Form.Item>}
              {modal === 'submit' && <><Form.Item name="doc" label="Invoice document reference"><Input /></Form.Item><Form.Item name="notes" label="Notes"><Input.TextArea rows={2} /></Form.Item><Alert type="info" showIcon title="Submission to the customer's portal is done by hand; this records that it was done." /></>}
              {(modal === 'reject' || modal === 'cancel') && <Form.Item name="reason" label={modal === 'reject' ? "Customer's reason" : 'Reason'} rules={[{ required: true }]}><Input.TextArea rows={3} /></Form.Item>}
              {modal === 'payment' && (
                <>
                  <Form.Item name="tds" label="TDS deducted by the customer (₹)" extra="Tax the customer kept back and did not send to the bank. Leave empty if none."><InputNumber min={0} precision={2} style={{ width: 200 }} /></Form.Item>
                  <Form.Item name="amount" label="Amount received (₹)" extra="What reached the bank: it should match the bank statement." rules={[{ required: true }]}><InputNumber min={0.01} precision={2} style={{ width: 200 }} /></Form.Item>
                  <Form.Item noStyle shouldUpdate>{() => {
                    const settled = ((form.getFieldValue('amount') as number | undefined) ?? 0) + ((form.getFieldValue('tds') as number | undefined) ?? 0)
                    return <Typography.Paragraph type={Math.abs(settled - s.outstanding) < 0.005 ? 'success' : 'secondary'}>Settles {money(settled)} of the {money(s.outstanding)} outstanding.</Typography.Paragraph>
                  }}</Form.Item>
                  <Form.Item name="on" label="Received on" rules={[{ required: true }]}><DatePicker format="DD MMM YYYY" /></Form.Item>
                  <Form.Item name="mode" label="Mode"><Select allowClear options={['NEFT', 'RTGS', 'UPI', 'Cheque', 'Cash'].map((v) => ({ value: v, label: v }))} /></Form.Item>
                  <Form.Item name="reference" label="UTR / cheque number"><Input /></Form.Item>
                  <Form.Item name="remarks" label="Remarks"><Input /></Form.Item>
                </>
              )}
            </Form>
          </Modal>
        </>
      )}
    </ResponsiveDrawer>
  )
}
