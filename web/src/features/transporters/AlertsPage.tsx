import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, Modal, Select, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { Can } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AlertDto, AlertSeverity, AlertStatus } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'

const severityColor: Record<AlertSeverity, string> = { Low: 'default', Medium: 'gold', High: 'orange', Critical: 'red' }
const typeLabel: Record<string, string> = {
  PLACEMENT_OVERDUE: 'Vehicle overdue', PLACEMENT_NO_SHOW: 'No-show', PICKUP_OVERDUE: 'Pickup overdue', DELIVERY_OVERDUE: 'Delivery overdue', POD_OVERDUE: 'Proof overdue',
  CARRIER_PICKUP_DELAY: 'Late pickup (carrier)', CARRIER_DELIVERY_DELAY: 'Late delivery (carrier)', DELAY_ATTRIBUTION_REQUIRED: 'Needs a delay reason',
}

/** What needs attention across all transporters. Overdue items appear by themselves and clear when the record catches up. */
export function AlertsPage() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<AlertStatus | undefined>('Open')
  const [severity, setSeverity] = useState<AlertSeverity>()
  const [page, setPage] = useState(1)
  const [resolving, setResolving] = useState<AlertDto | null>(null)
  const [comments, setComments] = useState('')
  const params = { status, severity, page, pageSize: 20 }
  const alerts = useQuery({ queryKey: queryKeys.performance.alerts(params), queryFn: () => performanceApi.alerts(params), placeholderData: (p) => p })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
  const fail = (e: unknown) => void message.error(toApiError(e).message)
  const acknowledge = useMutation({ mutationFn: (id: string) => performanceApi.acknowledgeAlert(id), onSuccess: refresh, onError: fail })
  const resolve = useMutation({
    mutationFn: () => performanceApi.resolveAlert(resolving!.id, comments.trim() || undefined),
    onSuccess: async () => { await refresh(); setResolving(null); setComments('') },
    onError: fail,
  })

  return (
    <>
      <PageHeader title="Transporter alerts" description="Vehicles, pickups, deliveries and proofs that are overdue, and late loads waiting for a reason." />
      <Card size="small" style={{ marginBottom: 16 }}>
        <Flex gap={12} wrap>
          <Select aria-label="Status" allowClear placeholder="Any status" style={{ width: 180 }} virtual={false} value={status} onChange={(v) => { setStatus(v); setPage(1) }}
            options={(['Open', 'Acknowledged', 'Resolved'] as AlertStatus[]).map((s) => ({ value: s, label: s }))} />
          <Select aria-label="Severity" allowClear placeholder="Any severity" style={{ width: 180 }} virtual={false} value={severity} onChange={(v) => { setSeverity(v); setPage(1) }}
            options={(['Low', 'Medium', 'High', 'Critical'] as AlertSeverity[]).map((s) => ({ value: s, label: s }))} />
        </Flex>
      </Card>
      {alerts.isError && <Alert type="error" showIcon title={alerts.error.message} style={{ marginBottom: 16 }} />}
      <Card styles={{ body: { padding: 0 } }}>
        <Table<AlertDto>
          rowKey="id"
          loading={alerts.isFetching}
          dataSource={alerts.data?.items ?? []}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'Nothing needs attention.' }}
          pagination={{ current: page, pageSize: 20, total: alerts.data?.totalCount ?? 0, showSizeChanger: false, onChange: setPage }}
          columns={[
            { title: 'Severity', dataIndex: 'severity', render: (s: AlertSeverity) => <Tag color={severityColor[s]}>{s}</Tag> },
            { title: 'Alert', key: 'm', render: (_, a) => <div><Typography.Text strong>{typeLabel[a.alertType] ?? a.alertType}</Typography.Text><br /><Typography.Text type="secondary">{a.message}</Typography.Text></div> },
            { title: 'Transporter', key: 't', render: (_, a) => <Link to={`/transporters/${a.transporterId}`}>{a.transporterName ?? 'Transporter'}</Link> },
            { title: 'Shipment', key: 's', render: (_, a) => (a.shipmentId ? <Link to={`/shipments/${a.shipmentId}`}>{a.shipmentNumber}</Link> : '—') },
            { title: 'Raised', dataIndex: 'createdAt', render: formatDateTime },
            { title: 'Status', dataIndex: 'status', render: (s: AlertStatus) => <Tag color={s === 'Open' ? 'red' : s === 'Acknowledged' ? 'blue' : 'green'}>{s}</Tag> },
            { title: '', key: 'a', align: 'right', render: (_, a) => (
              <Can permission="transporters.performance.manage">
                {a.status !== 'Resolved' && (
                  <Flex gap={4} justify="flex-end">
                    {a.status === 'Open' && <Button size="small" loading={acknowledge.isPending && acknowledge.variables === a.id} onClick={() => acknowledge.mutate(a.id)}>Acknowledge</Button>}
                    <Button size="small" onClick={() => { setComments(''); setResolving(a) }}>Resolve</Button>
                  </Flex>
                )}
              </Can>) },
          ]}
        />
      </Card>
      <Modal open={resolving !== null} title="Resolve this alert" okText="Resolve" confirmLoading={resolve.isPending} onCancel={() => setResolving(null)} onOk={() => resolve.mutate()} destroyOnHidden>
        <Typography.Paragraph type="secondary">{resolving?.message}</Typography.Paragraph>
        <Input.TextArea aria-label="What was done" rows={3} maxLength={500} placeholder="What was done (optional)" value={comments} onChange={(e) => setComments(e.target.value)} />
      </Modal>
    </>
  )
}
