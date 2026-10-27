import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, DatePicker, Select, Space, Statistic, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type ArrearCustomerDto, type ArrearRowDto } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'
import { day } from './fmt'
import { useCustomerOptions } from './hooks'
import { StatusTag } from './shared'

const money = (v: number | null | undefined) => (v == null ? '—' : formatInrExact(v))
const signed = (v: number | null | undefined) => (v == null ? '—' : <Typography.Text type={v > 0 ? 'danger' : v < 0 ? 'success' : undefined}>{formatInrExact(v)}</Typography.Text>)

const csvCell = (v: unknown) => `"${String(v ?? '').replaceAll('"', '""')}"`
function downloadCsv(rows: ArrearRowDto[]) {
  const head = ['Invoice', 'Invoice date', 'Customer', 'Payment', 'LR', 'Delivery date', 'Invoiced revision', 'Current revision', 'Invoiced freight', 'Revised freight', 'Freight difference', 'Tax difference', 'Total difference', 'Note']
  const lines = [head, ...rows.map((r) => [r.invoiceNumber, r.invoiceDate, r.customerName, r.paymentState, r.lrNumber, r.deliveryDate, r.invoicedRevision ?? '', r.currentRevision, r.invoicedFreight, r.revisedFreight ?? '', r.freightDifference ?? '', r.taxDifference ?? '', r.totalDifference ?? '', r.note ?? ''])]
  const url = URL.createObjectURL(new Blob([lines.map((l) => l.map(csvCell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = 'rate-revision-arrears.csv'
  a.click()
  URL.revokeObjectURL(url)
}

/** Invoices that were priced on an agreement revision that no longer applies to their delivery date, and what they would come to now. */
export function ArrearsPage() {
  const customers = useCustomerOptions()
  const [customerId, setCustomerId] = useState<string>()
  const [range, setRange] = useState<[string, string] | null>(null)
  const [includeUnchanged, setIncludeUnchanged] = useState(false)
  const q = useQuery({
    queryKey: ['ops', 'arrears', customerId, range?.[0], range?.[1], includeUnchanged],
    queryFn: () => opsApi.arrears({ customerId, from: range?.[0], to: range?.[1], includeUnchanged }),
  })
  const d = q.data
  return (
    <>
      <PageHeader title="Rate revision arrears" description="Invoices issued at the rates of an agreement revision that has since been replaced for their delivery date, and the difference the current revision would make. Issued invoices are never changed." />
      <Card>
        <Space wrap style={{ marginBottom: 12 }}>
          <Select allowClear showSearch optionFilterProp="label" placeholder="Customer" style={{ width: 260 }} options={customers} value={customerId} onChange={setCustomerId} />
          <DatePicker.RangePicker format="DD MMM YYYY" onChange={(v) => setRange(v?.[0] && v[1] ? [v[0].format('YYYY-MM-DD'), v[1].format('YYYY-MM-DD')] : null)} />
          <Checkbox checked={includeUnchanged} onChange={(e) => setIncludeUnchanged(e.target.checked)}>Also show deliveries whose amount did not change</Checkbox>
          <Button disabled={!d || d.rows.length === 0} onClick={() => d && downloadCsv(d.rows)}>Download CSV</Button>
        </Space>
        {q.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} title={toApiError(q.error).message} />}
        {d && (
          <Space size={32} wrap style={{ marginBottom: 16 }}>
            <Statistic title="Freight difference" value={formatInrExact(d.freightDifference)} />
            <Statistic title="Tax difference" value={formatInrExact(d.taxDifference)} />
            <Statistic title="Total arrears (+) / excess billed (−)" value={formatInrExact(d.totalDifference)} />
            {d.notPriced > 0 && <Statistic title="Could not be repriced" value={d.notPriced} />}
          </Space>
        )}
        {d && d.customers.length > 0 && (
          <Table<ArrearCustomerDto> size="small" style={{ marginBottom: 16 }} pagination={false} rowKey="customerId" dataSource={d.customers} scroll={{ x: 'max-content' }}
            columns={[{ title: 'Customer', dataIndex: 'customerName' }, { title: 'Invoices', dataIndex: 'invoices', align: 'right' }, { title: 'Deliveries', dataIndex: 'lines', align: 'right' },
              { title: 'Freight', dataIndex: 'freightDifference', align: 'right', render: signed }, { title: 'Tax', dataIndex: 'taxDifference', align: 'right', render: signed },
              { title: 'Total', dataIndex: 'totalDifference', align: 'right', render: (v: number) => <b>{signed(v)}</b> }]} />
        )}
        <Table<ArrearRowDto> size="small" loading={q.isLoading} rowKey={(r) => `${r.invoiceId}${r.lrNumber}`} dataSource={d?.rows ?? []} scroll={{ x: 'max-content' }} pagination={{ pageSize: 25, showSizeChanger: false }}
          locale={{ emptyText: 'No invoiced delivery is affected by a rate revision.' }}
          columns={[
            { title: 'Invoice', dataIndex: 'invoiceNumber' }, { title: 'Date', dataIndex: 'invoiceDate', render: day }, { title: 'Customer', dataIndex: 'customerName' },
            { title: 'Payment', dataIndex: 'paymentState', render: (v) => <StatusTag value={v} /> }, { title: 'LR', dataIndex: 'lrNumber' }, { title: 'Delivered', dataIndex: 'deliveryDate', render: day },
            { title: 'Revision', render: (_, r) => <span>{r.invoicedRevision ?? '—'} <Tag>→ {r.currentRevision || '—'}</Tag></span> },
            { title: 'Invoiced', dataIndex: 'invoicedFreight', align: 'right', render: money }, { title: 'Revised', dataIndex: 'revisedFreight', align: 'right', render: money },
            { title: 'Freight diff.', dataIndex: 'freightDifference', align: 'right', render: signed }, { title: 'Tax diff.', dataIndex: 'taxDifference', align: 'right', render: signed },
            { title: 'Total diff.', dataIndex: 'totalDifference', align: 'right', render: (v: number | null) => <b>{signed(v)}</b> },
            { title: 'Note', dataIndex: 'note', render: (v: string | null) => (v ? <Typography.Text type="danger">{v}</Typography.Text> : '') },
          ]} />
        <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>
          Freight is repriced from the delivery's saved lane, weight and date on the revision in force for that date today; detention is not repriced. Tax follows the invoice's own tax terms.
          A positive amount is arrears the customer owes; a negative amount was billed in excess. The accountant decides how to settle it (a supplementary invoice or a credit note).
        </Typography.Paragraph>
      </Card>
    </>
  )
}
