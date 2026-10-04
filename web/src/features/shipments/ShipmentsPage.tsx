import { SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Flex, Input, Select, Table, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { shipmentsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListShipmentsParams, ShipmentStatus, ShipmentSummaryDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { ShipmentStatusTag, UtilizationBar, formatKg, modeLabel, shipmentStatusOptions } from './shared'

export function ShipmentsPage() {
  const navigate = useNavigate()
  const { user } = useAuth()
  const isVendor = user?.transporterId != null
  const [search, setSearch] = useState('')
  const [searchParams] = useSearchParams()
  const initialStatus = shipmentStatusOptions.find((o) => o.value === searchParams.get('status'))?.value
  const [status, setStatus] = useState<ShipmentStatus | undefined>(initialStatus)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)

  const debounced = useDebouncedValue(search.trim())
  const params: ListShipmentsParams = { search: debounced || undefined, status, page, pageSize }
  const shipments = useQuery({ queryKey: queryKeys.shipments.list(params), queryFn: () => shipmentsApi.list(params), placeholderData: (p) => p })

  const columns: TableColumnsType<ShipmentSummaryDto> = [
    {
      title: 'Shipment',
      key: 'shipment',
      render: (_, s) => (
        <div>
          <Typography.Text strong>{s.number}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{modeLabel[s.mode]} · {s.orderCount} order{s.orderCount === 1 ? '' : 's'}</Typography.Text>
        </div>
      ),
    },
    { title: 'Lane', dataIndex: 'lane' },
    { title: 'Pickup', dataIndex: 'plannedPickupDate', responsive: ['md'] },
    { title: 'Load', dataIndex: 'totalWeightKg', align: 'right', responsive: ['md'], render: formatKg },
    ...(isVendor ? [] : [{ title: 'Transporter', dataIndex: 'transporterName', responsive: ['lg' as const], render: (n: string | null) => n ?? '—' }]),
    { title: 'Vehicle', dataIndex: 'vehicleRegistration', responsive: ['lg'], render: (v: string | null) => v ?? '—' },
    { title: 'Fill', dataIndex: 'utilization', responsive: ['xl'], width: 140, render: (u: number | null) => <UtilizationBar value={u} /> },
    ...(isVendor ? [] : [{ title: 'Estimate', dataIndex: 'freightEstimate', align: 'right' as const, responsive: ['xl' as const], render: (v: number | null) => formatInrExact(v) }]),
    { title: 'Status', dataIndex: 'status', render: (s: ShipmentStatus) => <ShipmentStatusTag status={s} /> },
  ]

  return (
    <>
      <PageHeader
        title={isVendor ? 'Tenders' : 'Shipments'}
        description={isVendor ? 'Loads offered to your company. Accept with a vehicle and driver, or decline.' : 'Planned loads, from draft to delivery.'}
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Input allowClear style={{ width: 280, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search number, vehicle or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="All statuses" style={{ width: 200 }} options={shipmentStatusOptions.filter((o) => !isVendor || o.value !== 'Draft')} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
        </Flex>
        {shipments.isError && <Alert type="error" showIcon title={shipments.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<ShipmentSummaryDto>
          rowKey="id"
          columns={columns}
          dataSource={shipments.data?.items}
          loading={shipments.isFetching}
          scroll={{ x: 'max-content' }}
          onRow={(s) => ({ onClick: () => navigate(`/shipments/${s.id}`), style: { cursor: 'pointer' } })}
          locale={{ emptyText: debounced || status ? 'No shipments match these filters' : isVendor ? 'Nothing has been offered to you yet' : 'No shipments yet. Create one from the planning board.' }}
          pagination={{
            current: page, pageSize, total: shipments.data?.totalCount ?? 0, showSizeChanger: true,
            showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
            onChange: (p, size) => { setPage(p); setPageSize(size) },
          }}
        />
      </Card>
    </>
  )
}
