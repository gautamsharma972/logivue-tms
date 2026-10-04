import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, Flex, Input, Modal, Select, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { PlacementDto, PlacementStatus } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'

const slaColor: Record<PlacementDto['slaStatus'], string> = { OnTime: 'green', Late: 'orange', Pending: 'blue', Overdue: 'red', NoShow: 'red', Cancelled: 'default' }
const slaLabel: Record<PlacementDto['slaStatus'], string> = { OnTime: 'On time', Late: 'Late', Pending: 'Expected', Overdue: 'Overdue', NoShow: 'No-show', Cancelled: 'Cancelled' }
const statusLabel: Record<PlacementStatus, string> = {
  VehicleAssigned: 'Vehicle named', Reported: 'Reported', Placed: 'At the site', LoadingStarted: 'Loading', NoShow: 'No-show', Cancelled: 'Cancelled',
}

/** Vehicles owed at pickup sites: expected, reported, placed. Vendors report their own; staff place, mark no-shows and cancel. */
export function PlacementsPage() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const { can, user } = useAuth()
  const isVendor = user?.transporterId != null
  const canManage = !isVendor && can('transporters.performance.manage')
  const [status, setStatus] = useState<PlacementStatus>()
  const [page, setPage] = useState(1)
  const [asking, setAsking] = useState<{ placement: PlacementDto; kind: 'no-show' | 'cancel' } | null>(null)
  const [reason, setReason] = useState('')
  const params = { status, page, pageSize: 20 }
  const placements = useQuery({ queryKey: queryKeys.performance.placements(params), queryFn: () => performanceApi.placements(params), placeholderData: (p) => p })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.performance.all })
  const fail = (e: unknown) => void message.error(toApiError(e).message)
  const report = useMutation({ mutationFn: (id: string) => performanceApi.reportPlacement(id), onSuccess: refresh, onError: fail })
  const place = useMutation({ mutationFn: (id: string) => performanceApi.placePlacement(id), onSuccess: refresh, onError: fail })
  const decide = useMutation({
    mutationFn: () => (asking!.kind === 'no-show' ? performanceApi.noShow(asking!.placement.id, reason.trim()) : performanceApi.cancelPlacement(asking!.placement.id, reason.trim())),
    onSuccess: async () => { await refresh(); setAsking(null); setReason('') },
    onError: fail,
  })

  return (
    <>
      <PageHeader title="Vehicle placements" description="The vehicle each transporter owes at the pickup site, and whether it came on time." />
      <Card size="small" style={{ marginBottom: 16 }}>
        <Select aria-label="Status" allowClear placeholder="All statuses" style={{ width: 200 }} virtual={false} value={status} onChange={(v) => { setStatus(v); setPage(1) }}
          options={(Object.keys(statusLabel) as PlacementStatus[]).map((s) => ({ value: s, label: statusLabel[s] }))} />
      </Card>
      <Card styles={{ body: { padding: 0 } }}>
        <Table<PlacementDto>
          rowKey="id"
          loading={placements.isFetching}
          dataSource={placements.data?.items ?? []}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'No placements yet. One is made when a transporter accepts a load.' }}
          pagination={{ current: page, pageSize: 20, total: placements.data?.totalCount ?? 0, showSizeChanger: false, onChange: setPage }}
          columns={[
            { title: 'Shipment', key: 's', render: (_, p) => <Link to={`/shipments/${p.shipmentId}`}>{p.shipmentNumber}</Link> },
            { title: 'Vehicle', key: 'v', render: (_, p) => <div>{p.vehicleRegistration ?? '—'}{p.replacementCount > 0 && <Tag color="orange" style={{ marginInlineStart: 8 }}>Replaced {p.replacementCount}×</Tag>}</div> },
            { title: 'Due at the site', dataIndex: 'requiredAt', render: formatDateTime },
            { title: 'Status', dataIndex: 'status', render: (s: PlacementStatus) => statusLabel[s] },
            { title: 'On time?', key: 'sla', render: (_, p) => <div><Tag color={slaColor[p.slaStatus]}>{slaLabel[p.slaStatus]}</Tag>{p.delayMinutes != null && p.delayMinutes > 0 && <Typography.Text type="secondary"> {p.delayMinutes} min late</Typography.Text>}</div> },
            { title: '', key: 'a', align: 'right', render: (_, p) => {
              const open = p.status === 'VehicleAssigned' || p.status === 'Reported'
              if (!open) return null
              return (
                <Flex gap={4} justify="flex-end" wrap>
                  {p.status === 'VehicleAssigned' && <Button size="small" loading={report.isPending && report.variables === p.id} onClick={() => report.mutate(p.id)}>Vehicle reported</Button>}
                  {canManage && <Button size="small" loading={place.isPending && place.variables === p.id} onClick={() => place.mutate(p.id)}>Mark placed</Button>}
                  {canManage && <Button size="small" danger onClick={() => { setReason(''); setAsking({ placement: p, kind: 'no-show' }) }}>No-show</Button>}
                  {canManage && <Button size="small" onClick={() => { setReason(''); setAsking({ placement: p, kind: 'cancel' }) }}>Cancel</Button>}
                </Flex>
              )
            } },
          ]}
        />
      </Card>
      <Modal
        open={asking !== null}
        title={asking?.kind === 'no-show' ? `Record a no-show: ${asking.placement.shipmentNumber}` : `Cancel the placement: ${asking?.placement.shipmentNumber ?? ''}`}
        okText={asking?.kind === 'no-show' ? 'Record no-show' : 'Cancel placement'}
        okButtonProps={{ danger: true, disabled: reason.trim() === '' }}
        confirmLoading={decide.isPending}
        onCancel={() => setAsking(null)}
        onOk={() => decide.mutate()}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">{asking?.kind === 'no-show' ? 'Only possible once the vehicle is later than the grace period. It counts against the transporter.' : 'Say why it is no longer needed.'}</Typography.Paragraph>
        <Input.TextArea aria-label="Reason" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </>
  )
}
