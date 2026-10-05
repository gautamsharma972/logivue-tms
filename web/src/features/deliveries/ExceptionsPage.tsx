import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { PaperClipOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Checkbox, DatePicker, Descriptions, Drawer, Flex, Input, InputNumber, Select, Table, Tag, Timeline, Typography, type TableColumnsType } from 'antd'
import type { Dayjs } from 'dayjs'
import { useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DeliveryExceptionDto, DeliveryExceptionSeverity, DeliveryExceptionStatus, DeliveryExceptionSummaryDto, DeliveryExceptionType, ListDeliveryExceptionsParams, ResponsibleParty } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { ExceptionStatusTag, SeverityTag, exceptionStatusOptions, exceptionTypeLabel, exceptionTypeOptions } from './shared'

const parties: ResponsibleParty[] = ['Unknown', 'Transporter', 'Warehouse', 'Customer', 'Supplier']
const severities: DeliveryExceptionSeverity[] = ['Low', 'Medium', 'High', 'Critical']

function ExceptionDrawer({ id, canManage, onClose }: { id: string; canManage: boolean; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const detail = useQuery({ queryKey: queryKeys.deliveries.exception(id), queryFn: () => deliveriesApi.exception(id) })
  const [note, setNote] = useState('')
  const [department, setDepartment] = useState('')
  const [dueAt, setDueAt] = useState<Dayjs | null>(null)
  const [severity, setSeverity] = useState<DeliveryExceptionSeverity>()
  const [rootCause, setRootCause] = useState('')
  const [party, setParty] = useState<ResponsibleParty>('Unknown')
  const [resolution, setResolution] = useState('')
  const [action, setAction] = useState('')
  const [impact, setImpact] = useState<number | null>(null)
  const [claim, setClaim] = useState('')
  const [escalation, setEscalation] = useState('')
  const [attachNote, setAttachNote] = useState('')
  const fileInput = useRef<HTMLInputElement>(null)

  const run = useMutation({
    mutationFn: (fn: () => Promise<DeliveryExceptionDto>) => fn(),
    onSuccess: async (e) => {
      queryClient.setQueryData(queryKeys.deliveries.exception(id), e)
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
      void message.success('Saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const claim_ = useMutation({
    mutationFn: (deliveryId: string) => deliveriesApi.createClaims(deliveryId, null),
    onSuccess: async (made) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
      void message.success(made.length === 0 ? 'Nothing new to claim' : `Claim sent with the evidence: ${made.map((c) => c.reference).join(', ')}`)
    },
    onError: (x) => void message.error(toApiError(x).message),
  })

  const e = detail.data
  const claimable = e && ['Shortage', 'Damage', 'CustomerRefusal'].includes(e.summary.type) && !e.summary.claimReference
  const open = e && !['Resolved', 'Closed'].includes(e.summary.status)
  return (
    <Drawer open onClose={onClose} size="large" title={e ? `${e.summary.number} · ${exceptionTypeLabel[e.summary.type]}` : 'Exception'} destroyOnHidden>
      {detail.isError && <Alert type="error" showIcon title={detail.error.message} />}
      {e && (
        <Flex vertical gap={16}>
          <Flex gap={8} wrap><SeverityTag severity={e.summary.severity} /><ExceptionStatusTag status={e.summary.status} />{e.summary.overdue && <Tag color="red">Overdue</Tag>}</Flex>
          <Typography.Paragraph>{e.description}</Typography.Paragraph>
          <Descriptions column={1} size="small">
            <Descriptions.Item label="Delivery"><Link to={`/delivery/${e.summary.deliveryId}`}>{e.summary.deliveryNumber}</Link> · {e.summary.customerName}</Descriptions.Item>
            <Descriptions.Item label="Transporter">{e.summary.transporterReference ?? '—'}{e.summary.vehicleReference ? ` · ${e.summary.vehicleReference}` : ''}</Descriptions.Item>
            <Descriptions.Item label="Owner">{e.summary.department ?? (e.summary.ownerUserId ? 'Assigned' : 'Nobody yet')}</Descriptions.Item>
            <Descriptions.Item label="Due">{formatDateTime(e.summary.dueAt)}</Descriptions.Item>
            <Descriptions.Item label="Responsible">{e.responsibleParty === 'Unknown' ? 'Not yet established' : e.responsibleParty}</Descriptions.Item>
            {e.rootCause && <Descriptions.Item label="Root cause">{e.rootCause}</Descriptions.Item>}
            {e.resolution && <Descriptions.Item label="Resolution">{e.resolution}</Descriptions.Item>}
            {e.financialImpact != null && <Descriptions.Item label="Financial impact">₹{e.financialImpact}</Descriptions.Item>}
            {e.summary.claimReference && <Descriptions.Item label="Claim">{e.summary.claimReference}</Descriptions.Item>}
          </Descriptions>

          {canManage && open && (
            <Card size="small" title="Take action">
              <Flex vertical gap={12}>
                {e.summary.status === 'Open' && <Button onClick={() => run.mutate(() => deliveriesApi.acknowledgeException(id))}>Acknowledge</Button>}
                <Flex gap={8} wrap>
                  <Input style={{ width: 200 }} aria-label="Department" placeholder="Department" value={department} onChange={(x) => setDepartment(x.target.value)} />
                  <DatePicker showTime aria-label="Due" value={dueAt} onChange={setDueAt} />
                  <Select allowClear aria-label="Severity" placeholder="Severity" style={{ width: 130 }} options={severities.map((v) => ({ value: v, label: v }))} value={severity} onChange={setSeverity} />
                  <Button disabled={!department.trim()} onClick={() => run.mutate(() => deliveriesApi.assignException(id, { ownerUserId: null, department: department.trim(), dueAt: dueAt?.toISOString() ?? null, severity: severity ?? null }))}>Assign</Button>
                </Flex>
                <Flex gap={8} wrap>
                  <Input style={{ width: 260 }} aria-label="Reason to escalate" placeholder="Why escalate?" value={escalation} onChange={(x) => setEscalation(x.target.value)} />
                  <Button danger disabled={!escalation.trim() || e.summary.status === 'Escalated'} onClick={() => run.mutate(() => deliveriesApi.escalateException(id, escalation.trim()))}>Escalate</Button>
                </Flex>
                <Flex gap={8}>
                  <Input aria-label="Note" placeholder="Add a note" value={note} onChange={(x) => setNote(x.target.value)} />
                  <Button disabled={!note.trim()} onClick={() => { run.mutate(() => deliveriesApi.noteException(id, note.trim())); setNote('') }}>Add note</Button>
                </Flex>
                <Typography.Text strong>Resolve</Typography.Text>
                <Input.TextArea rows={2} aria-label="Resolution" placeholder="How was it resolved?" value={resolution} onChange={(x) => setResolution(x.target.value)} />
                <Flex gap={8} wrap>
                  <Input style={{ width: 240 }} aria-label="Root cause" placeholder="Root cause" value={rootCause} onChange={(x) => setRootCause(x.target.value)} />
                  <Select aria-label="Responsible party" style={{ width: 160 }} options={parties.map((p) => ({ value: p, label: p === 'Unknown' ? 'Not established' : p }))} value={party} onChange={setParty} />
                  <InputNumber aria-label="Financial impact" placeholder="Impact ₹" min={0} value={impact} onChange={setImpact} />
                  <Input style={{ width: 140 }} aria-label="Claim reference" placeholder="Claim ref." value={claim} onChange={(x) => setClaim(x.target.value)} />
                </Flex>
                <Input aria-label="Action taken" placeholder="Action taken" value={action} onChange={(x) => setAction(x.target.value)} />
                <Button type="primary" disabled={!resolution.trim()} loading={run.isPending}
                  onClick={() => run.mutate(() => deliveriesApi.resolveException(id, { resolution: resolution.trim(), rootCause: rootCause.trim() || null, responsibleParty: party, actionTaken: action.trim() || null, financialImpact: impact, claimReference: claim.trim() || null }))}>
                  Resolve
                </Button>
              </Flex>
            </Card>
          )}
          {canManage && e.summary.status === 'Resolved' && <Button onClick={() => run.mutate(() => deliveriesApi.closeException(id))}>Close</Button>}

          <Card size="small" title="Attachments" extra={canManage && open && (
            <>
              <input ref={fileInput} type="file" accept="image/jpeg,image/png,application/pdf" hidden aria-label="Choose a file"
                onChange={(x) => { const f = x.target.files?.[0]; if (f) run.mutate(() => deliveriesApi.attachToException(id, f, attachNote.trim() || undefined)); x.target.value = ''; setAttachNote('') }} />
              <Flex gap={8}>
                <Input size="small" style={{ width: 180 }} aria-label="Attachment note" placeholder="What is it? (optional)" value={attachNote} onChange={(x) => setAttachNote(x.target.value)} />
                <Button size="small" icon={<PaperClipOutlined />} loading={run.isPending} onClick={() => fileInput.current?.click()}>Attach a file</Button>
              </Flex>
            </>
          )}>
            {(e.attachments ?? []).length === 0 ? <Typography.Text type="secondary">Nothing attached.</Typography.Text> : (e.attachments ?? []).map((a) => (
              <div key={a.id} style={{ marginBottom: 6 }}>
                <Button type="link" style={{ padding: 0 }} onClick={() => void deliveriesApi.downloadExceptionAttachment(a.id, a.fileName)}>{a.fileName}</Button>
                <Typography.Text type="secondary"> · {Math.max(1, Math.round(a.sizeBytes / 1024))} KB · {formatDateTime(a.at)}{a.note ? ` · ${a.note}` : ''}</Typography.Text>
              </div>
            ))}
          </Card>
          {canManage && claimable && <Button loading={claim_.isPending} onClick={() => claim_.mutate(e.summary.deliveryId)}>Create claim</Button>}

          <Timeline items={e.notes.map((n) => ({ content: <div>{n.text}<br /><Typography.Text type="secondary">{formatDateTime(n.at)}</Typography.Text></div> }))} />
        </Flex>
      )}
    </Drawer>
  )
}

export function ExceptionsPage() {
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const [searchParams] = useSearchParams()
  const [type, setType] = useState<DeliveryExceptionType>()
  const [status, setStatus] = useState<DeliveryExceptionStatus>()
  const [severity, setSeverity] = useState<DeliveryExceptionSeverity>()
  const [openOnly, setOpenOnly] = useState(true)
  const [overdue, setOverdue] = useState(false)
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string | null>(null)
  const params: ListDeliveryExceptionsParams = {
    type, status, severity, openOnly: openOnly || undefined, overdue: overdue || undefined, deliveryId: searchParams.get('deliveryId') ?? undefined, page, pageSize: 25,
  }
  const exceptions = useQuery({ queryKey: queryKeys.deliveries.exceptions(params), queryFn: () => deliveriesApi.exceptions(params), placeholderData: (p) => p })

  const columns: TableColumnsType<DeliveryExceptionSummaryDto> = [
    { title: 'Exception', key: 'e', render: (_, x) => <div><Typography.Text strong>{exceptionTypeLabel[x.type]}</Typography.Text><br /><Typography.Text type="secondary">{x.number}</Typography.Text></div> },
    { title: 'Delivery', key: 'd', render: (_, x) => <div>{x.deliveryNumber}<br /><Typography.Text type="secondary">{x.customerName ?? ''}</Typography.Text></div> },
    ...(isVendor ? [] : [{ title: 'Transporter', dataIndex: 'transporterReference', responsive: ['lg' as const], render: (v: string | null) => v ?? '—' }]),
    { title: 'Severity', dataIndex: 'severity', render: (v: DeliveryExceptionSeverity) => <SeverityTag severity={v} /> },
    { title: 'Age', dataIndex: 'ageHours', align: 'right', render: (v: number, x) => <span>{v < 48 ? `${Math.round(v)} h` : `${Math.round(v / 24)} d`}{x.overdue && <Tag color="red" style={{ marginLeft: 6 }}>Overdue</Tag>}</span> },
    { title: 'Owner', dataIndex: 'department', responsive: ['lg'], render: (v: string | null, x) => v ?? (x.ownerUserId ? 'Assigned' : '—') },
    { title: 'Status', dataIndex: 'status', render: (v: DeliveryExceptionStatus) => <ExceptionStatusTag status={v} /> },
  ]

  return (
    <>
      <PageHeader title="Delivery exceptions" description="Shortages, damage, refusals and failures: each has an owner, a due date and an outcome, and stays until someone resolves it." />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex gap={12} wrap align="center" style={{ padding: 16 }}>
          <Select allowClear placeholder="All types" style={{ width: 220 }} options={exceptionTypeOptions} value={type} onChange={(v) => { setType(v); setPage(1) }} />
          <Select allowClear placeholder="Any status" style={{ width: 180 }} options={exceptionStatusOptions} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
          <Select allowClear placeholder="Any severity" style={{ width: 150 }} options={severities.map((v) => ({ value: v, label: v }))} value={severity} onChange={(v) => { setSeverity(v); setPage(1) }} />
          <Checkbox checked={openOnly} onChange={(x) => { setOpenOnly(x.target.checked); setPage(1) }}>Open only</Checkbox>
          <Checkbox checked={overdue} onChange={(x) => { setOverdue(x.target.checked); setPage(1) }}>Overdue</Checkbox>
        </Flex>
        {exceptions.isError && <Alert type="error" showIcon title={exceptions.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<DeliveryExceptionSummaryDto>
          rowKey="id" columns={columns} dataSource={exceptions.data?.items} loading={exceptions.isFetching} scroll={{ x: 'max-content' }}
          onRow={(x) => ({ onClick: () => setSelected(x.id), style: { cursor: 'pointer' } })}
          locale={{ emptyText: 'No exceptions match these filters' }}
          pagination={{ current: page, pageSize: 25, total: exceptions.data?.totalCount ?? 0, showTotal: (total, r) => `${r[0]}–${r[1]} of ${total}`, onChange: setPage }}
        />
      </Card>
      {selected && <ExceptionDrawer id={selected} canManage={!isVendor && can('deliveries.exceptions.manage')} onClose={() => setSelected(null)} />}
    </>
  )
}
