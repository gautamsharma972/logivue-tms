import { Flex, Progress, Tag, Typography } from 'antd'
import type { AssignedDriver, AssignedTransporter, AssignedVehicle, FreightMode, OrderStatus, PodStage, PodStatus, PlanObjective, PlanOptions, PlanStatus, ShipmentStatus, SolverStatus } from '@/lib/api/types'

const orderColor: Record<OrderStatus, string> = { Open: 'blue', Planned: 'gold', Dispatched: 'cyan', Delivered: 'green', Cancelled: 'default' }

export function OrderStatusTag({ status }: { status: OrderStatus }) {
  return <Tag color={orderColor[status]}>{status}</Tag>
}

const shipmentColor: Record<ShipmentStatus, string> = {
  Draft: 'default',
  Tendered: 'gold',
  Accepted: 'blue',
  Dispatched: 'cyan',
  Delivered: 'green',
  Cancelled: 'red',
  Bidding: 'purple',
}

const shipmentLabel: Record<ShipmentStatus, string> = {
  Draft: 'Draft',
  Tendered: 'Awaiting transporter',
  Accepted: 'Accepted',
  Dispatched: 'On the road',
  Delivered: 'Delivered',
  Cancelled: 'Cancelled',
  Bidding: 'Out to tender',
}

export function ShipmentStatusTag({ status }: { status: ShipmentStatus }) {
  return <Tag color={shipmentColor[status]}>{shipmentLabel[status]}</Tag>
}

export const shipmentStatusOptions = (Object.keys(shipmentLabel) as ShipmentStatus[]).map((value) => ({ value, label: shipmentLabel[value] }))

export const modeLabel: Record<FreightMode, string> = { Ftl: 'Full truck (FTL)', Ptl: 'Part load (PTL)' }

export const percent = (fraction: number | null | undefined) => (fraction === null || fraction === undefined ? '—' : `${Math.round(fraction * 100)}%`)

/** Red when the truck went out mostly empty, green when well used. */
export function UtilizationBar({ value }: { value: number | null }) {
  if (value === null) return <>—</>
  const pct = Math.round(value * 100)
  // Colour carries the message (thin = red, healthy = green); the status icon would read as an error.
  return <Progress percent={pct} size="small" style={{ minWidth: 90 }} strokeColor={value < 0.5 ? '#d4380d' : value >= 0.8 ? '#389e0d' : '#1677ff'} />
}

export const formatKg = (kg: number) => `${new Intl.NumberFormat('en-IN', { maximumFractionDigits: 1 }).format(kg)} kg`

export const defaultPlanOptions: PlanOptions = {
  objective: 'MinimizeTotalCost',
  allowFtl: true,
  allowPtl: true,
  allowConsolidation: true,
  maxStops: 8,
  timeBudgetSeconds: 20,
  enforceDeadlines: true,
  ptlExtraTransitHours: 24,
  stopServiceMinutes: 30,
  departureHour: 8,
  allowBackhaul: true,
  backhaulChargePercent: 50,
  maxBackhaulExtraKm: 100,
  returnsAfterDeliveries: true,
}

export const objectiveLabel: Record<PlanObjective, string> = {
  MinimizeTotalCost: 'Lowest total cost',
  MinimizeVehicles: 'Fewest vehicles',
  MaximizeUtilisation: 'Highest utilisation',
  MinimizeDistance: 'Shortest distance (ranks by cost until stop sequencing)',
  BalanceCostAndUtilisation: 'Balance cost and utilisation',
}

export const objectiveOptions = (Object.keys(objectiveLabel) as PlanObjective[]).map((value) => ({ value, label: objectiveLabel[value] }))

const planStatusColor: Record<PlanStatus, string> = {
  Running: 'processing', Completed: 'green', PartiallyPlanned: 'gold', Infeasible: 'red', Approved: 'blue', Committed: 'cyan', Cancelled: 'default',
}
const planStatusLabel: Record<PlanStatus, string> = {
  Running: 'Running', Completed: 'Completed', PartiallyPlanned: 'Partially planned', Infeasible: 'Infeasible', Approved: 'Approved', Committed: 'Committed', Cancelled: 'Cancelled',
}

export function PlanStatusTag({ status }: { status: PlanStatus }) {
  return <Tag color={planStatusColor[status]}>{planStatusLabel[status]}</Tag>
}

/** The solver status is shown as it is: a rule-based plan is "Feasible", never "Optimal". */
export function SolverStatusTag({ status }: { status: SolverStatus }) {
  const color: Record<SolverStatus, string> = { Feasible: 'blue', Optimized: 'green', TimeLimitReached: 'orange', Infeasible: 'red', Failed: 'red' }
  return <Tag color={color[status]}>{status === 'TimeLimitReached' ? 'Time limit reached' : status}</Tag>
}

/** Road distance is measured; an estimate is a straight line scaled by a road factor, and must say so. */
export function RouteSourceTag({ source }: { source: 'Estimate' | 'Osrm' | null }) {
  if (!source) return null
  return source === 'Osrm' ? <Tag color="green">Road distance</Tag> : <Tag color="orange">Estimate</Tag>
}

export const formatHours = (hours: number | null | undefined) => {
  if (hours === null || hours === undefined) return '—'
  const h = Math.floor(hours)
  const m = Math.round((hours - h) * 60)
  return m === 0 ? `${h} h` : `${h} h ${m} m`
}

export const formatMinutes = (minutes: number | null | undefined) => (minutes === null || minutes === undefined ? '—' : formatHours(minutes / 60))

const podStatusColor: Record<PodStatus, string> = { Awaiting: 'default', Uploaded: 'gold', Verified: 'green', Rejected: 'red' }
const podStatusLabel: Record<PodStatus, string> = { Awaiting: 'Proof awaited', Uploaded: 'Proof to check', Verified: 'Proof verified', Rejected: 'Proof rejected' }

export function PodStatusTag({ status }: { status: PodStatus }) {
  return <Tag color={podStatusColor[status]}>{podStatusLabel[status]}</Tag>
}

const stageColor: Record<PodStage, string> = { DeliveryPending: 'cyan', AwaitingProof: 'default', ProofUploaded: 'gold', ProofRejected: 'red', ProofVerified: 'green' }
const stageLabel: Record<PodStage, string> = { DeliveryPending: 'On the road', AwaitingProof: 'Proof awaited', ProofUploaded: 'Proof to check', ProofRejected: 'Proof rejected', ProofVerified: 'Proof verified' }

export function PodStageTag({ stage }: { stage: PodStage }) {
  return <Tag color={stageColor[stage]}>{stageLabel[stage]}</Tag>
}

export const podStageOptions = (Object.keys(stageLabel) as PodStage[]).map((value) => ({ value, label: stageLabel[value] }))

const complianceColor: Record<string, string> = { Compliant: 'green', ExpiringSoon: 'orange', NonCompliant: 'red' }

function ComplianceTag({ value, issues }: { value: string; issues: string[] }) {
  return <Tag color={complianceColor[value] ?? 'default'} title={issues.join('\n')}>{value === 'ExpiringSoon' ? 'Papers expiring' : value === 'NonCompliant' ? 'Papers invalid' : 'Papers OK'}</Tag>
}

/** Who would run a trip: the transporter, the actual vehicle and the driver, with the contact details a planner needs. */
export function TransporterCell({ transporter, fallbackName }: { transporter: AssignedTransporter | null; fallbackName?: string | null }) {
  if (!transporter) {
    return <Typography.Text type={fallbackName ? undefined : 'secondary'}>{fallbackName ?? '—'}</Typography.Text>
  }
  return (
    <Flex vertical>
      <Typography.Text strong>{transporter.name}</Typography.Text>
      <Typography.Text type="secondary">{transporter.code}{transporter.city ? ` · ${transporter.city}` : ''}</Typography.Text>
      {transporter.contactPerson && <Typography.Text type="secondary">{transporter.contactPerson}</Typography.Text>}
      {transporter.phone && <Typography.Text type="secondary">{transporter.phone}</Typography.Text>}
      {transporter.email && <Typography.Text type="secondary">{transporter.email}</Typography.Text>}
    </Flex>
  )
}

/** `priced` is false for an option that was never feasible, where "no vehicle assigned" would only be noise. */
export function VehicleCell({ typeName, vehicle, priced = true }: { typeName: string | null; vehicle: AssignedVehicle | null; priced?: boolean }) {
  return (
    <Flex vertical>
      <Typography.Text strong>{typeName ?? '—'}</Typography.Text>
      {vehicle ? (
        <>
          <Typography.Text>{vehicle.registration}</Typography.Text>
          {vehicle.payloadKg !== null && <Typography.Text type="secondary">{vehicle.payloadKg.toLocaleString('en-IN')} kg payload</Typography.Text>}
          <span><ComplianceTag value={vehicle.compliance} issues={vehicle.issues} /></span>
        </>
      ) : priced ? <Typography.Text type="secondary">No vehicle assigned</Typography.Text> : null}
    </Flex>
  )
}

export function DriverCell({ driver }: { driver: AssignedDriver | null }) {
  if (!driver) {
    return <Typography.Text type="secondary">No driver assigned</Typography.Text>
  }
  return (
    <Flex vertical>
      <Typography.Text strong>{driver.name}</Typography.Text>
      {driver.phone && <Typography.Text type="secondary">{driver.phone}</Typography.Text>}
      {driver.licenseNumber && <Typography.Text type="secondary">Licence {driver.licenseNumber}</Typography.Text>}
      <span><ComplianceTag value={driver.compliance} issues={driver.issues} /></span>
    </Flex>
  )
}
