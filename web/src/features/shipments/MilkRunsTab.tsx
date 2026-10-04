import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, DatePicker, Drawer, Empty, Flex, Popconfirm, Switch, Table, Tag, Typography, type TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Can } from '@/features/auth/AuthContext'
import { milkRunsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CommitMilkRunResult, MilkRunDto, MilkRunPlan } from '@/lib/api/types'
import { MilkRunFormDrawer } from './MilkRunFormDrawer'
import { MilkRunPlanView } from './MilkRunPlanView'

function PlanDrawer({ milkRun, onClose }: { milkRun: MilkRunDto; onClose: () => void }) {
  const { message } = App.useApp()
  const [date, setDate] = useState<Dayjs>(dayjs())
  const [keep, setKeep] = useState(true)
  const [plan, setPlan] = useState<MilkRunPlan | null>(null)
  const [committed, setCommitted] = useState<CommitMilkRunResult | null>(null)
  const queryClient = useQueryClient()

  const preview = useMutation({
    mutationFn: () => milkRunsApi.preview(milkRun.id, date.format('YYYY-MM-DD'), keep),
    onSuccess: setPlan,
    onError: (e) => void message.error(toApiError(e).message),
  })

  const commit = useMutation({
    mutationFn: () => milkRunsApi.commit(milkRun.id, date.format('YYYY-MM-DD'), keep),
    onSuccess: async (result) => {
      setCommitted(result)
      setPlan(null)
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all })
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
      void message.success(`${result.shipments.length} draft shipment${result.shipments.length === 1 ? '' : 's'} created`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  return (
    <Drawer open onClose={onClose} size={900} destroyOnHidden title={`Plan a day: ${milkRun.name}`}>
      <Flex vertical gap={16}>
        <Flex gap={12} wrap align="center">
          <DatePicker aria-label="Day to plan" allowClear={false} format="ddd DD MMM YYYY" value={date} onChange={(d) => { if (d) setDate(d); setPlan(null); setCommitted(null) }} />
          <Flex gap={8} align="center">
            <Switch aria-label="Keep the planned order" checked={keep} onChange={(v) => { setKeep(v); setPlan(null) }} />
            <Typography.Text>Keep the planned stop order</Typography.Text>
          </Flex>
          <Button type="primary" loading={preview.isPending} onClick={() => { setCommitted(null); preview.mutate() }}>Recalculate for this day</Button>
          <Can permission="shipments.plan">
            <Popconfirm
              title="Create draft shipments?"
              description="One shipment is made for each trip, from the orders that are open now. The orders are then taken off the open list."
              okText="Create shipments"
              onConfirm={() => commit.mutate()}
              disabled={!plan || plan.trips.length === 0}
            >
              <Button loading={commit.isPending} disabled={!plan || plan.trips.length === 0}>Commit to shipments</Button>
            </Popconfirm>
          </Can>
        </Flex>
        <Typography.Text type="secondary">
          Open orders ready by this date and linked to this run's stops are collected or delivered. Stops with nothing to do are skipped, and the vehicle is chosen from the day's load. Nothing is saved.
        </Typography.Text>
        {committed && (
          <Alert
            type="success"
            showIcon
            title={`Created ${committed.shipments.length} draft shipment${committed.shipments.length === 1 ? '' : 's'} for ${committed.date}`}
            description={
              <Flex vertical gap={2}>
                {committed.shipments.map((s) => (
                  <span key={s.shipmentId}>
                    Trip {s.tripNumber}: <Link to={`/shipments/${s.shipmentId}`}>{s.shipmentNumber}</Link> · {s.orders} order{s.orders === 1 ? '' : 's'}{s.isCollectionRun ? ' · collection run' : ''}
                  </span>
                ))}
              </Flex>
            }
          />
        )}
        {plan ? <MilkRunPlanView plan={plan} /> : !committed && <Empty description="Choose a day and recalculate." />}
      </Flex>
    </Drawer>
  )
}

export function MilkRunsTab() {
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<MilkRunDto | null>(null)
  const [creating, setCreating] = useState(false)
  const [planning, setPlanning] = useState<MilkRunDto | null>(null)
  const runs = useQuery({ queryKey: queryKeys.milkRuns.list(page), queryFn: () => milkRunsApi.list({ page, pageSize: 20 }), placeholderData: (p) => p })

  const columns: TableColumnsType<MilkRunDto> = [
    { title: 'Milk run', key: 'm', render: (_, m) => <div><Typography.Text strong>{m.name}</Typography.Text> <Tag variant="filled">{m.code}</Tag>{!m.isActive && <Tag>Off</Tag>}<br /><Typography.Text type="secondary">from {m.depotName}</Typography.Text></div> },
    { title: 'Stops', key: 's', render: (_, m) => `${m.stops.length} (${m.stops.filter((s) => s.type === 'Pickup').length} pickup, ${m.stops.filter((s) => s.type === 'Delivery').length} delivery)` },
    { title: 'Runs', key: 'd', responsive: ['md'], render: (_, m) => m.days.map((d) => d.slice(0, 3)).join(' ') },
    { title: 'Departs', dataIndex: 'departureTime', responsive: ['lg'], render: (t: string) => t.slice(0, 5) },
    {
      title: '',
      key: 'a',
      align: 'right',
      render: (_, m) => (
        <Flex gap={4} justify="flex-end">
          <Can permission="shipments.plan"><Button size="small" disabled={!m.isActive} onClick={() => setPlanning(m)}>Plan a day</Button></Can>
          <Can permission="shipments.plan"><Button size="small" onClick={() => setEditing(m)}>Edit</Button></Can>
        </Flex>
      ),
    },
  ]

  return (
    <Flex vertical gap={16}>
      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Text type="secondary">Recurring routes with a depot, planned stops and limits. Each day is recalculated from the orders that actually exist.</Typography.Text>
        <Can permission="shipments.plan"><Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>New milk run</Button></Can>
      </Flex>
      {runs.isError && <Alert type="error" showIcon title={runs.error.message} />}
      <Table<MilkRunDto>
        rowKey="id"
        columns={columns}
        dataSource={runs.data?.items}
        loading={runs.isFetching}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'No milk runs yet. Add locations first, then create a route with its pickup and delivery points.' }}
        pagination={{ current: page, pageSize: 20, total: runs.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
      />
      <MilkRunFormDrawer open={creating || editing !== null} milkRun={editing} onClose={() => { setCreating(false); setEditing(null) }} />
      {planning && <PlanDrawer milkRun={planning} onClose={() => setPlanning(null)} />}
    </Flex>
  )
}
