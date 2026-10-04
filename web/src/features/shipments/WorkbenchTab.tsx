import { PlayCircleOutlined, SwapOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Flex, Row, Table, Typography, type TableColumnsType } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ordersApi, planningApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ModeChoice, OrderDto, PlanOptions } from '@/lib/api/types'
import { ComparisonModal } from './ComparisonModal'
import { PlanOptionsForm } from './PlanOptionsForm'
import { defaultPlanOptions, formatKg } from './shared'

/** Pick open orders, set the rules, run the planner. The result opens as a plan to review. */
export function WorkbenchTab() {
  const { message } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [selected, setSelected] = useState<string[]>([])
  const [date, setDate] = useState<Dayjs>(dayjs())
  const [options, setOptions] = useState<PlanOptions>(defaultPlanOptions)
  const [comparing, setComparing] = useState(false)

  const orders = useQuery({ queryKey: queryKeys.planning.openOrders, queryFn: () => ordersApi.list({ status: 'Open', pageSize: 200 }) })

  const run = useMutation({
    mutationFn: (choice?: ModeChoice) => planningApi.createRun(date.format('YYYY-MM-DD'), selected, options, true, choice),
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.planning.all })
      navigate(`/planning/runs/${created.id}`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const columns: TableColumnsType<OrderDto> = [
    { title: 'Order', dataIndex: 'number', render: (n: string, o) => <><Typography.Text strong>{n}</Typography.Text>{o.reference && <Typography.Text type="secondary"> · {o.reference}</Typography.Text>}</> },
    { title: 'From', key: 'from', render: (_, o) => `${o.pickup.city}, ${o.pickup.state}` },
    { title: 'To', key: 'to', render: (_, o) => `${o.drop.city}, ${o.drop.state}` },
    { title: 'Weight', dataIndex: 'weightKg', align: 'right', render: formatKg },
    { title: 'Volume', dataIndex: 'volumeCbm', align: 'right', responsive: ['lg'], render: (v: number | null) => (v === null ? '—' : `${v} CBM`) },
    { title: 'Ready', dataIndex: 'readyDate', responsive: ['md'] },
    { title: 'Deliver by', dataIndex: 'deliverByDate', responsive: ['lg'], render: (d: string | null) => d ?? '—' },
  ]

  const chosen = orders.data?.items.filter((o) => selected.includes(o.id)) ?? []
  const totalWeight = chosen.reduce((sum, o) => sum + o.weightKg, 0)
  const samePickup = new Set(chosen.map((o) => `${o.pickup.state}|${o.pickup.city}`)).size <= 1

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={16}>
        <Card title="Orders to plan" extra={<Typography.Text type="secondary">{selected.length} selected · {formatKg(totalWeight)}</Typography.Text>} styles={{ body: { padding: 0 } }}>
          {orders.isError && <Alert type="error" showIcon title={orders.error.message} style={{ margin: 16 }} />}
          <Table<OrderDto>
            rowKey="id"
            size="small"
            columns={columns}
            dataSource={orders.data?.items}
            loading={orders.isLoading}
            scroll={{ x: 'max-content' }}
            pagination={{ pageSize: 15, showSizeChanger: false }}
            rowSelection={{ selectedRowKeys: selected, onChange: (keys) => setSelected(keys as string[]) }}
            locale={{ emptyText: 'No open orders. Enter orders first.' }}
          />
        </Card>
      </Col>
      <Col xs={24} xl={8}>
        <Card title="Planning rules">
          <Flex vertical gap={16}>
            <div>
              <Typography.Text type="secondary">Planning date</Typography.Text>
              <DatePicker aria-label="Planning date" style={{ width: '100%' }} format="DD MMM YYYY" allowClear={false} value={date} onChange={(d) => d && setDate(d)} />
            </div>
            <PlanOptionsForm value={options} onChange={setOptions} />
            <Flex gap={8} wrap>
              <Button type="primary" icon={<PlayCircleOutlined />} disabled={selected.length === 0} loading={run.isPending} onClick={() => run.mutate()}>
                Run planning
              </Button>
              <Button icon={<SwapOutlined />} disabled={chosen.length === 0 || !samePickup} onClick={() => setComparing(true)}>
                Compare FTL / PTL
              </Button>
            </Flex>
            {chosen.length > 0 && !samePickup && <Alert type="info" showIcon title="Compare needs orders from a single pickup." />}
          </Flex>
        </Card>
      </Col>
      {comparing && (
        <ComparisonModal
          orderIds={selected}
          options={options}
          busy={run.isPending}
          onClose={() => setComparing(false)}
          onDecide={(choice) => run.mutate(choice, { onSettled: () => setComparing(false) })}
        />
      )}
    </Row>
  )
}
