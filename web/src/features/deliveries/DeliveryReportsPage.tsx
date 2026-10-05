import { DownloadOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { App, Button, Card, DatePicker, Flex, Input, Segmented, Select, Table, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'

const REPORTS = [
  ['deliveries', 'Deliveries'], ['pods', 'Proofs of delivery'], ['ageing', 'Proof ageing'], ['shortages', 'Shortages'], ['damages', 'Damage'], ['failed', 'Failed deliveries'],
  ['exceptions', 'Exceptions'], ['compliance', 'Proof compliance'], ['performance', 'Delivery performance'],
] as const

const rate = (v: number | null) => (v == null ? 'Not applicable' : `${Math.round(v * 1000) / 10}%`)

export function DeliveryReportsPage() {
  const { message } = App.useApp()
  const [report, setReport] = useState<string>('deliveries')
  const [range, setRange] = useState<[string, string] | null>(null)
  const [customer, setCustomer] = useState('')
  const [groupBy, setGroupBy] = useState<'transporter' | 'customer' | 'lane'>('transporter')
  const [vehicle, setVehicle] = useState('')
  const [lane, setLane] = useState('')
  const [serviceType, setServiceType] = useState<string>()
  const [busy, setBusy] = useState(false)
  const params = { from: range?.[0], to: range?.[1], customer: customer.trim() || undefined, vehicle: vehicle.trim() || undefined, lane: lane.trim() || undefined, serviceType, groupBy }
  const compliance = useQuery({ queryKey: queryKeys.deliveries.compliance(params), queryFn: () => deliveriesApi.compliance(params) })

  const download = async (format: 'csv' | 'xlsx') => {
    setBusy(true)
    try {
      await deliveriesApi.downloadReport(report, format, params)
    } catch (e) {
      void message.error(toApiError(e).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <PageHeader title="Delivery reports" description="Deliveries, proofs, ageing, shortages, damage, failures and exceptions, and how each transporter, customer and lane is doing." />
      <Card title="Download a report">
        <Flex gap={12} wrap align="center">
          <Select aria-label="Report" style={{ width: 240 }} value={report} onChange={setReport} options={REPORTS.map(([value, label]) => ({ value, label }))} />
          <DatePicker.RangePicker aria-label="Period" onChange={(v) => setRange(v?.[0] && v[1] ? [dayjs(v[0]).format('YYYY-MM-DD'), dayjs(v[1]).format('YYYY-MM-DD')] : null)} />
          <Input allowClear aria-label="Customer" style={{ width: 200 }} placeholder="Customer" value={customer} onChange={(e) => setCustomer(e.target.value)} />
          <Input allowClear aria-label="Vehicle" style={{ width: 150 }} placeholder="Vehicle" value={vehicle} onChange={(e) => setVehicle(e.target.value)} />
          <Input allowClear aria-label="Lane" style={{ width: 170 }} placeholder="Lane (from or to)" value={lane} onChange={(e) => setLane(e.target.value)} />
          <Select allowClear aria-label="Service type" style={{ width: 140 }} placeholder="Service type" value={serviceType} onChange={setServiceType} options={['FTL', 'PTL', 'Dedicated'].map((v) => ({ value: v, label: v }))} />
          <Button icon={<DownloadOutlined />} loading={busy} onClick={() => void download('csv')}>CSV</Button>
          <Button icon={<DownloadOutlined />} loading={busy} onClick={() => void download('xlsx')}>Excel</Button>
        </Flex>
      </Card>

      <Card title="Proof compliance" style={{ marginTop: 16 }} extra={<Segmented aria-label="Group by" value={groupBy} onChange={(v) => setGroupBy(v as typeof groupBy)} options={[{ value: 'transporter', label: 'Transporter' }, { value: 'customer', label: 'Customer' }, { value: 'lane', label: 'Lane' }]} />}>
        <Typography.Paragraph type="secondary">Only deliveries that were made need a proof; failed and refused ones are left out rather than counted as misses.</Typography.Paragraph>
        <Table
          size="small" rowKey="key" loading={compliance.isLoading} pagination={false} dataSource={compliance.data?.rows} scroll={{ x: 'max-content' }} locale={{ emptyText: 'No deliveries in this period' }}
          columns={[
            { title: groupBy[0]!.toUpperCase() + groupBy.slice(1), dataIndex: 'name' },
            { title: 'Delivered', key: 'd', align: 'right', render: (_, r) => r.metrics.delivered },
            { title: 'Proof received', key: 'r', align: 'right', render: (_, r) => r.metrics.podSubmitted },
            { title: 'Pending', key: 'p', align: 'right', render: (_, r) => r.metrics.podPending },
            { title: 'Rejected', key: 'x', align: 'right', render: (_, r) => r.metrics.podRejected },
            { title: 'Accepted', key: 'a', align: 'right', render: (_, r) => r.metrics.podAccepted },
            { title: 'Compliance', key: 'c', align: 'right', render: (_, r) => rate(r.metrics.submissionCompliance) },
            { title: 'On time', key: 'o', align: 'right', render: (_, r) => rate(r.metrics.onTimeRate) },
          ]}
        />
      </Card>
    </>
  )
}
