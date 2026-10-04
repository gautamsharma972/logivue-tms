import { LockFilled, LockOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowDownOutlined, ArrowUpOutlined, DownloadOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, Collapse, Empty, Flex, Input, Modal, Row, Select, Skeleton, Statistic, Table, Tag, Tooltip, Typography } from 'antd'
import { Suspense, lazy, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { planningApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { EditPlanRequest, LockKind, PlanOptions, PlannedVehicle, RunDto, UnplannedOrder } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { OptimizationProgress, PlanningLog } from './OptimizationProgress'
import { PlanOptionsForm } from './PlanOptionsForm'

const RouteMap = lazy(() => import('./RouteMap'))
import { DriverCell, PlanStatusTag, RouteSourceTag, SolverStatusTag, TransporterCell, UtilizationBar, VehicleCell, formatHours, formatKg, formatMinutes, objectiveLabel, percent } from './shared'

const stopKindLabel: Record<string, string> = { Pickup: 'Pickup', Drop: 'Drop', ReturnPickup: 'Return pickup', Return: 'Back at depot' }

const vehicleLabel = (v: PlannedVehicle, index: number) => `Vehicle ${index + 1} · ${v.mode === 'Ptl' ? 'Part load' : (v.vehicleTypeName ?? 'Full truck')} · ${v.orders.length} order${v.orders.length === 1 ? '' : 's'}`

interface EditorProps {
  vehicle: PlannedVehicle
  index: number
  all: PlannedVehicle[]
  vehicleTypes: { id: string; name: string }[]
  busy: boolean
  onEdit: (request: EditPlanRequest) => void
  onLock: (locked: boolean, kind?: LockKind, orderId?: string) => void
}

/** Manual changes to one vehicle. The server re-validates every one and says exactly why a change is refused. */
function VehicleEditor({ vehicle, index, all, vehicleTypes, busy, onEdit, onLock }: EditorProps) {
  const [typeId, setTypeId] = useState<string | undefined>(vehicle.vehicleTypeId ?? undefined)
  const outbound = vehicle.orders.filter((o) => o.kind === 'Delivery').sort((a, b) => a.sequence - b.sequence)
  const move = (position: number, by: -1 | 1) => {
    const ids = outbound.map((o) => o.orderId)
    const target = position + by
    ;[ids[position], ids[target]] = [ids[target]!, ids[position]!]
    onEdit({ kind: 'ReorderStops', vehicleKey: vehicle.key, orderIds: ids })
  }

  return (
    <Flex vertical gap={12}>
      {vehicle.mode === 'Ftl' && (
        <Flex gap={8} wrap align="center">
          <Typography.Text type="secondary">Vehicle type</Typography.Text>
          <Select aria-label="Vehicle type" style={{ minWidth: 200 }} virtual={false} value={typeId} onChange={setTypeId} options={vehicleTypes.map((t) => ({ value: t.id, label: t.name }))} />
          <Button disabled={!typeId || typeId === vehicle.vehicleTypeId || busy} onClick={() => typeId && onEdit({ kind: 'ChangeVehicleType', vehicleKey: vehicle.key, vehicleTypeId: typeId })}>Change type</Button>
        </Flex>
      )}
      <Table
        size="small"
        pagination={false}
        rowKey="orderId"
        dataSource={vehicle.orders}
        columns={[
          { title: '#', dataIndex: 'sequence', width: 40 },
          { title: 'Order', key: 'o', render: (_: unknown, o: PlannedVehicle['orders'][number]) => <>{o.number} {o.kind === 'ReturnPickup' && <Tag color="purple">Return</Tag>}</> },
          { title: 'Weight', dataIndex: 'weightKg', align: 'right', render: formatKg },
          {
            title: 'Change',
            key: 'c',
            render: (_: unknown, o: PlannedVehicle['orders'][number]) => {
              const position = outbound.findIndex((x) => x.orderId === o.orderId)
              const isReturn = o.kind === 'ReturnPickup'
              return (
                <Flex gap={4} wrap align="center">
                  {!isReturn && (
                    <>
                      <Button size="small" aria-label={`Move ${o.number} earlier`} icon={<ArrowUpOutlined />} disabled={busy || position <= 0} onClick={() => move(position, -1)} />
                      <Button size="small" aria-label={`Move ${o.number} later`} icon={<ArrowDownOutlined />} disabled={busy || position < 0 || position === outbound.length - 1} onClick={() => move(position, 1)} />
                      <Select
                        size="small"
                        aria-label={`Move ${o.number} to`}
                        placeholder="Move to…"
                        style={{ width: 190 }}
                        virtual={false}
                        disabled={busy}
                        value={null}
                        onChange={(target: string) => onEdit({ kind: 'MoveOrder', orderId: o.orderId, toVehicleKey: target === 'new' ? null : target })}
                        options={[
                          ...all.map((v, i) => ({ v, i })).filter(({ v }) => v.key !== vehicle.key && !v.isLocked).map(({ v, i }) => ({ value: v.key, label: vehicleLabel(v, i) })),
                          { value: 'new', label: 'A new vehicle' },
                        ]}
                      />
                    </>
                  )}
                  <Button size="small" danger disabled={busy || o.isLocked} onClick={() => onEdit({ kind: 'RemoveOrder', orderId: o.orderId })}>Remove</Button>
                  <Tooltip title={o.isLocked ? 'Unlock so this order can be moved or removed' : 'Keep this order on this vehicle'}>
                    <Button
                      size="small"
                      disabled={busy}
                      icon={o.isLocked ? <LockFilled /> : <LockOutlined />}
                      aria-label={`${o.isLocked ? 'Unlock' : 'Lock'} order ${o.number}`}
                      onClick={() => onLock(!o.isLocked, 'Order', o.orderId)}
                    />
                  </Tooltip>
                </Flex>
              )
            },
          },
        ]}
      />
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {vehicleLabel(vehicle, index)}. Return pickups stay with their truck; remove one to place it elsewhere.
      </Typography.Text>
    </Flex>
  )
}

function VehicleCard({ vehicle, index, all, editable, onLock, locking, vehicleTypes, busy, onEdit }: { vehicle: PlannedVehicle; index: number; all: PlannedVehicle[]; editable: boolean; onLock: (locked: boolean, kind?: LockKind, orderId?: string) => void; locking: boolean; vehicleTypes: { id: string; name: string }[]; busy: boolean; onEdit: (request: EditPlanRequest) => void }) {
  const title = vehicle.mode === 'Ptl' ? 'Part load (shared truck)' : (vehicle.vehicleTypeName ?? 'Full truck')
  return (
    <Card
      size="small"
      title={
        <Flex gap={8} align="center" wrap>
          <span>{title}</span>
          <Tag color={vehicle.mode === 'Ftl' ? 'blue' : 'purple'}>{vehicle.mode.toUpperCase()}</Tag>
          {vehicle.isLocked && <Tag icon={<LockFilled />} color="gold">Locked</Tag>}
          {vehicle.sequenceLocked && <Tag icon={<LockFilled />} color="gold">Stop order locked</Tag>}
          {vehicle.assignmentLocked && <Tag icon={<LockFilled />} color="gold">Vehicle &amp; driver locked</Tag>}
          {vehicle.orders.some((o) => o.isLocked) && <Tag icon={<LockFilled />} color="gold">{vehicle.orders.filter((o) => o.isLocked).length} order(s) locked</Tag>}
        </Flex>
      }
      extra={
        editable && (
          <Flex gap={8} wrap>
            <Tooltip title={vehicle.sequenceLocked ? 'Unlock the stop order' : 'Keep the stops in this order, whatever else changes'}>
              <Button size="small" disabled={locking || vehicle.isLocked} icon={vehicle.sequenceLocked ? <LockFilled /> : <LockOutlined />} aria-label={`${vehicle.sequenceLocked ? 'Unlock' : 'Lock'} stop order of ${title}`} onClick={() => onLock(!vehicle.sequenceLocked, 'Sequence')}>
                Stops
              </Button>
            </Tooltip>
            <Tooltip title={vehicle.assignmentLocked ? 'Unlock the vehicle and driver' : vehicle.assignedVehicle ? 'Keep this vehicle and driver on the trip' : 'No vehicle has been assigned to lock'}>
              <Button size="small" disabled={locking || vehicle.isLocked || (!vehicle.assignmentLocked && !vehicle.assignedVehicle)} icon={vehicle.assignmentLocked ? <LockFilled /> : <LockOutlined />} aria-label={`${vehicle.assignmentLocked ? 'Unlock' : 'Lock'} vehicle and driver of ${title}`} onClick={() => onLock(!vehicle.assignmentLocked, 'Assignment')}>
                Vehicle &amp; driver
              </Button>
            </Tooltip>
            <Tooltip title={vehicle.isLocked ? 'Unlock so re-planning can change it' : 'Lock so re-planning keeps it exactly as it is'}>
              <Button size="small" loading={locking} icon={vehicle.isLocked ? <LockFilled /> : <LockOutlined />} aria-label={`${vehicle.isLocked ? 'Unlock' : 'Lock'} ${title}`} onClick={() => onLock(!vehicle.isLocked)}>
                {vehicle.isLocked ? 'Unlock' : 'Lock'}
              </Button>
            </Tooltip>
          </Flex>
        )
      }
    >
      <Flex justify="space-between" wrap gap={12}>
        <div>
          <Typography.Text strong>{vehicle.pickupCity}, {vehicle.pickupState}</Typography.Text>
          <br />
          <Typography.Text type="secondary">{vehicle.transporterName ?? '—'} · {vehicle.contractReference ?? ''}</Typography.Text>
          {(vehicle.assignedVehicle || vehicle.assignedDriver) && (
            <div>
              <Typography.Text>
                {vehicle.assignedVehicle?.registration ?? 'No vehicle'}
                {vehicle.assignedDriver ? ` · ${vehicle.assignedDriver.name}${vehicle.assignedDriver.phone ? ` (${vehicle.assignedDriver.phone})` : ''}` : ' · no driver'}
              </Typography.Text>
              {vehicle.assignedVehicle?.compliance === 'ExpiringSoon' && <Tag color="orange" style={{ marginInlineStart: 8 }}>Papers expiring</Tag>}
            </div>
          )}
        </div>
        <div style={{ textAlign: 'right' }}>
          <Typography.Title level={4} style={{ margin: 0 }}>{formatInrExact(vehicle.estimatedCost)}</Typography.Title>
          {vehicle.consolidationSaving !== null && <Tag color="green">Consolidation saves {formatInrExact(vehicle.consolidationSaving)}</Tag>}
        </div>
      </Flex>

      <Row gutter={16} style={{ marginTop: 12 }}>
        <Col xs={24} md={12}>
          <Typography.Text type="secondary">Weight {formatKg(vehicle.weightKg)}{vehicle.payloadKg ? ` of ${formatKg(vehicle.payloadKg)}` : ''}</Typography.Text>
          <UtilizationBar value={vehicle.weightUtilisation} />
        </Col>
        <Col xs={24} md={12}>
          <Typography.Text type="secondary">
            {vehicle.volumeCbm === null ? 'Volume not entered on the orders' : `Volume ${vehicle.volumeCbm} CBM${vehicle.volumeCapacityCbm ? ` of ${vehicle.volumeCapacityCbm} CBM` : ''}`}
          </Typography.Text>
          {vehicle.volumeCbm !== null && <UtilizationBar value={vehicle.volumeUtilisation} />}
        </Col>
      </Row>

      {vehicle.distanceKm !== null && (
        <Flex gap={8} wrap align="center" style={{ marginTop: 12 }}>
          <Typography.Text strong>{vehicle.distanceKm.toLocaleString('en-IN')} km</Typography.Text>
          <Typography.Text type="secondary">· driving {formatMinutes(vehicle.durationMinutes)} · transit {formatHours(vehicle.transitHours)}</Typography.Text>
          <RouteSourceTag source={vehicle.routeSource} />
          {vehicle.costPerTonneKm !== null && <Tag>₹{vehicle.costPerTonneKm}/tonne-km</Tag>}
        </Flex>
      )}

      {(vehicle.savingPercent !== null || vehicle.backhaulSaving !== null || vehicle.sequenceMethod) && (
        <Flex gap={6} wrap style={{ marginTop: 8 }}>
          {vehicle.separateCost !== null && vehicle.savingPercent !== null && (
            <Tag color="green">vs separate trips ₹{vehicle.separateCost.toLocaleString('en-IN')}: saves {vehicle.savingPercent}%{vehicle.additionalKm !== null ? ` for ${vehicle.additionalKm >= 0 ? '+' : ''}${vehicle.additionalKm} km` : ''}</Tag>
          )}
          {vehicle.backhaulSaving !== null && <Tag color="purple">Return load saves {formatInrExact(vehicle.backhaulSaving)}</Tag>}
          {vehicle.sequenceMethod && <Tag>Stop order: {vehicle.sequenceMethod === 'Exact' ? 'best of all orders' : 'improved heuristic'}</Tag>}
        </Flex>
      )}
      {vehicle.routeNote && <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>{vehicle.routeNote}</Typography.Paragraph>}

      <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 8 }}>{vehicle.reason}</Typography.Paragraph>

      <Flex gap={6} wrap align="center">
        {vehicle.orders.map((o) => (
          <Tooltip key={o.orderId} title={`${o.dropCity}, ${o.dropState} · ${formatKg(o.weightKg)}`}>
            <Tag color={o.kind === 'ReturnPickup' ? 'purple' : undefined}>{o.sequence}. {o.number}{o.kind === 'ReturnPickup' ? ' (return)' : ''}</Tag>
          </Tooltip>
        ))}
        {vehicle.shipmentId && <Link to={`/shipments/${vehicle.shipmentId}`}>Shipment {vehicle.shipmentNumber}</Link>}
      </Flex>

      <Collapse
        ghost
        size="small"
        items={[...(editable ? [{ key: 'edit', label: 'Edit this vehicle', children: <VehicleEditor vehicle={vehicle} index={index} all={all} vehicleTypes={vehicleTypes} busy={busy} onEdit={onEdit} onLock={onLock} /> }] : []), ...(vehicle.stops && vehicle.stops.length > 1 ? [{
          key: 'route',
          label: `Route and stops (${vehicle.stops.length})`,
          children: (
            <Flex vertical gap={12}>
              <Table
                size="small"
                pagination={false}
                rowKey="sequence"
                dataSource={vehicle.stops}
                columns={[
                  { title: '#', dataIndex: 'sequence', width: 40 },
                  { title: 'Stop', key: 's', render: (_: unknown, st: NonNullable<PlannedVehicle['stops']>[number]) => <>{stopKindLabel[st.kind] ?? st.kind} · {st.label}</> },
                  { title: 'Arrive', dataIndex: 'plannedArrival', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
                  { title: 'Waits', dataIndex: 'waitMinutes', render: (m: number | null) => (m ? formatMinutes(m) : '—') },
                  { title: 'Depart', dataIndex: 'plannedDeparture', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
                ]}
              />
              <Suspense fallback={<Skeleton active />}>
                <RouteMap stops={vehicle.stops} />
              </Suspense>
            </Flex>
          ),
        }] : []), {
          key: 'alt',
          label: `Alternatives considered (${vehicle.alternatives.length})`,
          children: (
            <Table
              size="small"
              pagination={false}
              rowKey={(a) => `${a.mode}-${a.vehicleTypeId ?? 'ptl'}`}
              dataSource={vehicle.alternatives}
              columns={[
                { title: 'Option', key: 'o', render: (_, a) => <>{a.mode === 'Ptl' ? 'Part load' : a.vehicleTypeName}{a.chosen && <Tag color="green" style={{ marginInlineStart: 8 }}>Chosen</Tag>}</> },
                { title: 'Transporter', key: 't', render: (_, a) => <TransporterCell transporter={a.transporter} fallbackName={a.transporterName} /> },
                { title: 'Vehicle', key: 'veh', render: (_, a) => (a.mode === 'Ptl' ? '—' : <VehicleCell typeName={a.vehicleTypeName} vehicle={a.vehicle} priced={a.total !== null} />) },
                { title: 'Driver', key: 'drv', render: (_, a) => (a.mode === 'Ptl' || a.total === null ? '—' : <DriverCell driver={a.driver} />) },
                { title: 'Cost', dataIndex: 'total', align: 'right', render: (t: number | null) => formatInrExact(t) },
                { title: 'Transit', dataIndex: 'transitHours', render: (h: number | null, a: PlannedVehicle['alternatives'][number]) => (h === null ? '—' : <>{formatHours(h)} {a.meetsDeadline === false && <Tag color="red">Late</Tag>}</>) },
                { title: 'Why', dataIndex: 'verdict' },
              ]}
            />
          ),
        }]}
      />
    </Card>
  )
}

function UnplannedCard({ items, vehicles, editable, busy, onEdit }: { items: UnplannedOrder[]; vehicles: PlannedVehicle[]; editable: boolean; busy: boolean; onEdit: (request: EditPlanRequest) => void }) {
  return (
    <Card title={`Could not be planned (${items.length})`} size="small">
      <Flex vertical gap={12}>
        {items.map((u) => (
          <div key={u.orderId}>
            <Flex justify="space-between" align="flex-start" gap={8} wrap>
              <div>
                <Typography.Text strong>{u.number}</Typography.Text> <Tag color="red">{u.code.replaceAll('_', ' ').toLowerCase()}</Tag>
                <div>{u.reason}</div>
                {u.suggestions.length > 0 && <Typography.Text type="secondary">Try: {u.suggestions.join(' · ')}</Typography.Text>}
              </div>
              {editable && (
                <Select
                  size="small"
                  aria-label={`Add ${u.number} to`}
                  placeholder="Add to a vehicle…"
                  style={{ width: 220 }}
                  virtual={false}
                  disabled={busy}
                  value={null}
                  onChange={(target: string) => onEdit({ kind: 'AddOrder', orderId: u.orderId, toVehicleKey: target === 'new' ? null : target })}
                  options={[
                    ...vehicles.map((v, i) => ({ v, i })).filter(({ v }) => !v.isLocked).map(({ v, i }) => ({ value: v.key, label: vehicleLabel(v, i) })),
                    { value: 'new', label: 'A new vehicle' },
                  ]}
                />
              )}
            </Flex>
          </div>
        ))}
      </Flex>
    </Card>
  )
}

export function PlanRunPage() {
  const { id = '' } = useParams()
  const { can } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<'replan' | 'cancel' | null>(null)
  const [reason, setReason] = useState('')
  const [options, setOptions] = useState<PlanOptions | null>(null)

  const run = useQuery({
    queryKey: queryKeys.planning.run(id),
    queryFn: () => planningApi.run(id),
    // While the server is still calculating, look again every couple of seconds.
    refetchInterval: (query) => (query.state.data?.status === 'Running' ? 1500 : false),
  })
  const versions = useQuery({ queryKey: queryKeys.planning.versions(id), queryFn: () => planningApi.versions(id), enabled: run.isSuccess })

  const refresh = async (updated: RunDto) => {
    queryClient.setQueryData(queryKeys.planning.run(updated.id), updated)
    await queryClient.invalidateQueries({ queryKey: queryKeys.planning.all })
    await queryClient.invalidateQueries({ queryKey: queryKeys.orders.all })
    await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all })
  }
  const fail = (e: unknown) => void message.error(toApiError(e).message)

  const [editError, setEditError] = useState<string | null>(null)
  const [pendingEdit, setPendingEdit] = useState<EditPlanRequest | null>(null)
  const [editReason, setEditReason] = useState('')
  const vehicleTypes = useQuery({ queryKey: queryKeys.shipments.vehicleTypes, queryFn: () => planningApi.vehicleTypes(), enabled: run.isSuccess && can('shipments.plan') })
  const edit = useMutation({
    mutationFn: (request: EditPlanRequest) => planningApi.edit(id, request),
    onSuccess: async (r) => {
      setEditError(null)
      setPendingEdit(null)
      setEditReason('')
      await refresh(r)
      void message.success(`Version ${r.planVersion} saved`)
      navigate(`/planning/runs/${r.id}`)
    },
    onError: (e) => {
      setEditError(toApiError(e).message)
      setPendingEdit(null)
    },
  })
  // A plain re-ordering of stops goes straight through; any other manual change overrides the planner and needs a reason.
  const requestEdit = (request: EditPlanRequest) => {
    if (request.kind === 'ReorderStops') {
      edit.mutate(request)
    } else {
      setEditReason('')
      setPendingEdit(request)
    }
  }
  const lock = useMutation({ mutationFn: (v: { key: string; locked: boolean; kind?: LockKind; orderId?: string }) => planningApi.lockVehicle(id, v.key, v.locked, v.kind, v.orderId), onSuccess: refresh, onError: fail })
  const approve = useMutation({ mutationFn: () => planningApi.approve(id), onSuccess: async (r) => { await refresh(r); void message.success('Plan approved') }, onError: fail })
  const commit = useMutation({ mutationFn: () => planningApi.commit(id), onSuccess: async (r) => { await refresh(r); void message.success('Plan committed: draft shipments created') }, onError: fail })
  const cancel = useMutation({ mutationFn: (why: string) => planningApi.cancelRun(id, why), onSuccess: async (r) => { await refresh(r); setDialog(null); setReason('') }, onError: fail })
  const replan = useMutation({
    mutationFn: () => planningApi.reoptimize(id, reason.trim(), options),
    onSuccess: async (r) => {
      await refresh(r)
      setDialog(null)
      setReason('')
      void message.success(`Version ${r.planVersion} created`)
      navigate(`/planning/runs/${r.id}`)
    },
    onError: fail,
  })

  if (run.isLoading) return <Skeleton active />
  if (run.isError) return <Alert type="error" showIcon title={run.error.message} action={<Link to="/planning">Back to planning</Link>} />
  const data = run.data
  if (!data) return null

  if (data.status === 'Running') {
    return (
      <>
        <PageHeader
          title={`${data.number} · version ${data.planVersion}`}
          description={`Planning date ${data.planningDate} · started ${formatDateTime(data.startedAt ?? data.createdAt)}`}
          actions={
            <>
              <PlanStatusTag status={data.status} />
              {can('shipments.plan') && <Button danger onClick={() => setDialog('cancel')}>Cancel plan</Button>}
            </>
          }
        />
        <OptimizationProgress run={data} />
        <Modal
          open={dialog === 'cancel'}
          title="Cancel this plan?"
          okText="Cancel plan"
          okButtonProps={{ danger: true, disabled: reason.trim().length === 0 }}
          confirmLoading={cancel.isPending}
          onOk={() => cancel.mutate(reason.trim())}
          onCancel={() => { setDialog(null); setReason('') }}
          destroyOnHidden
        >
          <Input.TextArea rows={3} maxLength={500} aria-label="Reason for cancelling" placeholder="Why is it being cancelled?" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Modal>
      </>
    )
  }

  const reviewable = data.status === 'Completed' || data.status === 'PartiallyPlanned'
  const editable = data.isLatest && reviewable && can('shipments.plan')
  const s = data.plan.summary

  return (
    <>
      <PageHeader
        title={`${data.number} · version ${data.planVersion}`}
        description={`Planning date ${data.planningDate} · created ${formatDateTime(data.createdAt)}${data.reason ? ` · ${data.reason}` : ''}`}
        actions={
          <>
            <PlanStatusTag status={data.status} />
            <SolverStatusTag status={data.plan.solverStatus} />
            <Button icon={<DownloadOutlined />} onClick={() => void planningApi.exportRun(id, 'xlsx', data.number).catch((e: unknown) => message.error(toApiError(e).message))}>Excel</Button>
            <Button icon={<DownloadOutlined />} onClick={() => void planningApi.exportRun(id, 'pdf', data.number).catch((e: unknown) => message.error(toApiError(e).message))}>PDF</Button>
            <Button icon={<DownloadOutlined />} onClick={() => void planningApi.exportRun(id, 'csv', data.number).catch((e: unknown) => message.error(toApiError(e).message))}>CSV</Button>
            {editable && <Button onClick={() => { setOptions(data.options); setDialog('replan') }}>Re-plan…</Button>}
            {data.isLatest && reviewable && can('shipments.approve') && data.plan.vehicles.length > 0 && (
              <Button type="primary" loading={approve.isPending} onClick={() => approve.mutate()}>Approve</Button>
            )}
            {data.isLatest && data.status === 'Approved' && can('shipments.approve') && (
              <Button type="primary" loading={commit.isPending} onClick={() => commit.mutate()}>Commit to shipments</Button>
            )}
            {data.isLatest && (reviewable || data.status === 'Approved') && can('shipments.plan') && <Button danger onClick={() => setDialog('cancel')}>Cancel plan</Button>}
          </>
        }
      />

      {!data.isLatest && (
        <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="This is an older version, kept for the record. It cannot be changed."
          action={versions.data && <Link to={`/planning/runs/${versions.data[0]?.id}`}>Open latest</Link>} />
      )}
      {data.plan.solverMessage && <Alert type={data.plan.solverStatus === 'TimeLimitReached' ? 'warning' : 'info'} showIcon style={{ marginBottom: 16 }} title={data.plan.solverMessage} />}
      {editError && <Alert type="error" showIcon closable onClose={() => setEditError(null)} style={{ marginBottom: 16 }} title="That change is not allowed" description={editError} />}
      {data.status === 'Cancelled' && <Alert type="error" showIcon style={{ marginBottom: 16 }} title={`Cancelled: ${data.cancelReason ?? ''}`} />}

      <Flex wrap gap={16} style={{ marginBottom: 16 }}>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Orders planned" value={s.ordersPlanned} /></Card></div>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Unplanned" value={s.ordersUnplanned} styles={{ content: { color: s.ordersUnplanned > 0 ? '#cf1322' : undefined } }} /></Card></div>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Vehicles" value={s.vehiclesUsed} suffix={<Typography.Text type="secondary" style={{ fontSize: 12 }}>{s.ftlCount} FTL · {s.ptlCount} PTL</Typography.Text>} /></Card></div>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Total freight" value={formatInrExact(s.totalCost)} /></Card></div>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Weight / volume fill" value={`${percent(s.averageWeightUtilisation)} / ${percent(s.averageVolumeUtilisation)}`} /></Card></div>
        <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Consolidation saving" value={formatInrExact(s.consolidationSaving)} /></Card></div>
        {s.totalEmptyKm != null && (
          <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Empty km" value={`${s.totalEmptyKm.toLocaleString('en-IN')} km`} suffix={s.emptyKmPercent != null ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>{s.emptyKmPercent}% of driven</Typography.Text> : undefined} /></Card></div>
        )}
        {s.costPerTonne != null && (
          <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Cost per tonne" value={formatInrExact(s.costPerTonne)} suffix={s.costPerShipment != null ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>{formatInrExact(s.costPerShipment)}/trip</Typography.Text> : undefined} /></Card></div>
        )}
        {s.totalDistanceKm !== null && (
          <div style={{ flex: '1 1 170px' }}><Card size="small"><Statistic title="Total distance" value={`${s.totalDistanceKm.toLocaleString('en-IN')} km`} suffix={s.costPerTonneKm !== null ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>₹{s.costPerTonneKm}/t-km</Typography.Text> : undefined} /></Card></div>
        )}
      </Flex>

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          <Flex vertical gap={16}>
            {data.plan.vehicles.length === 0 && <Empty description="Nothing could be planned." />}
            {data.plan.vehicles.map((v, i) => (
              <VehicleCard
                key={v.key}
                vehicle={v}
                index={i}
                all={data.plan.vehicles}
                editable={editable}
                busy={edit.isPending}
                vehicleTypes={vehicleTypes.data ?? []}
                onEdit={requestEdit}
                locking={lock.isPending && lock.variables?.key === v.key}
                onLock={(locked, kind, orderId) => lock.mutate({ key: v.key, locked, kind, orderId })}
              />
            ))}
            {data.plan.unplanned.length > 0 && <UnplannedCard items={data.plan.unplanned} vehicles={data.plan.vehicles} editable={editable} busy={edit.isPending} onEdit={requestEdit} />}
          </Flex>
        </Col>
        <Col xs={24} xl={8}>
          <Flex vertical gap={16}>
            <Card title="Rules used" size="small">
              <Flex vertical gap={4}>
                <Typography.Text>{objectiveLabel[data.options.objective]}</Typography.Text>
                <Typography.Text type="secondary">
                  {[data.options.allowFtl && 'Full truck', data.options.allowPtl && 'Part load', data.options.allowConsolidation && 'Consolidation'].filter(Boolean).join(' · ')} · max {data.options.maxStops} stops
                </Typography.Text>
              </Flex>
            </Card>
            <PlanningLog run={data} />
            <Card title="Versions" size="small">
              <Flex vertical gap={12}>
                {versions.data?.map((v) => (
                  <Flex key={v.id} justify="space-between" align="flex-start" gap={8}>
                    <Flex vertical>
                      <Link to={`/planning/runs/${v.id}`}>{v.id === data.id ? <b>Version {v.planVersion}</b> : `Version ${v.planVersion}`}</Link>
                      <Typography.Text type="secondary">{formatDateTime(v.createdAt)} · {formatInrExact(v.summary.totalCost)}{v.reason ? ` · ${v.reason}` : ''}</Typography.Text>
                    </Flex>
                    <PlanStatusTag status={v.status} />
                  </Flex>
                ))}
              </Flex>
            </Card>
          </Flex>
        </Col>
      </Row>

      <Modal
        open={pendingEdit !== null}
        title="Why are you changing the plan?"
        okText="Save change"
        okButtonProps={{ disabled: editReason.trim().length === 0 }}
        confirmLoading={edit.isPending}
        onOk={() => pendingEdit && edit.mutate({ ...pendingEdit, comment: editReason.trim() })}
        onCancel={() => setPendingEdit(null)}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">A manual change overrides the planner. The reason is kept with the new version.</Typography.Paragraph>
        <Input.TextArea rows={3} maxLength={300} aria-label="Reason for the change" placeholder="e.g. Customer asked for these two to travel together" value={editReason} onChange={(e) => setEditReason(e.target.value)} />
      </Modal>
      <Modal
        open={dialog === 'replan'}
        title="Re-plan as a new version"
        okText="Re-plan"
        okButtonProps={{ disabled: reason.trim().length === 0 }}
        confirmLoading={replan.isPending}
        onOk={() => replan.mutate()}
        onCancel={() => { setDialog(null); setReason('') }}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">Locked vehicles stay exactly as they are. The current version is kept.</Typography.Paragraph>
        <Input.TextArea rows={2} maxLength={500} aria-label="Reason for re-planning" placeholder="Why are you re-planning?" value={reason} onChange={(e) => setReason(e.target.value)} style={{ marginBottom: 16 }} />
        {options && <PlanOptionsForm value={options} onChange={setOptions} />}
      </Modal>
      <Modal
        open={dialog === 'cancel'}
        title="Cancel this plan?"
        okText="Cancel plan"
        okButtonProps={{ danger: true, disabled: reason.trim().length === 0 }}
        confirmLoading={cancel.isPending}
        onOk={() => cancel.mutate(reason.trim())}
        onCancel={() => { setDialog(null); setReason('') }}
        destroyOnHidden
      >
        <Input.TextArea rows={3} maxLength={500} aria-label="Reason for cancelling" placeholder="Why is it being cancelled?" value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </>
  )
}
