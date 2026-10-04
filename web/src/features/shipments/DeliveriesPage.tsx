import { SearchOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Col, Flex, Input, Row, Select, Statistic, Switch, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveryApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ListPodParams, PodLineDto, PodStage } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { PodStageTag, podStageOptions } from './shared'

function AgeTag({ line }: { line: PodLineDto }) {
  if (line.ageDays === null) return <>—</>
  if (line.stage === 'ProofVerified') return <Typography.Text type="secondary">{line.ageDays} d</Typography.Text>
  return <Tag color={line.overdue ? 'red' : line.ageDays > 3 ? 'gold' : 'default'}>{line.ageDays === 0 ? 'today' : `${line.ageDays} d`}</Tag>
}

export function DeliveriesPage() {
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const [search, setSearch] = useState('')
  const [stage, setStage] = useState<PodStage>()
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)

  const debounced = useDebouncedValue(search.trim())
  const params: ListPodParams = { search: debounced || undefined, stage, overdueOnly: overdueOnly || undefined, page, pageSize }
  const queue = useQuery({ queryKey: queryKeys.pod.queue(params), queryFn: () => deliveryApi.queue(params), placeholderData: (p) => p })
  const ageing = useQuery({ queryKey: queryKeys.pod.ageing, queryFn: () => deliveryApi.ageing(), enabled: !isVendor && (can('shipments.read') || can('shipments.plan')) })

  const columns: TableColumnsType<PodLineDto> = [
    {
      title: 'Delivery',
      key: 'd',
      render: (_, l) => (
        <div>
          <Link to={`/shipments/${l.shipmentId}`}><Typography.Text strong>{l.shipmentNumber}</Typography.Text></Link> <Typography.Text type="secondary">· {l.orderNumber}{l.lrNumber ? ` · ${l.lrNumber}` : ''}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{l.consignee}, {l.consigneeCity}</Typography.Text>
        </div>
      ),
    },
    ...(isVendor ? [] : [{ title: 'Transporter', dataIndex: 'transporterName', responsive: ['lg' as const], render: (n: string | null) => n ?? '—' }]),
    { title: 'Delivered', key: 'at', responsive: ['md'], render: (_, l) => (l.deliveredAt ? `${formatDateTime(l.deliveredAt)}${l.receiverName ? ` · ${l.receiverName}` : ''}` : '—') },
    {
      title: 'Condition',
      key: 'c',
      responsive: ['md'],
      render: (_, l) => (l.hasException ? <Flex gap={4} wrap>{(l.shortagePackages ?? 0) > 0 && <Tag color="red">{l.shortagePackages} short</Tag>}{(l.damagedPackages ?? 0) > 0 && <Tag color="orange">{l.damagedPackages} damaged</Tag>}</Flex> : l.deliveredAt ? 'Complete' : '—'),
    },
    { title: 'Age', key: 'age', width: 90, render: (_, l) => <AgeTag line={l} /> },
    { title: 'Status', key: 's', render: (_, l) => <><PodStageTag stage={l.stage} />{l.rejectionReason && <Typography.Text type="danger" style={{ display: 'block', fontSize: 12 }}>{l.rejectionReason}</Typography.Text>}</> },
  ]

  const a = ageing.data
  return (
    <>
      <PageHeader
        title={isVendor ? 'Deliveries' : 'POD & deliveries'}
        description={isVendor ? 'Confirm each delivery and upload the signed copy. Open a shipment to do it.' : 'Delivered goods and the proof that backs them. Billing and claims start from verified proof.'}
      />
      {a && (
        <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
          <Col xs={12} md={6} xl={4}><Card size="small"><Statistic title="Proof outstanding" value={a.outstanding} /></Card></Col>
          <Col xs={12} md={6} xl={4}><Card size="small"><Statistic title={`Overdue (${a.overdueDays}+ days)`} value={a.overdue} styles={{ content: { color: a.overdue > 0 ? '#cf1322' : undefined } }} /></Card></Col>
          <Col xs={12} md={6} xl={4}><Card size="small"><Statistic title="Short or damaged" value={a.withExceptions} /></Card></Col>
          {a.buckets.map((b) => <Col key={b.label} xs={12} md={6} xl={3}><Card size="small"><Statistic title={b.label} value={b.count} /></Card></Col>)}
        </Row>
      )}
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap align="center" style={{ padding: 16 }}>
          <Input allowClear style={{ width: 280, maxWidth: '100%' }} prefix={<SearchOutlined />} placeholder="Search shipment, order, LR or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          <Select allowClear placeholder="All stages" style={{ width: 190 }} options={podStageOptions} value={stage} onChange={(v) => { setStage(v); setPage(1) }} />
          <Flex gap={8} align="center">
            <Switch aria-label="Overdue only" checked={overdueOnly} onChange={(v) => { setOverdueOnly(v); setPage(1) }} />
            <Typography.Text>Overdue only</Typography.Text>
          </Flex>
        </Flex>
        {queue.isError && <Alert type="error" showIcon title={queue.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<PodLineDto>
          rowKey={(l) => `${l.shipmentId}-${l.orderId}`}
          columns={columns}
          dataSource={queue.data?.items}
          loading={queue.isFetching}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: debounced || stage || overdueOnly ? 'Nothing matches these filters' : 'No deliveries yet' }}
          pagination={{ current: page, pageSize, total: queue.data?.totalCount ?? 0, showSizeChanger: true, showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`, onChange: (p, size) => { setPage(p); setPageSize(size) } }}
        />
      </Card>
      {a && a.transporters.length > 0 && (
        <Card title="Who is slowest to send proof" size="small" style={{ marginTop: 16 }}>
          <Table size="small" pagination={false} rowKey={(t) => t.transporterId ?? t.name} dataSource={a.transporters}
            columns={[{ title: 'Transporter', dataIndex: 'name' }, { title: 'Outstanding', dataIndex: 'outstanding', align: 'right' }, { title: 'Overdue', dataIndex: 'overdue', align: 'right' }, { title: 'Oldest', dataIndex: 'oldestDays', align: 'right', render: (d: number) => `${d} d` }]} />
        </Card>
      )}
    </>
  )
}
