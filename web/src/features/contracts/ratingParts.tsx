import { CheckCircleFilled, CloseCircleFilled } from '@ant-design/icons'
import { Alert, Card, Descriptions, Flex, List, Table, Tag, Typography } from 'antd'
import type { RatingOptionDto, RatingResultDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'

const lineTone: Record<string, string> = { BASE_FREIGHT: 'blue', DPH: 'gold', DISCOUNT: 'green', ROUNDING: 'default' }

/** The freight, line by line. The total is exactly the sum of what is shown. */
export function RatingBreakdown({ option }: { option: RatingOptionDto }) {
  return (
    <Table
      size="small"
      pagination={false}
      rowKey="sequence"
      dataSource={option.lines}
      columns={[
        { title: 'Charge', render: (_, l) => <Flex vertical><span><Tag color={lineTone[l.type] ?? 'purple'}>{l.type.replaceAll('_', ' ')}</Tag></span><Typography.Text type="secondary" style={{ fontSize: 12 }}>{l.description}</Typography.Text></Flex> },
        { title: 'Amount', dataIndex: 'amount', align: 'right', width: 150, render: (a: number) => formatInrExact(a) },
      ]}
      summary={() => (
        <Table.Summary.Row>
          <Table.Summary.Cell index={0}><strong>Total freight</strong></Table.Summary.Cell>
          <Table.Summary.Cell index={1} align="right"><strong>{formatInrExact(option.totalFreight)}</strong></Table.Summary.Cell>
        </Table.Summary.Row>
      )}
    />
  )
}

/** Which contract and rate priced the shipment, and why: the qualification, line by line. */
export function WhyThisRate({ option, calculationVersion }: { option: RatingOptionDto; calculationVersion: string }) {
  return (
    <Descriptions size="small" column={1} bordered>
      <Descriptions.Item label="Contract">{option.contractReference} V{option.contractRevision} · {option.transporterName}</Descriptions.Item>
      <Descriptions.Item label="Rate">{option.rate.code} V{option.rate.version} · {option.rate.lane}{option.rate.priority !== 100 ? ` · preference ${option.rate.priority}` : ''}</Descriptions.Item>
      <Descriptions.Item label="DPH">{option.dphRule ? `${option.dphRule} V${option.dphVersion}` : 'None applied'}</Descriptions.Item>
      <Descriptions.Item label="Calculation version">{calculationVersion}</Descriptions.Item>
      {option.transitSlaMinutes != null && <Descriptions.Item label="Transit commitment">{Math.round(option.transitSlaMinutes / 60)} h</Descriptions.Item>}
      <Descriptions.Item label="Qualification">
        <Flex vertical gap={2}>
          {option.reasons.map((r) => <span key={r}>{r.startsWith('✓') ? <CheckCircleFilled style={{ color: '#389e0d', marginRight: 6 }} /> : null}{r.replace(/^✓\s*/, '')}</span>)}
        </Flex>
      </Descriptions.Item>
      {option.notes.length > 0 && <Descriptions.Item label="Notes">{option.notes.map((n) => <div key={n}>{n}</div>)}</Descriptions.Item>}
    </Descriptions>
  )
}

/** What was looked at and not used. Essential when someone expected a different answer. */
export function Exclusions({ result }: { result: RatingResultDto }) {
  if (result.exclusions.length === 0) return <Typography.Text type="secondary">Nothing else was considered.</Typography.Text>
  return (
    <List
      size="small"
      dataSource={result.exclusions}
      renderItem={(e) => (
        <List.Item>
          <Flex vertical>
            <span><strong>{e.rateReference ?? e.contractReference}</strong> <Tag>{e.reasonCode.replaceAll('_', ' ')}</Tag></span>
            <Typography.Text type="secondary">{e.reason}</Typography.Text>
          </Flex>
        </List.Item>
      )}
    />
  )
}

export function RatingTrace({ result }: { result: RatingResultDto }) {
  return (
    <List
      size="small"
      dataSource={result.trace}
      renderItem={(t) => (
        <List.Item>
          <Flex gap={8} align="start">
            {t.ok ? <CheckCircleFilled style={{ color: '#389e0d', marginTop: 4 }} /> : <CloseCircleFilled style={{ color: '#cf1322', marginTop: 4 }} />}
            <div><Tag>{t.stage}</Tag>{t.text}</div>
          </Flex>
        </List.Item>
      )}
    />
  )
}

/** A rated result in full: the answer, the lines, why, what was left out and the trace. Or why there is no answer. */
export function RatingResultView({ result }: { result: RatingResultDto }) {
  if (!result.qualified || !result.selected) {
    return (
      <Flex vertical gap={12}>
        <Alert type={result.errorCode === 'FREIGHT_RATE_CONFLICT' ? 'warning' : 'error'} showIcon title={result.errorCode?.replaceAll('_', ' ') ?? 'No rate'} description={result.message} />
        {result.advice.length > 0 && <Card size="small" title="Check">{result.advice.map((a) => <div key={a}>• {a}</div>)}</Card>}
        <Card size="small" title="What was considered"><Exclusions result={result} /></Card>
        <Card size="small" title="Trace"><RatingTrace result={result} /></Card>
      </Flex>
    )
  }

  const best = result.selected
  return (
    <Flex vertical gap={12}>
      <Card size="small" title={`${best.contractReference} V${best.contractRevision} · ${best.rate.code} V${best.rate.version}`} extra={<Typography.Title level={4} style={{ margin: 0 }}>{formatInrExact(best.totalFreight)}</Typography.Title>}>
        <RatingBreakdown option={best} />
      </Card>
      {result.options.length > 1 && (
        <Card size="small" title={`${result.options.length} transporters qualify`}>
          <Table size="small" pagination={false} rowKey={(o) => o.contractId} dataSource={result.options} columns={[
            { title: 'Transporter', dataIndex: 'transporterName' }, { title: 'Contract', render: (_, o) => `${o.contractReference} V${o.contractRevision}` }, { title: 'Rate', render: (_, o) => `${o.rate.code} V${o.rate.version}` },
            { title: 'Preference', render: (_, o) => o.rate.priority }, { title: 'Transit', render: (_, o) => (o.transitSlaMinutes == null ? '—' : `${Math.round(o.transitSlaMinutes / 60)} h`) },
            { title: 'Total', align: 'right', render: (_, o) => formatInrExact(o.totalFreight) },
          ]} />
        </Card>
      )}
      <Card size="small" title="Why this rate"><WhyThisRate option={best} calculationVersion={result.calculationVersion} /></Card>
      <Card size="small" title="Not used"><Exclusions result={result} /></Card>
      <Card size="small" title="Calculation trace"><RatingTrace result={result} /></Card>
    </Flex>
  )
}
