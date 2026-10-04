import { DownloadOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Empty, Flex, Row, Skeleton, Statistic, Table, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { Suspense, lazy, useState } from 'react'
import { planningApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { formatInr, formatInrExact } from '@/lib/format'
import { percent } from './shared'

const DailyChart = lazy(() => import('./KpiCharts').then((m) => ({ default: m.DailyChart })))
const ReasonsChart = lazy(() => import('./KpiCharts').then((m) => ({ default: m.ReasonsChart })))

const fmt = 'YYYY-MM-DD'

export function KpiTab() {
  const { message } = App.useApp()
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().subtract(30, 'day'), dayjs()])
  const from = range[0].format(fmt)
  const to = range[1].format(fmt)
  const dashboard = useQuery({ queryKey: queryKeys.planning.dashboard(from, to), queryFn: () => planningApi.dashboard(from, to) })
  const k = dashboard.data?.kpis

  const download = (format: 'csv' | 'xlsx' | 'pdf') => planningApi.exportDashboard(format, from, to).catch((e: unknown) => void message.error(toApiError(e).message))

  const stat = (title: string, value: string | number | null | undefined, suffix?: string) => (
    <Col xs={12} md={8} xl={6}>
      <Card size="small">
        <Statistic title={title} value={value ?? '—'} loading={dashboard.isLoading} suffix={suffix} />
      </Card>
    </Col>
  )

  return (
    <Flex vertical gap={16}>
      <Flex gap={12} wrap justify="space-between" align="center">
        <DatePicker.RangePicker
          aria-label="Period"
          allowClear={false}
          value={range}
          presets={[
            { label: 'Last 7 days', value: [dayjs().subtract(7, 'day'), dayjs()] },
            { label: 'Last 30 days', value: [dayjs().subtract(30, 'day'), dayjs()] },
            { label: 'Last 90 days', value: [dayjs().subtract(90, 'day'), dayjs()] },
            { label: 'This month', value: [dayjs().startOf('month'), dayjs()] },
          ]}
          onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])}
        />
        <Flex gap={8}>
          <Button icon={<DownloadOutlined />} onClick={() => void download('csv')}>CSV</Button>
          <Button icon={<DownloadOutlined />} onClick={() => void download('xlsx')}>Excel</Button>
          <Button icon={<DownloadOutlined />} onClick={() => void download('pdf')}>PDF</Button>
        </Flex>
      </Flex>

      {dashboard.isError && <Alert type="error" showIcon title={dashboard.error.message} />}
      {dashboard.isLoading && <Skeleton active />}
      {k && k.plans === 0 && <Empty description="No plans in this period. Pick another range, or run planning from the workbench." />}

      {k && k.plans > 0 && (
        <>
          <Typography.Text type="secondary">
            {k.plans} plan{k.plans === 1 ? '' : 's'} (latest version of each, cancelled ones left out). Savings are measured against the alternative each plan shows: separate trips, separate return vehicles.
          </Typography.Text>
          <Row gutter={[12, 12]}>
            {stat('Total orders', k.ordersTotal)}
            {stat('Planned', k.ordersPlanned)}
            {stat('Unplanned', k.ordersUnplanned)}
            {stat('Vehicles used', k.vehiclesUsed)}
            {stat('Total freight', formatInrExact(k.totalFreightCost))}
            {stat('Savings', formatInr(k.totalSavings))}
            {stat('Weight fill', percent(k.averageWeightUtilisation))}
            {stat('Volume fill', percent(k.averageVolumeUtilisation))}
            {stat('Distance', k.totalDistanceKm.toLocaleString('en-IN'), 'km')}
            {stat('Cost per tonne-km', k.costPerTonneKm === null ? null : `₹${k.costPerTonneKm}`)}
            {stat('Empty km', k.totalEmptyKm === undefined ? null : k.totalEmptyKm.toLocaleString('en-IN'), k.emptyKmPercent == null ? 'km' : `km (${k.emptyKmPercent}%)`)}
            {stat('Cost per tonne', k.costPerTonne == null ? null : formatInrExact(k.costPerTonne))}
            {stat('Cost per shipment', k.costPerShipment == null ? null : formatInrExact(k.costPerShipment))}
            {stat('Stops per vehicle', k.averageStopsPerVehicle)}
            {stat('Full truck', `${k.ftlPercent}%`)}
            {stat('Part load', `${k.ptlPercent}%`)}
            {stat('Consolidated trips', `${k.consolidatedPercent}%`)}
            {stat('Return pickups', `${k.returnPickupPercent}%`)}
          </Row>

          <Row gutter={[16, 16]}>
            <Col xs={24} xl={14}>
              <Card title="Freight and truck fill by day" size="small">
                <Suspense fallback={<Skeleton active />}><DailyChart daily={k.daily} /></Suspense>
              </Card>
            </Col>
            <Col xs={24} xl={10}>
              <Card title="Why orders were not planned" size="small">
                {k.unplannedReasons.length === 0 ? <Empty description="Every order was planned" /> : <Suspense fallback={<Skeleton active />}><ReasonsChart reasons={k.unplannedReasons} /></Suspense>}
              </Card>
            </Col>
            <Col xs={24} lg={12}>
              <Card title="Spend by transporter" size="small">
                <Table size="small" pagination={false} rowKey="name" dataSource={k.transporters}
                  columns={[{ title: 'Transporter', dataIndex: 'name' }, { title: 'Vehicles', dataIndex: 'vehicles', align: 'right' }, { title: 'Freight', dataIndex: 'cost', align: 'right', render: formatInrExact }]} />
              </Card>
            </Col>
            <Col xs={24} lg={12}>
              <Card title="Vehicle mix" size="small">
                <Table size="small" pagination={false} rowKey="name" dataSource={k.vehicleTypes}
                  columns={[{ title: 'Vehicle', dataIndex: 'name' }, { title: 'Vehicles', dataIndex: 'vehicles', align: 'right' }]} />
              </Card>
            </Col>
          </Row>
        </>
      )}
    </Flex>
  )
}
