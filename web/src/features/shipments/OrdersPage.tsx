import { PlusOutlined, SearchOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, Modal, Select, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { ordersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListOrdersParams, OrderDto, OrderStatus } from '@/lib/api/types'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { OrderFormDrawer } from './OrderFormDrawer'
import { OrderStatusTag, formatKg } from './shared'

const statuses: OrderStatus[] = ['Open', 'Planned', 'Dispatched', 'Delivered', 'Cancelled']

export function OrdersPage() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<OrderStatus>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [editing, setEditing] = useState<OrderDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [cancelling, setCancelling] = useState<OrderDto | null>(null)
  const [reason, setReason] = useState('')

  const debounced = useDebouncedValue(search.trim())
  const params: ListOrdersParams = { search: debounced || undefined, status, page, pageSize }
  const orders = useQuery({ queryKey: queryKeys.orders.list(params), queryFn: () => ordersApi.list(params), placeholderData: (p) => p })

  const cancel = useMutation({
    mutationFn: ({ order, why }: { order: OrderDto; why: string }) => ordersApi.cancel(order.id, why),
    onSuccess: async (o) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.suggestions })
      void message.success(`Order ${o.number} cancelled`)
      setCancelling(null)
      setReason('')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const columns: TableColumnsType<OrderDto> = [
    {
      title: 'Order',
      key: 'order',
      render: (_, o) => (
        <div>
          <Typography.Text strong>{o.number}</Typography.Text>
          {o.direction === 'Reverse' && <Tag style={{ marginInlineStart: 8 }}>Return</Tag>}
          <br />
          <Typography.Text type="secondary">{o.reference ?? o.description}</Typography.Text>
        </div>
      ),
    },
    {
      title: 'Lane',
      key: 'lane',
      render: (_, o) => `${o.pickup.city}, ${o.pickup.state} → ${o.drop.city}, ${o.drop.state}`,
    },
    { title: 'Load', key: 'load', align: 'right', responsive: ['md'], render: (_, o) => formatKg(o.weightKg) },
    { title: 'Ready', dataIndex: 'readyDate', responsive: ['lg'] },
    {
      title: 'Shipment',
      key: 'shipment',
      responsive: ['md'],
      render: (_, o) => (o.shipmentId ? <Link to={`/shipments/${o.shipmentId}`}>{o.shipmentNumber}</Link> : '—'),
    },
    { title: 'Status', dataIndex: 'status', render: (s: OrderStatus) => <OrderStatusTag status={s} /> },
    {
      title: '',
      key: 'actions',
      align: 'right',
      render: (_, o) =>
        o.status === 'Open' && (
          <Can permission="shipments.plan">
            <Flex gap={4} justify="flex-end">
              <Button size="small" onClick={() => setEditing(o)}>Edit</Button>
              <Button size="small" danger onClick={() => setCancelling(o)}>Cancel</Button>
            </Flex>
          </Can>
        ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Orders"
        description="Goods waiting to move. Open orders are grouped into shipments on the planning board."
        actions={
          <Can permission="shipments.plan">
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New order</Button>
          </Can>
        }
      />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap style={{ padding: 16 }}>
          <Input allowClear style={{ width: 280, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search number, reference or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="All statuses" style={{ width: 170 }} options={statuses.map((s) => ({ value: s, label: s }))} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
        </Flex>
        {orders.isError && <Alert type="error" showIcon title={orders.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<OrderDto>
          rowKey="id"
          columns={columns}
          dataSource={orders.data?.items}
          loading={orders.isFetching}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: debounced || status ? 'No orders match these filters' : 'No orders yet' }}
          pagination={{
            current: page, pageSize, total: orders.data?.totalCount ?? 0, showSizeChanger: true,
            showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`,
            onChange: (p, size) => { setPage(p); setPageSize(size) },
          }}
        />
      </Card>
      <OrderFormDrawer open={creating || editing !== null} order={editing} onClose={() => { setCreating(false); setEditing(null) }} />
      <Modal
        open={cancelling !== null}
        title={cancelling ? `Cancel ${cancelling.number}?` : ''}
        okText="Cancel order"
        okButtonProps={{ danger: true, disabled: reason.trim().length === 0 }}
        cancelText="Keep it"
        confirmLoading={cancel.isPending}
        onOk={() => cancelling && cancel.mutate({ order: cancelling, why: reason.trim() })}
        onCancel={() => { setCancelling(null); setReason('') }}
        destroyOnHidden
      >
        <Input.TextArea rows={3} maxLength={500} placeholder="Why is this order being cancelled?" aria-label="Cancellation reason" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </>
  )
}
