import { CalculatorOutlined } from '@ant-design/icons'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, DatePicker, Empty, Flex, Form, Input, InputNumber, Select, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { contractsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractType, QuoteDto, QuoteResultDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { INDIAN_STATES } from '@/lib/indiaStates'
import { ContractTypeTag } from './shared'

interface FormValues {
  date: Dayjs
  originState: string
  originCity?: string
  destinationState: string
  destinationCity?: string
  vehicleTypeId?: string
  type?: ContractType
  weightKg?: number
  volumeCbm?: number
  distanceKm?: number
  drops: number
}

function Breakdown({ quote }: { quote: QuoteDto }) {
  return (
    <div style={{ maxWidth: 640 }}>
      <Table
        size="small"
        pagination={false}
        rowKey="code"
        dataSource={quote.lines}
        columns={[
          { title: 'Charge', dataIndex: 'description' },
          { title: 'Amount', dataIndex: 'amount', align: 'right', width: 140, render: (a: number) => formatInrExact(a) },
        ]}
        summary={() => (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0}><Typography.Text strong>Total</Typography.Text></Table.Summary.Cell>
            <Table.Summary.Cell index={1} align="right"><Typography.Text strong>{formatInrExact(quote.total)}</Typography.Text></Table.Summary.Cell>
          </Table.Summary.Row>
        )}
      />
      {quote.notes.map((n) => <Typography.Paragraph key={n} type="secondary" style={{ margin: '6px 0 0' }}>• {n}</Typography.Paragraph>)}
    </div>
  )
}

interface Props {
  /** Price against this one contract (draft or not) instead of every contract in force. */
  contractId?: string
  contractType?: ContractType
}

/** Prices a shipment against the rate cards: cheapest first, with the maths shown. */
export function QuotePanel({ contractId, contractType }: Props) {
  const [form] = Form.useForm<FormValues>()
  const vehicleTypes = useQuery({ queryKey: queryKeys.contracts.vehicleTypes, queryFn: contractsApi.vehicleTypes })

  const quote = useMutation<QuoteResultDto, Error, FormValues>({
    mutationFn: (v) =>
      contractsApi.quote({
        date: v.date.format('YYYY-MM-DD'),
        origin: { state: v.originState, city: v.originCity?.trim() || undefined },
        destination: { state: v.destinationState, city: v.destinationCity?.trim() || undefined },
        vehicleTypeId: v.vehicleTypeId,
        type: v.type ?? contractType,
        weightKg: v.weightKg,
        volumeCbm: v.volumeCbm,
        distanceKm: v.distanceKm,
        drops: v.drops,
        contractId,
        preview: contractId !== undefined,
      }),
  })

  const states = INDIAN_STATES.map((s) => ({ value: s.toUpperCase(), label: s }))
  const showType = contractId === undefined

  return (
    <Flex vertical gap={16}>
      <Card title={contractId ? 'Test this contract’s rates' : 'Shipment'}>
        <Form<FormValues>
          form={form}
          layout="vertical"
          requiredMark={false}
          initialValues={{ date: dayjs(), drops: 1 }}
          onFinish={(v) => quote.mutate(v)}
        >
          <Flex gap={16} wrap>
            <Form.Item label="Pickup state" name="originState" rules={[{ required: true, message: 'Choose the state' }]} style={{ flex: '1 1 200px' }}><Select showSearch virtual={false} options={states} /></Form.Item>
            <Form.Item label="Pickup city" name="originCity" style={{ flex: '1 1 200px' }}><Input /></Form.Item>
            <Form.Item label="Delivery state" name="destinationState" rules={[{ required: true, message: 'Choose the state' }]} style={{ flex: '1 1 200px' }}><Select showSearch virtual={false} options={states} /></Form.Item>
            <Form.Item label="Delivery city" name="destinationCity" style={{ flex: '1 1 200px' }}><Input /></Form.Item>
          </Flex>
          <Flex gap={16} wrap>
            <Form.Item label="Shipment date" name="date" rules={[{ required: true }]} style={{ flex: '1 1 160px' }}><DatePicker format="D MMM YYYY" style={{ width: '100%' }} /></Form.Item>
            {(contractType !== 'Ptl') && (
              <Form.Item label="Vehicle type" name="vehicleTypeId" style={{ flex: '2 1 240px' }} extra="Needed for truck-load and dedicated rates.">
                <Select allowClear showSearch optionFilterProp="label" options={vehicleTypes.data?.map((t) => ({ value: t.id, label: t.name }))} />
              </Form.Item>
            )}
            {showType && (
              <Form.Item label="Contract type" name="type" style={{ flex: '1 1 160px' }}>
                <Select allowClear placeholder="Any" options={[{ value: 'Ftl', label: 'FTL' }, { value: 'Ptl', label: 'PTL' }]} />
              </Form.Item>
            )}
          </Flex>
          <Flex gap={16} wrap>
            <Form.Item label="Weight (kg)" name="weightKg" style={{ flex: '1 1 140px' }}><InputNumber min={0.01} precision={2} style={{ width: '100%' }} /></Form.Item>
            <Form.Item label="Volume (CBM)" name="volumeCbm" style={{ flex: '1 1 140px' }}><InputNumber min={0.01} precision={2} style={{ width: '100%' }} /></Form.Item>
            <Form.Item label="Distance (km)" name="distanceKm" style={{ flex: '1 1 140px' }}><InputNumber min={1} precision={0} style={{ width: '100%' }} /></Form.Item>
            <Form.Item label="Drop points" name="drops" style={{ flex: '1 1 120px' }}><InputNumber min={1} max={50} precision={0} style={{ width: '100%' }} /></Form.Item>
          </Flex>
          <Button type="primary" htmlType="submit" icon={<CalculatorOutlined />} loading={quote.isPending}>Get quotes</Button>
        </Form>
      </Card>

      {quote.isError && <Alert type="error" showIcon title={toApiError(quote.error).message} />}
      {quote.data && (
        <Card title={`Quotes for ${dayjs(quote.data.date).format('D MMM YYYY')}`} styles={{ body: { padding: 0 } }}>
          {quote.data.message && <Alert type={quote.data.quotes.length ? 'warning' : 'info'} showIcon title={quote.data.message} style={{ margin: 16 }} />}
          {quote.data.quotes.length === 0 ? (
            <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No quotes" style={{ padding: 24 }} />
          ) : (
            <Table<QuoteDto>
              rowKey="contractId"
              pagination={false}
              scroll={{ x: 'max-content' }}
              dataSource={quote.data.quotes}
              expandable={{ defaultExpandedRowKeys: quote.data.quotes.slice(0, 1).map((q) => q.contractId), expandedRowRender: (q) => <Breakdown quote={q} /> }}
              columns={[
                { title: 'Transporter', key: 't', render: (_, q, i) => <><Typography.Text strong>{q.transporterName}</Typography.Text> {i === 0 && quote.data!.quotes.length > 1 && <Tag color="green">Lowest</Tag>}<br /><Typography.Text type="secondary">{q.contractReference} · {q.lane}</Typography.Text></> },
                { title: 'Type', dataIndex: 'type', render: (t: ContractType) => <ContractTypeTag type={t} /> },
                { title: 'Chargeable weight', dataIndex: 'chargeableWeightKg', responsive: ['md'], render: (w: number | null) => (w === null ? '—' : `${w} kg`) },
                { title: 'Total', dataIndex: 'total', align: 'right', render: (t: number) => <Typography.Text strong>{formatInrExact(t)}</Typography.Text> },
              ]}
            />
          )}
        </Card>
      )}
    </Flex>
  )
}
