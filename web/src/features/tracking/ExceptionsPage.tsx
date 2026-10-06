import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Descriptions, Drawer, Flex, Form, Input, Modal, Select, Space, Switch, Table, Tag, Timeline } from 'antd'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DelayReason, TrackExceptionStatus, TrackExceptionSummaryDto } from '@/lib/api/types'
import { alertTypeLabel, clock, exceptionStatusLabel, SeverityTag } from './shared'
import { reasonOptions } from './ShipmentTrackingPage'
import { useTrackingLive } from './useTrackingLive'

const STATUSES: TrackExceptionStatus[] = ['Open', 'Acknowledged', 'InProgress', 'Escalated', 'Resolved', 'Closed']

export function TrackingExceptionsPage() {
  const live = useTrackingLive()
  const [params, setParams] = useSearchParams()
  const [status, setStatus] = useState<TrackExceptionStatus | undefined>()
  const [overdue, setOverdue] = useState(false)
  const [page, setPage] = useState(1)
  const open = params.get('open')
  const query = { status, overdue: overdue || undefined, page, pageSize: 20 }
  const list = useQuery({ queryKey: queryKeys.tracking.exceptions(query), queryFn: () => trackingApi.exceptions(query), refetchInterval: live.pollMs })

  return (
    <>
      <PageHeader title="Tracking exceptions" description="Problems that need a person. An exception stays open until someone resolves it, even when the cause has cleared by itself." />
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Select<TrackExceptionStatus> allowClear placeholder="Status" style={{ width: 170 }} value={status} onChange={(v) => { setStatus(v); setPage(1) }} options={STATUSES.map((v) => ({ value: v, label: exceptionStatusLabel(v) }))} />
        <Space>Overdue only <Switch checked={overdue} onChange={setOverdue} /></Space>
      </Flex>
      <Table<TrackExceptionSummaryDto>
        size="small" rowKey="id" loading={list.isLoading} dataSource={list.data?.items ?? []}
        pagination={{ current: page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
        onRow={(r) => ({ onClick: () => setParams({ open: r.id }), style: { cursor: 'pointer' } })}
        columns={[
          { title: 'Number', dataIndex: 'number' },
          { title: 'Type', dataIndex: 'type', render: alertTypeLabel },
          { title: 'Severity', render: (_, r) => <SeverityTag severity={r.severity} /> },
          { title: 'Trip', render: (_, r) => <Link to={`/tracking/shipments/${r.shipmentId}`} onClick={(e) => e.stopPropagation()}>{r.tripReference}</Link> },
          { title: 'Carrier', dataIndex: 'transporterReference', render: (v: string | null) => v ?? '—' },
          { title: 'Status', render: (_, r) => <>{exceptionStatusLabel(r.status)}{r.conditionCleared && r.status !== 'Resolved' && r.status !== 'Closed' && <Tag color="blue" style={{ marginLeft: 6 }}>Cause cleared</Tag>}{r.overdue && <Tag color="red" style={{ marginLeft: 6 }}>Overdue</Tag>}</> },
          { title: 'Level', dataIndex: 'escalationLevel', render: (v: number, r) => (v > 0 ? `${v}${r.escalatedTo ? ` · ${r.escalatedTo}` : ''}` : '—') },
          { title: 'Raised', dataIndex: 'raisedAt', render: clock },
        ]}
      />
      <ExceptionDrawer id={open} onClose={() => setParams({})} />
    </>
  )
}

function ExceptionDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const { can } = useAuth()
  const { message } = App.useApp()
  const client = useQueryClient()
  const [resolving, setResolving] = useState(false)
  const [noting, setNoting] = useState(false)
  const q = useQuery({ queryKey: queryKeys.tracking.exception(id ?? ''), queryFn: () => trackingApi.exception(id!), enabled: !!id })
  const done = () => void client.invalidateQueries({ queryKey: queryKeys.tracking.all })
  const act = <T,>(fn: () => Promise<T>) => ({ mutationFn: fn, onSuccess: done, onError: (e: unknown) => void message.error(toApiError(e).message) })
  const ack = useMutation(act(() => trackingApi.acknowledgeException(id!)))
  const escalate = useMutation(act(() => trackingApi.escalateException(id!, 'Escalated by an operator')))
  const close = useMutation(act(() => trackingApi.closeException(id!)))
  const resolve = useMutation({ ...act(() => Promise.resolve(null)), mutationFn: (v: { rootCause: string; delayReason: DelayReason | null; actionTaken: string | null }) => trackingApi.resolveException(id!, v), onSuccess: () => { done(); setResolving(false) } })
  const note = useMutation({ mutationFn: (text: string) => trackingApi.noteException(id!, text), onSuccess: () => { done(); setNoting(false) }, onError: (e) => void message.error(toApiError(e).message) })
  const e = q.data
  const manage = can('tracking.manage')
  const finished = e && (e.summary.status === 'Resolved' || e.summary.status === 'Closed')

  return (
    <Drawer open={!!id} onClose={onClose} size={520} title={e ? `${e.summary.number} · ${alertTypeLabel(e.summary.type)}` : ''} loading={q.isLoading}>
      {e && (
        <Flex vertical gap={16}>
          <Descriptions size="small" column={1} bordered>
            <Descriptions.Item label="What happened">{e.summary.description}</Descriptions.Item>
            <Descriptions.Item label="Trip"><Link to={`/tracking/shipments/${e.summary.shipmentId}`}>{e.summary.tripReference}</Link> · {e.summary.vehicleReference ?? 'no vehicle'}</Descriptions.Item>
            <Descriptions.Item label="Driver">{e.driverName ?? '—'}{e.driverPhone ? ` · ${e.driverPhone}` : ''}</Descriptions.Item>
            <Descriptions.Item label="Severity"><SeverityTag severity={e.summary.severity} /> {exceptionStatusLabel(e.summary.status)}</Descriptions.Item>
            <Descriptions.Item label="Due">{clock(e.summary.dueAt)}{e.summary.overdue ? ' (overdue)' : ''}</Descriptions.Item>
            <Descriptions.Item label="Escalation">{e.summary.escalationLevel > 0 ? `Level ${e.summary.escalationLevel}${e.summary.escalatedTo ? ` to ${e.summary.escalatedTo}` : ''}` : 'Not escalated'}</Descriptions.Item>
            {e.rootCause && <Descriptions.Item label="Root cause">{e.rootCause}</Descriptions.Item>}
            {e.actionTaken && <Descriptions.Item label="Action taken">{e.actionTaken}</Descriptions.Item>}
          </Descriptions>
          {manage && !finished && (
            <Space wrap>
              {e.summary.status === 'Open' && <Button onClick={() => ack.mutate()}>Acknowledge</Button>}
              <Button onClick={() => escalate.mutate()}>Escalate</Button>
              <Button type="primary" onClick={() => setResolving(true)}>Resolve</Button>
              <Button onClick={() => setNoting(true)}>Add note</Button>
            </Space>
          )}
          {manage && e.summary.status === 'Resolved' && <Button onClick={() => close.mutate()}>Close</Button>}
          <Timeline items={e.notes.map((n) => ({ content: <div>{n.text}<div style={{ fontSize: 12 }}>{clock(n.at)}</div></div> }))} />
        </Flex>
      )}
      <Modal open={resolving} title="Resolve exception" footer={null} onCancel={() => setResolving(false)} destroyOnHidden>
        <Form layout="vertical" onFinish={(v: { rootCause: string; delayReason?: DelayReason; actionTaken?: string }) => resolve.mutate({ rootCause: v.rootCause, delayReason: v.delayReason ?? null, actionTaken: v.actionTaken ?? null })}>
          <Form.Item name="rootCause" label="Root cause" rules={[{ required: true }]}><Input.TextArea rows={2} /></Form.Item>
          <Form.Item name="delayReason" label="Delay reason"><Select allowClear options={reasonOptions} /></Form.Item>
          <Form.Item name="actionTaken" label="Action taken"><Input.TextArea rows={2} /></Form.Item>
          <Button type="primary" htmlType="submit" loading={resolve.isPending}>Resolve</Button>
        </Form>
      </Modal>
      <Modal open={noting} title="Add a note" footer={null} onCancel={() => setNoting(false)} destroyOnHidden>
        <Form layout="vertical" onFinish={(v: { text: string }) => note.mutate(v.text)}>
          <Form.Item name="text" rules={[{ required: true }]}><Input.TextArea rows={3} /></Form.Item>
          <Button type="primary" htmlType="submit" loading={note.isPending}>Add</Button>
        </Form>
      </Modal>
    </Drawer>
  )
}
