import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Flex, Input, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { locationsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListLocationsParams, LocationDto, LocationType } from '@/lib/api/types'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { LocationFormDrawer, locationTypes } from './LocationFormDrawer'

export function LocationsPage() {
  const [search, setSearch] = useState('')
  const [type, setType] = useState<LocationType>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [editing, setEditing] = useState<LocationDto | null>(null)
  const [creating, setCreating] = useState(false)

  const debounced = useDebouncedValue(search.trim())
  const params: ListLocationsParams = { search: debounced || undefined, type, page, pageSize }
  const locations = useQuery({ queryKey: queryKeys.locations.list(params), queryFn: () => locationsApi.list(params), placeholderData: (p) => p })

  const columns: TableColumnsType<LocationDto> = [
    { title: 'Location', key: 'l', render: (_, l) => <div><Typography.Text strong>{l.name}</Typography.Text> <Tag variant="filled">{l.code}</Tag>{!l.isActive && <Tag>Inactive</Tag>}<br /><Typography.Text type="secondary">{l.line1}</Typography.Text></div> },
    { title: 'City', key: 'c', render: (_, l) => `${l.city}, ${l.state} ${l.pincode}` },
    { title: 'Type', dataIndex: 'type', responsive: ['md'] },
    { title: 'Coordinates', key: 'g', responsive: ['lg'], render: (_, l) => `${l.latitude.toFixed(4)}, ${l.longitude.toFixed(4)}` },
    { title: '', key: 'a', align: 'right', render: (_, l) => <Can permission="shipments.plan"><Button size="small" onClick={() => setEditing(l)}>Edit</Button></Can> },
  ]

  return (
    <>
      <PageHeader
        title="Locations"
        description="Plants, depots, warehouses, customers and suppliers, with the coordinates routing needs."
        actions={<Can permission="shipments.plan"><Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New location</Button></Can>}
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Input allowClear style={{ width: 280, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search code, name or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="All types" style={{ width: 160 }} options={locationTypes.map((t) => ({ value: t, label: t }))} value={type} onChange={(v) => { setType(v); setPage(1) }} />
        </Flex>
        {locations.isError && <Alert type="error" showIcon title={locations.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<LocationDto>
          rowKey="id"
          columns={columns}
          dataSource={locations.data?.items}
          loading={locations.isFetching}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: debounced || type ? 'No locations match these filters' : 'No locations yet. Add your plants, depots and customers.' }}
          pagination={{ current: page, pageSize, total: locations.data?.totalCount ?? 0, showSizeChanger: true, showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`, onChange: (p, size) => { setPage(p); setPageSize(size) } }}
        />
      </Card>
      <LocationFormDrawer open={creating || editing !== null} location={editing} onClose={() => { setCreating(false); setEditing(null) }} />
    </>
  )
}
