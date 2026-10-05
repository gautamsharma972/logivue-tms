import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, DatePicker, Flex, Input, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveriesApi, transportersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DeliveryStatus, DeliverySummaryDto, ListDeliveriesParams, ProofStatus } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { NewDeliveryDrawer } from './NewDeliveryDrawer'
import { DeliveryStatusTag, ProofStatusTag, deliveryStatusOptions, proofStatusOptions } from './shared'

export function DeliveriesListPage() {
  const navigate = useNavigate()
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const [search, setSearch] = useState('')
  const [customer, setCustomer] = useState('')
  const [status, setStatus] = useState<DeliveryStatus>()
  const [podStatus, setPodStatus] = useState<ProofStatus>()
  const [range, setRange] = useState<[string, string] | null>(null)
  const [hasException, setHasException] = useState(false)
  const [hasDiscrepancy, setHasDiscrepancy] = useState(false)
  const [vehicle, setVehicle] = useState('')
  const [lane, setLane] = useState('')
  const [serviceType, setServiceType] = useState<string>()
  const [transporterId, setTransporterId] = useState<string>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [creating, setCreating] = useState(false)

  const debounced = useDebouncedValue(search.trim())
  const debouncedCustomer = useDebouncedValue(customer.trim())
  const debouncedVehicle = useDebouncedValue(vehicle.trim())
  const debouncedLane = useDebouncedValue(lane.trim())
  const transporters = useQuery({ queryKey: queryKeys.transporters.list({ page: 1, pageSize: 100 }), queryFn: () => transportersApi.list({ page: 1, pageSize: 100 }), enabled: !isVendor && can('transporters.read') })
  const params: ListDeliveriesParams = {
    search: debounced || undefined, customer: debouncedCustomer || undefined, status, podStatus, from: range?.[0], to: range?.[1],
    hasException: hasException || undefined, hasDiscrepancy: hasDiscrepancy || undefined, vehicle: debouncedVehicle || undefined, lane: debouncedLane || undefined, serviceType,
    transporterId: isVendor ? undefined : transporterId, page, pageSize,
  }
  const deliveries = useQuery({ queryKey: queryKeys.deliveries.list(params), queryFn: () => deliveriesApi.list(params), placeholderData: (p) => p })
  const reset = () => setPage(1)

  const columns: TableColumnsType<DeliverySummaryDto> = [
    { title: 'Delivery', key: 'n', render: (_, d) => <div><Typography.Text strong>{d.number}</Typography.Text><br /><Typography.Text type="secondary">{d.shipmentReference ?? '—'}</Typography.Text></div> },
    { title: 'Customer', key: 'c', render: (_, d) => <div>{d.customerName}<br /><Typography.Text type="secondary">{d.destinationReference ?? ''}</Typography.Text></div> },
    ...(isVendor ? [] : [{ title: 'Transporter', dataIndex: 'transporterReference', responsive: ['lg' as const], render: (v: string | null) => v ?? '—' }]),
    { title: 'Vehicle', dataIndex: 'vehicleReference', responsive: ['lg'], render: (v: string | null) => v ?? '—' },
    { title: 'Planned', dataIndex: 'plannedDeliveryAt', responsive: ['md'], render: formatDateTime },
    { title: 'Delivered', dataIndex: 'actualDeliveryAt', responsive: ['xl'], render: formatDateTime },
    { title: 'Status', dataIndex: 'status', render: (s: DeliveryStatus) => <DeliveryStatusTag status={s} /> },
    { title: 'Proof', dataIndex: 'podStatus', render: (s: ProofStatus) => <ProofStatusTag status={s} /> },
    {
      title: 'Issues', key: 'i',
      render: (_, d) => (
        <Flex gap={4} wrap>
          {d.hasDiscrepancy && <Tag color="orange">Discrepancy</Tag>}
          {d.openExceptions > 0 && <Tag color="red">{d.openExceptions} open</Tag>}
        </Flex>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={isVendor ? 'My deliveries' : 'Deliveries'}
        description="Each drop of a shipment: where it is, what was delivered and whether its proof has been accepted."
        actions={!isVendor && can('deliveries.manage') ? <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New delivery</Button> : undefined}
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap align="center" style={{ padding: 16 }}>
          <Input allowClear style={{ width: 260, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search number, shipment, vehicle" value={search} onChange={(e) => { setSearch(e.target.value); reset() }} />
          <Input allowClear style={{ width: 200 }} placeholder="Customer" value={customer} onChange={(e) => { setCustomer(e.target.value); reset() }} />
          {!isVendor && can('transporters.read') && (
            <Select allowClear showSearch optionFilterProp="label" placeholder="Transporter" style={{ width: 210 }} value={transporterId} onChange={(v) => { setTransporterId(v); reset() }}
              options={(transporters.data?.items ?? []).map((t) => ({ value: t.id, label: t.legalName }))} />
          )}
          <Input allowClear style={{ width: 150 }} placeholder="Vehicle" value={vehicle} onChange={(e) => { setVehicle(e.target.value); reset() }} />
          <Input allowClear style={{ width: 170 }} placeholder="Lane (from or to)" value={lane} onChange={(e) => { setLane(e.target.value); reset() }} />
          <Select allowClear placeholder="Service" style={{ width: 130 }} options={['FTL', 'PTL', 'Dedicated'].map((v) => ({ value: v, label: v }))} value={serviceType} onChange={(v) => { setServiceType(v); reset() }} />
          <Select allowClear placeholder="Delivery status" style={{ width: 180 }} options={deliveryStatusOptions} value={status} onChange={(v) => { setStatus(v); reset() }} />
          <Select allowClear placeholder="Proof status" style={{ width: 190 }} options={proofStatusOptions} value={podStatus} onChange={(v) => { setPodStatus(v); reset() }} />
          <DatePicker.RangePicker onChange={(v) => { setRange(v?.[0] && v[1] ? [dayjs(v[0]).format('YYYY-MM-DD'), dayjs(v[1]).format('YYYY-MM-DD')] : null); reset() }} />
          <Checkbox checked={hasException} onChange={(e) => { setHasException(e.target.checked); reset() }}>Has open exception</Checkbox>
          <Checkbox checked={hasDiscrepancy} onChange={(e) => { setHasDiscrepancy(e.target.checked); reset() }}>Has discrepancy</Checkbox>
        </Flex>
        {deliveries.isError && <Alert type="error" showIcon title={deliveries.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<DeliverySummaryDto>
          rowKey="id"
          columns={columns}
          dataSource={deliveries.data?.items}
          loading={deliveries.isFetching}
          scroll={{ x: 'max-content' }}
          onRow={(d) => ({ onClick: () => navigate(`/delivery/${d.id}`), style: { cursor: 'pointer' } })}
          locale={{ emptyText: 'No deliveries match these filters' }}
          pagination={{ current: page, pageSize, total: deliveries.data?.totalCount ?? 0, showSizeChanger: true, showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`, onChange: (p, size) => { setPage(p); setPageSize(size) } }}
        />
      </Card>
      <NewDeliveryDrawer open={creating} onClose={() => setCreating(false)} onCreated={(id) => navigate(`/delivery/${id}`)} />
    </>
  )
}
