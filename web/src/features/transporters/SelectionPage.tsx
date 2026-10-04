import { SearchOutlined } from '@ant-design/icons'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, DatePicker, Descriptions, Flex, Form, Input, InputNumber, Radio, Select, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { performanceApi, transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CandidateEvaluationDto, FreightMode, RankedCandidateDto, RecommendationResultDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { INDIAN_STATES } from '@/lib/indiaStates'

interface FormValues {
  originState: string
  originCity?: string
  destinationState: string
  destinationCity?: string
  mode: FreightMode
  vehicleTypeId?: string
  weightKg: number
  volumeCbm?: number | null
  date: dayjs.Dayjs
  requiredCapabilities?: string[]
  isUrgent?: boolean
}

function Ranked({ ranked }: { ranked: RankedCandidateDto[] }) {
  return (
    <Table<RankedCandidateDto>
      size="small"
      rowKey={(r) => r.candidate.transporterId}
      pagination={false}
      dataSource={ranked}
      expandable={{
        expandedRowRender: (r) => (
          <Flex vertical gap={12}>
            <Table
              size="small"
              rowKey="factor"
              pagination={false}
              dataSource={r.components}
              columns={[
                { title: 'Factor', dataIndex: 'factor' },
                { title: 'Basis', dataIndex: 'basis', render: (b: string, c) => (c.sufficient ? b : <Typography.Text type="secondary">{b}</Typography.Text>) },
                { title: 'Score', dataIndex: 'score', align: 'right' },
                { title: 'Weight', dataIndex: 'weight', align: 'right', render: (w: number) => `${w}%` },
                { title: 'Contribution', dataIndex: 'contribution', align: 'right' },
              ]}
            />
            {r.comparisons.length > 0 && (
              <div>
                <Typography.Text strong>Why not first</Typography.Text>
                <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>{r.comparisons.map((c) => <li key={c}>{c}</li>)}</ul>
              </div>
            )}
          </Flex>
        ),
      }}
      columns={[
        { title: '#', dataIndex: 'rank', width: 50 },
        { title: 'Transporter', key: 't', render: (_, r) => <div><Link to={`/transporters/${r.candidate.transporterId}`}>{r.candidate.name}</Link> <Tag variant="filled">{r.candidate.code}</Tag>{r.candidate.preferred && <Tag color="green">Preferred</Tag>}</div> },
        { title: 'Score', dataIndex: 'recommendationScore', align: 'right', render: (v: number) => <Typography.Text strong>{v}</Typography.Text> },
        { title: 'Price', key: 'p', align: 'right', render: (_, r) => (r.candidate.rate ? formatInrExact(r.candidate.rate.total) : '—') },
        { title: 'Vehicles free', key: 'v', align: 'right', render: (_, r) => r.candidate.availableVehicles },
        { title: 'Why', key: 'w', render: (_, r) => <ul style={{ margin: 0, paddingLeft: 16 }}>{r.explanations.map((e) => <li key={e}>{e}</li>)}</ul> },
      ]}
    />
  )
}

function Excluded({ candidates }: { candidates: CandidateEvaluationDto[] }) {
  return (
    <Table<CandidateEvaluationDto>
      size="small"
      rowKey="transporterId"
      pagination={{ pageSize: 10, hideOnSinglePage: true }}
      dataSource={candidates}
      columns={[
        { title: 'Transporter', key: 't', render: (_, c) => <div><Link to={`/transporters/${c.transporterId}`}>{c.name}</Link> <Tag variant="filled">{c.code}</Tag></div> },
        { title: 'Why not', key: 'r', render: (_, c) => <ul style={{ margin: 0, paddingLeft: 16 }}>{c.reasons.map((r) => <li key={r}>{r}</li>)}</ul> },
      ]}
    />
  )
}

/** Who can take a load, ranked with the reasoning, and who cannot and why. Nothing is booked. */
export function SelectionPage() {
  const { message } = App.useApp()
  const [form] = Form.useForm<FormValues>()
  const mode = Form.useWatch('mode', form)
  const types = useQuery({ queryKey: queryKeys.transporters.vehicleTypes, queryFn: transportersApi.vehicleTypes })
  const catalog = useQuery({ queryKey: queryKeys.performance.capabilityCatalog, queryFn: () => performanceApi.capabilityCatalog() })
  const find = useMutation({
    mutationFn: (v: FormValues): Promise<RecommendationResultDto> =>
      performanceApi.recommend({
        originState: v.originState, originCity: v.originCity?.trim() || null, destinationState: v.destinationState, destinationCity: v.destinationCity?.trim() || null, mode: v.mode,
        vehicleTypeId: v.mode === 'Ftl' ? (v.vehicleTypeId ?? null) : null, weightKg: v.weightKg, volumeCbm: v.volumeCbm ?? null, date: v.date.format('YYYY-MM-DD'),
        requiredCapabilities: v.requiredCapabilities ?? [], isUrgent: v.isUrgent ?? false, distanceKm: null,
      }),
    onError: (e) => void message.error(toApiError(e).message),
  })
  const states = INDIAN_STATES.map((s) => ({ value: s, label: s }))
  const result = find.data

  return (
    <>
      <PageHeader title="Find a transporter" description="Which transporters can take a load, which one to give it to, and why the others cannot. Prices come from your contracts; nothing is booked." />
      <Card style={{ marginBottom: 16 }}>
        <Form<FormValues> form={form} layout="vertical" requiredMark="optional" initialValues={{ mode: 'Ftl', date: dayjs(), isUrgent: false }} onFinish={(v) => find.mutate(v)}>
          <Flex gap={12} wrap>
            <Form.Item name="originState" label="From state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1, minWidth: 180 }}><Select aria-label="From state" showSearch virtual={false} options={states} /></Form.Item>
            <Form.Item name="originCity" label="From city" style={{ flex: 1, minWidth: 140 }}><Input aria-label="From city" maxLength={100} /></Form.Item>
            <Form.Item name="destinationState" label="To state" rules={[{ required: true, message: 'Choose a state' }]} style={{ flex: 1, minWidth: 180 }}><Select aria-label="To state" showSearch virtual={false} options={states} /></Form.Item>
            <Form.Item name="destinationCity" label="To city" style={{ flex: 1, minWidth: 140 }}><Input aria-label="To city" maxLength={100} /></Form.Item>
          </Flex>
          <Flex gap={12} wrap align="flex-start">
            <Form.Item name="mode" label="Service"><Radio.Group optionType="button" options={[{ value: 'Ftl', label: 'Full truck' }, { value: 'Ptl', label: 'Part load' }]} /></Form.Item>
            {mode === 'Ftl' && (
              <Form.Item name="vehicleTypeId" label="Vehicle type" rules={[{ required: true, message: 'Choose the vehicle type' }]} style={{ minWidth: 240 }}>
                <Select aria-label="Vehicle type" showSearch optionFilterProp="label" virtual={false} options={(types.data ?? []).filter((t) => t.isActive).map((t) => ({ value: t.id, label: t.name }))} />
              </Form.Item>
            )}
            <Form.Item name="weightKg" label="Weight (kg)" rules={[{ required: true, message: 'Enter the weight' }]}><InputNumber min={1} precision={0} controls={false} style={{ width: 140 }} /></Form.Item>
            <Form.Item name="volumeCbm" label="Volume (CBM)"><InputNumber min={0} precision={2} controls={false} style={{ width: 120 }} /></Form.Item>
            <Form.Item name="date" label="Pickup date" rules={[{ required: true, message: 'Choose a date' }]}><DatePicker allowClear={false} format="DD MMM YYYY" /></Form.Item>
          </Flex>
          <Flex gap={24} wrap align="center">
            <Form.Item name="requiredCapabilities" label="Must be able to carry"><Checkbox.Group options={(catalog.data ?? []).map((c) => ({ value: c.code, label: c.name }))} /></Form.Item>
            <Form.Item name="isUrgent" valuePropName="checked"><Checkbox>Urgent load</Checkbox></Form.Item>
          </Flex>
          <Button type="primary" htmlType="submit" icon={<SearchOutlined />} loading={find.isPending}>Find transporters</Button>
        </Form>
      </Card>

      {result && (
        <Flex vertical gap={16}>
          {result.recommended ? (
            <Alert
              type="success"
              showIcon
              title={`Recommended: ${result.recommended.candidate.name}`}
              description={
                <Descriptions size="small" column={{ xs: 1, md: 3 }} colon={false}>
                  <Descriptions.Item label="Score">{result.recommended.recommendationScore}</Descriptions.Item>
                  <Descriptions.Item label="Price">{result.recommended.candidate.rate ? formatInrExact(result.recommended.candidate.rate.total) : '—'}</Descriptions.Item>
                  <Descriptions.Item label="Vehicles free">{result.recommended.candidate.availableVehicles}</Descriptions.Item>
                </Descriptions>
              }
            />
          ) : (
            <Alert type="warning" showIcon title="No transporter can take this load" description="See below why each one cannot." />
          )}
          {result.ranked.length > 0 && <Card title={`Can take it (${result.ranked.length})`} styles={{ body: { padding: 0 } }}><Ranked ranked={result.ranked} /></Card>}
          {result.candidates.some((c) => !c.eligible) && (
            <Card title={`Cannot take it (${result.candidates.filter((c) => !c.eligible).length})`} styles={{ body: { padding: 0 } }}>
              <Excluded candidates={result.candidates.filter((c) => !c.eligible)} />
            </Card>
          )}
        </Flex>
      )}
    </>
  )
}
