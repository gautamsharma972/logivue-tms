import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, DatePicker, Descriptions, Drawer, Flex, Form, Input, InputNumber, Modal, Select, Skeleton, Table, Tabs, Tag } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { RatingSummaryDto } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { RatingBreakdown, RatingResultView, WhyThisRate } from './ratingParts'

/** Every rating that was kept (and every attempt that found no rate), with its full explanation and a check that it still reproduces. */
export function RatingHistoryPage() {
  const [filters, setFilters] = useState<{ shipmentReference?: string; qualified?: boolean; committed?: boolean; from?: string; to?: string; page: number }>({ page: 1 })
  const [open, setOpen] = useState<string | null>(null)
  const list = useQuery({ queryKey: queryKeys.freight.ratings(filters), queryFn: () => freightApi.ratings({ ...filters, pageSize: 20 }) })

  return (
    <>
      <PageHeader title="Rating history" description="What each shipment was rated at, under which contract, rate and DPH version. Kept ratings never change, whatever happens to the contracts afterwards." />
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search allowClear placeholder="Shipment reference" style={{ width: 240 }} onSearch={(v) => setFilters((f) => ({ ...f, shipmentReference: v || undefined, page: 1 }))} />
        <Select allowClear placeholder="Found a rate" style={{ width: 160 }} onChange={(qualified: boolean | undefined) => setFilters((f) => ({ ...f, qualified, page: 1 }))} options={[{ value: true, label: 'Found a rate' }, { value: false, label: 'No rate found' }]} />
        <Select allowClear placeholder="Kept" style={{ width: 160 }} onChange={(committed: boolean | undefined) => setFilters((f) => ({ ...f, committed, page: 1 }))} options={[{ value: true, label: 'Kept against a shipment' }, { value: false, label: 'Not kept' }]} />
        <DatePicker.RangePicker onChange={(v) => setFilters((f) => ({ ...f, from: v?.[0]?.format('YYYY-MM-DD'), to: v?.[1]?.format('YYYY-MM-DD'), page: 1 }))} />
      </Flex>
      <Table<RatingSummaryDto>
        size="small" rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={{ current: filters.page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: (page) => setFilters((f) => ({ ...f, page })), showSizeChanger: false }}
        onRow={(r) => ({ onClick: () => setOpen(r.id), style: { cursor: 'pointer' } })}
        columns={[
          { title: 'Rating', dataIndex: 'reference' }, { title: 'Shipment', dataIndex: 'shipmentReference', render: (v: string | null) => v ?? '—' }, { title: 'Lane', dataIndex: 'lane' },
          { title: 'Service', dataIndex: 'service' }, { title: 'Date', dataIndex: 'shipmentDate' }, { title: 'Transporter', dataIndex: 'transporterName', render: (v: string | null) => v ?? '—' },
          { title: 'Contract', render: (_, r) => (r.contractReference ? `${r.contractReference} V${r.contractRevision}` : '—') },
          { title: 'Rate', render: (_, r) => (r.rateCode ? `${r.rateCode} V${r.rateVersion}` : '—') },
          { title: 'Freight', align: 'right', render: (_, r) => (r.qualified ? <span>{formatInrExact(r.overrideAmount ?? r.totalFreight)}{r.overrideAmount != null && <Tag color="orange" style={{ marginLeft: 6 }}>Overridden</Tag>}</span> : <Tag color="red">No rate</Tag>) },
          { title: 'Kept', dataIndex: 'committed', render: (v: boolean) => (v ? <Tag color="green">Kept</Tag> : <Tag>Not kept</Tag>) },
        ]}
      />
      <RatingDrawer id={open} onClose={() => setOpen(null)} />
    </>
  )
}

function RatingDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { can } = useAuth()
  const { message } = App.useApp()
  const client = useQueryClient()
  const [overriding, setOverriding] = useState(false)
  const detail = useQuery({ queryKey: queryKeys.freight.rating(id ?? ''), queryFn: () => freightApi.rating(id!), enabled: !!id })
  const reproduce = useMutation({ mutationFn: () => freightApi.reproduce(id!), onError: (e) => void message.error(toApiError(e).message) })
  const apply = useMutation({
    mutationFn: (v: { amount: number; reason: string; approvedBy?: string }) => freightApi.override(id!, { amount: v.amount, reason: v.reason, approvedBy: v.approvedBy ?? null }),
    onSuccess: () => { setOverriding(false); void client.invalidateQueries({ queryKey: queryKeys.freight.all }) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const clear = useMutation({ mutationFn: () => freightApi.clearOverride(id!), onSuccess: () => void client.invalidateQueries({ queryKey: queryKeys.freight.all }) })
  const d = detail.data

  return (
    <Drawer open={!!id} onClose={onClose} size={720} title={d ? `${d.summary.reference} · ${d.summary.shipmentReference ?? 'not kept'}` : ''} loading={detail.isLoading}>
      {d && (
        <Tabs items={[
          { key: 'trace', label: 'Explanation', children: <RatingResultView result={d.result} /> },
          {
            key: 'detail', label: 'Record',
            children: (
              <Flex vertical gap={12}>
                <Descriptions size="small" bordered column={1}>
                  <Descriptions.Item label="Rated at">{formatDateTime(d.summary.calculatedAt)} under calculation version {d.summary.calculationVersion}</Descriptions.Item>
                  <Descriptions.Item label="Shipment date">{d.summary.shipmentDate}</Descriptions.Item>
                  {d.request && <Descriptions.Item label="Request">{d.request.weightKg ?? '—'} kg · {d.request.volumeCbm ?? '—'} CBM · {d.request.distanceKm ?? '—'} km · {d.request.stopCount} stop(s)</Descriptions.Item>}
                  {d.result.selected && <Descriptions.Item label="Contract"><Link to={`/contracts/${d.result.selected.contractId}`}>{d.result.selected.contractReference} V{d.result.selected.contractRevision}</Link></Descriptions.Item>}
                </Descriptions>
                {d.result.selected && <WhyThisRate option={d.result.selected} calculationVersion={d.summary.calculationVersion} />}
                {d.result.selected && <RatingBreakdown option={d.result.selected} />}
              </Flex>
            ),
          },
          {
            key: 'check', label: 'Reproduce',
            children: (
              <Flex vertical gap={12}>
                <Button onClick={() => reproduce.mutate()} loading={reproduce.isPending}>Rate it again from the stored request</Button>
                {reproduce.data && <Alert type={reproduce.data.matches ? 'success' : 'warning'} showIcon title={reproduce.data.matches ? 'Reproduced' : 'Differs'} description={<>{reproduce.data.message}{reproduce.data.differences.map((x) => <div key={x}>• {x}</div>)}</>} />}
              </Flex>
            ),
          },
          {
            key: 'override', label: 'Override',
            children: !d.summary.committed || !d.summary.qualified ? <Alert type="info" showIcon title="Only a kept rating that found a rate can be overridden." /> : (
              <Flex vertical gap={12}>
                <Descriptions size="small" bordered column={1}>
                  <Descriptions.Item label="System freight">{formatInrExact(d.summary.totalFreight)}</Descriptions.Item>
                  <Descriptions.Item label="Agreed freight">{d.overrideAmount == null ? 'No override' : formatInrExact(d.overrideAmount)}</Descriptions.Item>
                  {d.overrideAmount != null && <Descriptions.Item label="Reason">{d.overrideReason}{d.overrideApprovedBy ? ` · approved by ${d.overrideApprovedBy}` : ''} · {formatDateTime(d.overriddenAt)}</Descriptions.Item>}
                </Descriptions>
                {can('contracts.rating.override') ? (
                  <Flex gap={8}><Button type="primary" onClick={() => setOverriding(true)}>{d.overrideAmount == null ? 'Agree a different freight' : 'Change the override'}</Button>{d.overrideAmount != null && <Button danger onClick={() => clear.mutate()}>Remove</Button>}</Flex>
                ) : <Alert type="info" showIcon title="You are not allowed to override a rating." />}
              </Flex>
            ),
          },
        ]} />
      )}
      {detail.isLoading && <Skeleton active />}
      <Modal open={overriding} title="Agree a different freight" footer={null} onCancel={() => setOverriding(false)} destroyOnHidden>
        <Alert type="info" showIcon style={{ marginBottom: 12 }} title="The system freight is kept beside the agreed one, with who agreed and why." />
        <Form layout="vertical" onFinish={(v: { amount: number; reason: string; approvedBy?: string }) => apply.mutate(v)}>
          <Form.Item name="amount" label="Agreed freight (₹)" rules={[{ required: true }]}><InputNumber min={1} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="reason" label="Reason" rules={[{ required: true, message: 'A reason is required.' }]}><Input.TextArea rows={2} maxLength={500} /></Form.Item>
          <Form.Item name="approvedBy" label="Approved by"><Input /></Form.Item>
          <Button type="primary" htmlType="submit" loading={apply.isPending}>Save override</Button>
        </Form>
      </Modal>
    </Drawer>
  )
}
