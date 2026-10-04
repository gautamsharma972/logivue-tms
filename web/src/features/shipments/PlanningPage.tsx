import { ThunderboltOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Empty, Flex, Form, Input, InputNumber, Row, Select, Statistic, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { planningApi, shipmentsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AdviceDto, AdviceRequest, SuggestedLoadDto, UtilizationDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { INDIAN_STATES } from '@/lib/indiaStates'
import { CompatibilityTab } from './CompatibilityTab'
import { KpiTab } from './KpiTab'
import { MilkRunsTab } from './MilkRunsTab'
import { PlansTab } from './PlansTab'
import { WorkbenchTab } from './WorkbenchTab'
import { UtilizationBar, formatKg, modeLabel, percent } from './shared'

const today = () => dayjs().format('YYYY-MM-DD')

function SuggestedLoads() {
  const { message } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const suggestions = useQuery({ queryKey: queryKeys.shipments.suggestions, queryFn: () => planningApi.suggestions() })

  const create = useMutation({
    mutationFn: (load: SuggestedLoadDto) =>
      shipmentsApi.create({
        orderIds: load.orderIds,
        mode: load.suggested,
        vehicleTypeId: load.suggested === 'Ftl' ? (load.vehicle?.vehicleTypeId ?? null) : null,
        plannedPickupDate: load.earliestReady < today() ? today() : load.earliestReady,
        distanceKm: null,
      }),
    onSuccess: async (shipment) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all })
      await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
      void message.success(`Shipment ${shipment.summary.number} drafted`)
      navigate(`/shipments/${shipment.summary.id}`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  if (suggestions.isError) return <Alert type="error" showIcon title={suggestions.error.message} />
  if (!suggestions.isLoading && suggestions.data?.length === 0) {
    return <Empty description="No open orders to plan. New orders appear here as soon as they are entered." />
  }

  return (
    <Flex vertical gap={12}>
      {suggestions.data?.map((load) => (
        <Card key={load.orderIds.join('-')} loading={suggestions.isLoading} size="small">
          <Flex justify="space-between" align="flex-start" wrap gap={12}>
            <div>
              <Typography.Text strong>{load.pickupCity}, {load.pickupState}</Typography.Text>
              <Typography.Text type="secondary"> → {load.drops.join(' · ')}</Typography.Text>
              <div style={{ marginTop: 4 }}>
                {load.orderNumbers.map((n) => <Tag key={n}>{n}</Tag>)}
              </div>
              <Typography.Text type="secondary">
                {formatKg(load.totalWeightKg)}
                {load.totalVolumeCbm !== null && ` · ${load.totalVolumeCbm} CBM`} · ready {load.earliestReady}
                {load.earliestDeadline && ` · deliver by ${load.earliestDeadline}`}
              </Typography.Text>
            </div>
            <Flex vertical align="flex-end" gap={4}>
              <Flex gap={8} align="center">
                <Tag color={load.suggested === 'Ftl' ? 'blue' : 'purple'}>{modeLabel[load.suggested]}</Tag>
                {load.vehicle && <Typography.Text>{load.vehicle.name} · {percent(load.utilization)} full</Typography.Text>}
              </Flex>
              <Button type="primary" icon={<ThunderboltOutlined />} loading={create.isPending && create.variables === load} onClick={() => create.mutate(load)}>
                Create shipment
              </Button>
            </Flex>
          </Flex>
          {load.warning && <Alert type="warning" showIcon title={load.warning} style={{ marginTop: 12 }} />}
          {load.backhaulOrderIds.length > 0 && (
            <Alert type="info" showIcon style={{ marginTop: 12 }} title={`${load.backhaulOrderIds.length} return order(s) could use this truck on its way back.`} />
          )}
        </Card>
      ))}
    </Flex>
  )
}

interface AdviceForm {
  weightKg: number
  volumeCbm?: number | null
  originState: string
  originCity?: string
  destinationState: string
  destinationCity?: string
  date?: Dayjs | null
}

function Advisor() {
  const [form] = Form.useForm<AdviceForm>()
  const { message } = App.useApp()
  const advise = useMutation({
    mutationFn: (v: AdviceForm) => {
      const body: AdviceRequest = {
        weightKg: v.weightKg,
        volumeCbm: v.volumeCbm ?? null,
        originState: v.originState,
        originCity: v.originCity?.trim() || null,
        destinationState: v.destinationState,
        destinationCity: v.destinationCity?.trim() || null,
        date: v.date ? v.date.format('YYYY-MM-DD') : null,
        distanceKm: null,
      }
      return planningApi.advice(body)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const stateOptions = INDIAN_STATES.map((s) => ({ value: s, label: s }))
  const advice: AdviceDto | undefined = advise.data

  return (
    <Row gutter={[24, 24]}>
      <Col xs={24} lg={10}>
        <Card title="Which vehicle, and full or part load?">
          <Form<AdviceForm> form={form} layout="vertical" onFinish={(v) => advise.mutate(v)}>
            <Flex gap={12}>
              <Form.Item name="weightKg" label="Weight (kg)" rules={[{ required: true, message: 'Enter the weight' }]} style={{ flex: 1 }}>
                <InputNumber min={1} precision={0} style={{ width: '100%' }} controls={false} />
              </Form.Item>
              <Form.Item name="volumeCbm" label="Volume (CBM)" style={{ flex: 1 }}>
                <InputNumber min={0} precision={2} style={{ width: '100%' }} controls={false} />
              </Form.Item>
            </Flex>
            <Flex gap={12}>
              <Form.Item name="originState" label="Pickup state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1 }}>
                <Select aria-label="Pickup state" showSearch virtual={false} options={stateOptions} />
              </Form.Item>
              <Form.Item name="originCity" label="Pickup city" style={{ flex: 1 }}><Input maxLength={100} /></Form.Item>
            </Flex>
            <Flex gap={12}>
              <Form.Item name="destinationState" label="Delivery state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1 }}>
                <Select aria-label="Delivery state" showSearch virtual={false} options={stateOptions} />
              </Form.Item>
              <Form.Item name="destinationCity" label="Delivery city" style={{ flex: 1 }}><Input maxLength={100} /></Form.Item>
            </Flex>
            <Form.Item name="date" label="Pickup date">
              <DatePicker style={{ width: '100%' }} format="DD MMM YYYY" />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={advise.isPending}>Get advice</Button>
          </Form>
        </Card>
      </Col>
      <Col xs={24} lg={14}>
        {advice ? (
          <Flex vertical gap={16}>
            <Alert type={advice.recommendedMode ? 'success' : 'warning'} showIcon title={advice.recommendedMode ? `Recommended: ${modeLabel[advice.recommendedMode]}` : 'No recommendation'} description={advice.modeReason} />
            <Table
              size="small"
              rowKey="mode"
              pagination={false}
              dataSource={advice.modes}
              columns={[
                { title: 'Mode', dataIndex: 'mode', render: (m: 'Ftl' | 'Ptl') => modeLabel[m] },
                { title: 'Best price', dataIndex: 'total', align: 'right', render: (t: number | null) => formatInrExact(t) },
                { title: 'Transporter', dataIndex: 'transporter', render: (t: string | null) => t ?? '—' },
                { title: 'Detail', dataIndex: 'detail' },
              ]}
            />
            {advice.sizingWarning && <Alert type="warning" showIcon title={advice.sizingWarning} />}
            {advice.fitting.length > 0 && (
              <Table
                size="small"
                rowKey="vehicleTypeId"
                pagination={false}
                dataSource={advice.fitting}
                title={() => <Typography.Text strong>Vehicles that can carry it</Typography.Text>}
                columns={[
                  { title: 'Vehicle', dataIndex: 'name', render: (n: string, v) => <>{n} {advice.recommended?.vehicleTypeId === v.vehicleTypeId && <Tag color="blue">Best fit</Tag>}</> },
                  { title: 'Payload', dataIndex: 'payloadKg', align: 'right', render: formatKg },
                  { title: 'Full', dataIndex: 'weightUtilization', render: (u: number) => <UtilizationBar value={u} /> },
                ]}
              />
            )}
          </Flex>
        ) : (
          <Empty description="Enter a load to see which truck fits and whether full or part load is cheaper." />
        )}
      </Col>
    </Row>
  )
}

function Utilization() {
  const report = useQuery({ queryKey: queryKeys.shipments.utilization, queryFn: () => planningApi.utilization({}) })
  const data: UtilizationDto | undefined = report.data
  return (
    <Flex vertical gap={16}>
      <Row gutter={16}>
        <Col xs={12} md={6}><Card><Statistic title="Shipments (last 30 days)" value={data?.shipments} loading={report.isLoading} /></Card></Col>
        <Col xs={12} md={6}><Card><Statistic title="Average fill" value={data?.averageUtilization === null || data === undefined ? '—' : percent(data.averageUtilization)} loading={report.isLoading} /></Card></Col>
        <Col xs={12} md={6}><Card><Statistic title="Under half full" value={data?.underUtilised} loading={report.isLoading} styles={{ content: { color: data && data.underUtilised > 0 ? '#cf1322' : undefined } }} /></Card></Col>
      </Row>
      <Table
        rowKey="shipmentId"
        loading={report.isFetching}
        dataSource={data?.rows}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'No dispatched shipments in this period' }}
        columns={[
          { title: 'Shipment', dataIndex: 'number' },
          { title: 'Pickup', dataIndex: 'plannedPickupDate' },
          { title: 'Transporter', dataIndex: 'transporterName' },
          { title: 'Vehicle', dataIndex: 'vehicleRegistration', render: (v: string | null) => v ?? '—' },
          { title: 'Load', dataIndex: 'loadKg', align: 'right', render: formatKg },
          { title: 'Capacity', dataIndex: 'payloadKg', align: 'right', render: (v: number | null) => (v === null ? '—' : formatKg(v)) },
          { title: 'Fill', dataIndex: 'utilization', render: (u: number | null) => <UtilizationBar value={u} /> },
        ]}
      />
    </Flex>
  )
}

export function PlanningPage() {
  return (
    <>
      <PageHeader title="Planning" description="Turn open orders into loads, and pick the right truck and mode before you ask a transporter." />
      <Tabs
        items={[
          { key: 'workbench', label: 'Workbench', children: <WorkbenchTab /> },
          { key: 'plans', label: 'Plans', children: <PlansTab /> },
          { key: 'milk-runs', label: 'Milk runs', children: <MilkRunsTab /> },
          { key: 'rules', label: 'Rules', children: <CompatibilityTab /> },
          { key: 'kpis', label: 'KPIs', children: <KpiTab /> },
          { key: 'loads', label: 'Suggested loads', children: <SuggestedLoads /> },
          { key: 'advisor', label: 'Load advisor', children: <Advisor /> },
          { key: 'utilization', label: 'Truck utilisation', children: <Utilization /> },
        ]}
      />
    </>
  )
}
