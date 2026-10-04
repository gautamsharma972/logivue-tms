import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Flex, Input, Modal, Radio, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { planningApi } from '@/lib/api/endpoints'
import type { ModeChoice, PlanAlternative, PlanOptions } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { modeLabel } from './shared'

interface Props {
  orderIds: string[]
  options: PlanOptions
  busy?: boolean
  onClose: () => void
  /** The planner's decision. Without it the dialog is read-only. */
  onDecide?: (choice: ModeChoice) => void
}

const optionKey = (a: PlanAlternative) => `${a.mode}-${a.vehicleTypeId ?? 'ptl'}`

/** What an option is made of: each charge on the quote, so the total can be checked line by line. */
function CostComponents({ option }: { option: PlanAlternative }) {
  if (option.lines.length === 0) return <Typography.Text type="secondary">No priced charges: {option.verdict}</Typography.Text>
  return (
    <Table
      size="small"
      pagination={false}
      rowKey="code"
      dataSource={option.lines}
      columns={[
        { title: 'Charge', dataIndex: 'description' },
        { title: 'Code', dataIndex: 'code', width: 140, render: (c: string) => <Typography.Text type="secondary">{c}</Typography.Text> },
        { title: 'Amount', dataIndex: 'amount', align: 'right', width: 140, render: (v: number) => formatInrExact(v) },
      ]}
      summary={() => (
        <Table.Summary.Row>
          <Table.Summary.Cell index={0} colSpan={2}><Typography.Text strong>Total</Typography.Text></Table.Summary.Cell>
          <Table.Summary.Cell index={2} align="right"><Typography.Text strong>{formatInrExact(option.total)}</Typography.Text></Table.Summary.Cell>
        </Table.Summary.Row>
      )}
    />
  )
}

/** Every way the selected orders could move, priced from contracts, with the reason for the recommendation. */
export function ComparisonModal({ orderIds, options, busy, onClose, onDecide }: Props) {
  const comparison = useQuery({ queryKey: ['planning', 'compare', orderIds, options], queryFn: () => planningApi.compare(orderIds, options) })
  const data = comparison.data
  const [overriding, setOverriding] = useState(false)
  const [picked, setPicked] = useState<string>()
  const [reason, setReason] = useState('')

  const priced = (data?.alternatives ?? []).filter((a) => a.total !== null)
  const chosen = priced.find((a) => optionKey(a) === picked)
  const recommendedMode = data?.recommendedMode ?? null

  return (
    <Modal open title="FTL / PTL comparison" footer={null} onCancel={onClose} width={860} destroyOnHidden>
      {comparison.isError && <Alert type="error" showIcon title={comparison.error.message} />}
      {data && (
        <>
          <Alert
            type={data.recommended ? 'success' : 'warning'}
            showIcon
            style={{ marginBottom: 16 }}
            title={data.recommended ? `Recommended: ${recommendedMode ? modeLabel[recommendedMode] : ''}` : 'No option available'}
            description={data.reason}
          />
          <Table<PlanAlternative>
            size="small"
            pagination={false}
            loading={comparison.isLoading}
            rowKey={optionKey}
            dataSource={data.alternatives}
            scroll={{ x: 'max-content' }}
            expandable={{ expandedRowRender: (a) => <CostComponents option={a} />, rowExpandable: (a) => a.lines.length > 0 }}
            columns={[
              { title: 'Option', key: 'o', render: (_, a) => <>{a.mode === 'Ptl' ? 'Part load' : a.vehicleTypeName}{a.chosen && <Tag color="green" style={{ marginInlineStart: 8 }}>Recommended</Tag>}</> },
              { title: 'Transporter', dataIndex: 'transporterName', render: (t: string | null) => t ?? '—' },
              { title: 'Total', dataIndex: 'total', align: 'right', render: (t: number | null) => formatInrExact(t) },
              { title: 'Why / why not', dataIndex: 'verdict', render: (v: string) => <Typography.Text type="secondary">{v}</Typography.Text> },
            ]}
          />
          {data.saving !== null && data.ftlTotal !== null && data.ptlTotal !== null && (
            <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>
              Best full truck {formatInrExact(data.ftlTotal)} vs best part load {formatInrExact(data.ptlTotal)}: a difference of {formatInrExact(data.saving)}.
              Transit time and SLA are not compared yet (no lane transit-time data).
            </Typography.Paragraph>
          )}

          {onDecide && (
            <>
              {overriding && (
                <Flex vertical gap={8} style={{ marginTop: 12 }}>
                  <Typography.Text strong>Use a different option</Typography.Text>
                  <Radio.Group value={picked} onChange={(e) => setPicked(e.target.value as string)}>
                    <Flex vertical gap={4}>
                      {priced.filter((a) => !a.chosen).map((a) => (
                        <Radio key={optionKey(a)} value={optionKey(a)}>
                          {a.mode === 'Ptl' ? 'Part load' : a.vehicleTypeName} · {formatInrExact(a.total)}
                        </Radio>
                      ))}
                    </Flex>
                  </Radio.Group>
                  <Input.TextArea rows={2} maxLength={300} aria-label="Reason for overriding" placeholder="Why not follow the recommendation?" value={reason} onChange={(e) => setReason(e.target.value)} />
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    The plan will use {chosen ? (chosen.mode === 'Ptl' ? 'part load' : 'a full truck') : 'that mode'} only. The reason is kept with the plan.
                  </Typography.Text>
                </Flex>
              )}
              <Flex justify="flex-end" gap={8} style={{ marginTop: 16 }}>
                <Button onClick={onClose} disabled={busy}>Close</Button>
                {!overriding ? (
                  <>
                    <Button disabled={priced.length < 2 || busy} onClick={() => setOverriding(true)}>Override…</Button>
                    <Button type="primary" disabled={!recommendedMode || busy} loading={busy} onClick={() => recommendedMode && onDecide({ mode: recommendedMode, override: false })}>
                      Accept recommendation
                    </Button>
                  </>
                ) : (
                  <>
                    <Button onClick={() => setOverriding(false)} disabled={busy}>Back</Button>
                    <Button type="primary" danger disabled={!chosen || reason.trim().length === 0 || busy} loading={busy} onClick={() => chosen && onDecide({ mode: chosen.mode, override: true, reason: reason.trim() })}>
                      Override and plan
                    </Button>
                  </>
                )}
              </Flex>
            </>
          )}
        </>
      )}
    </Modal>
  )
}
