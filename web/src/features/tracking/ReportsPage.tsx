import { App, Button, Card, DatePicker, Flex, Space } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'

const REPORTS: [string, string, string][] = [
  ['shipments', 'Shipment tracking', 'Every trip with its status, risk, ETA and delay.'],
  ['vehicles', 'Vehicle tracking', 'Last position, health and distance per vehicle.'],
  ['deviations', 'Route deviations', 'Each time a vehicle left its route, how far and for how long.'],
  ['dwell', 'Standing time', 'Planned and unplanned stops, with the time over the expected.'],
  ['eta-accuracy', 'ETA accuracy', 'What was predicted against what happened.'],
  ['tracking-health', 'Tracking health', 'Gaps, stale and lost periods per trip.'],
  ['delays', 'Delay reasons', 'Delayed trips grouped by the reason given.'],
  ['exceptions', 'Exceptions', 'Raised, escalated and resolved, with age.'],
  ['planned-vs-actual', 'Planned against actual', 'Distance and time against the plan, for finished trips.'],
]

export function TrackingReportsPage() {
  const { message } = App.useApp()
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const params = { from: range?.[0].format('YYYY-MM-DD'), to: range?.[1].format('YYYY-MM-DD') }
  const run = (report: string, format: 'csv' | 'xlsx') => trackingApi.downloadReport(report, format, params).catch((e: unknown) => void message.error(toApiError(e).message))
  return (
    <>
      <PageHeader title="Tracking reports" description="Downloads as CSV or Excel. Times are in Indian time. Leave the dates empty for everything." />
      <DatePicker.RangePicker style={{ marginBottom: 16 }} onChange={(v) => setRange(v as [Dayjs, Dayjs] | null)} />
      <Flex gap={12} wrap>
        {REPORTS.map(([key, title, text]) => (
          <Card key={key} size="small" title={title} style={{ width: 300 }}>
            <p style={{ minHeight: 44 }}>{text}</p>
            <Space><Button onClick={() => void run(key, 'csv')}>CSV</Button><Button onClick={() => void run(key, 'xlsx')}>Excel</Button></Space>
          </Card>
        ))}
      </Flex>
    </>
  )
}
