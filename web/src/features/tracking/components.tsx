import { Alert, Button, Card, Descriptions, Flex, Input, Progress, Select, Tag } from 'antd'
import type { JourneyAnalyticsDto, RiskStatus, TrackedDetailDto, TrackedSummaryDto, TrackExceptionSummaryDto, TrackEtaDto, TrackingExecution, TrackingHealth, TrackingHealthDto, TrackRouteDto, ListTrackedParams } from '@/lib/api/types'
import { Age, alertTypeLabel, clock, Delay, executionLabel, HealthTag, healthLabel, km, RiskTag, riskLabel, SeverityTag } from './shared'

/** Where the trip is in its life (not whether it is late, and not whether tracking works). */
export const ShipmentStatusBadge = ({ execution }: { execution: TrackingExecution }) => <Tag>{executionLabel(execution)}</Tag>

/** Is the phone reporting? */
export const TrackingHealthBadge = HealthTag

/** Will it be late, by how much, and how sure are we. */
export function EtaRiskIndicator({ risk, delayMinutes, confidence }: { risk: RiskStatus; delayMinutes: number; confidence?: number | null }) {
  return (
    <Flex gap={6} align="center" wrap>
      <RiskTag risk={risk} />
      <Delay minutes={delayMinutes} />
      {confidence != null && <span style={{ fontSize: 12, opacity: 0.7 }}>confidence {Math.round(confidence * 100)}%</span>}
    </Flex>
  )
}

/** Planned, calculated and shown arrival side by side, so an operator's correction is never mistaken for the system's estimate. */
export function EtaCard({ eta, title = 'Arrival' }: { eta: Pick<TrackEtaDto, 'plannedAt' | 'systemEtaAt' | 'etaAt' | 'overridden' | 'overrideReason' | 'risk' | 'delayMinutes' | 'confidence' | 'calculationVersion'>; title?: string }) {
  return (
    <Card size="small" title={title}>
      <Descriptions column={1} size="small">
        <Descriptions.Item label="Planned">{clock(eta.plannedAt)}</Descriptions.Item>
        <Descriptions.Item label="Calculated">{clock(eta.systemEtaAt)}</Descriptions.Item>
        {eta.overridden && <Descriptions.Item label="Set by an operator">{clock(eta.etaAt)}{eta.overrideReason ? ` · ${eta.overrideReason}` : ''}</Descriptions.Item>}
        <Descriptions.Item label="Risk"><EtaRiskIndicator risk={eta.risk} delayMinutes={eta.delayMinutes} confidence={eta.confidence} /></Descriptions.Item>
        <Descriptions.Item label="Method">{eta.calculationVersion} (rule-based)</Descriptions.Item>
      </Descriptions>
    </Card>
  )
}

export function RouteProgress({ progressPct, travelledKm, remainingKm, plannedKm, risk, tracking }: { progressPct: number | null; travelledKm: number; remainingKm: number | null; plannedKm: number | null; risk: RiskStatus; tracking: TrackingHealth }) {
  return (
    <div>
      <Progress percent={Math.round(progressPct ?? 0)} status={risk === 'SeverelyDelayed' || tracking === 'Lost' ? 'exception' : 'active'} />
      <span style={{ fontSize: 12 }}>{km(travelledKm)} travelled · {km(remainingKm)} to go · plan {km(plannedKm)}</span>
    </div>
  )
}

/** Shown only while the vehicle is off its route, with how far and for how long. */
export function RouteDeviationBanner({ route }: { route: Pick<TrackRouteDto, 'deviations'> | undefined }) {
  const open = route?.deviations.find((d) => d.status === 'Open')
  if (!open) return null
  return <Alert type="error" showIcon message={`Off route: ${Math.round(open.distanceFromRouteKm * 10) / 10} km from the planned road for ${open.durationMinutes} min`} description={open.reason ? `Reason given: ${open.reason}${open.reasonNote ? `, ${open.reasonNote}` : ''}` : 'No reason recorded yet.'} />
}

/** A stop that is running long, or one outside any planned place. */
export function DwellIndicator({ analytics }: { analytics: JourneyAnalyticsDto | undefined }) {
  const now = analytics?.dwells.find((d) => d.status === 'Ongoing')
  if (!now) return null
  const excess = now.excessDurationMinutes
  return (
    <Alert type={excess > 0 ? 'warning' : 'info'} showIcon
      message={`${now.kind === 'UnplannedStop' ? 'Stopped away from any planned stop' : `At ${now.place ?? 'a stop'}`}: ${now.durationMinutes} min`}
      description={excess > 0 ? `${excess} min longer than the ${now.expectedDurationMinutes} min expected.` : `${now.expectedDurationMinutes} min expected.`} />
  )
}

/** Open gaps in tracking and how long they have run. */
export function TrackingGapIndicator({ health }: { health: Pick<TrackingHealthDto, 'gaps' | 'health' | 'ageMinutes'> | undefined }) {
  const open = health?.gaps.find((g) => g.gapEnd == null)
  if (!open && health?.health !== 'Stale' && health?.health !== 'Lost') return null
  return <Alert type={health?.health === 'Lost' ? 'error' : 'warning'} showIcon message={`No location for ${open?.durationMinutes ?? health?.ageMinutes ?? '?'} min`} description="The vehicle may still be moving: tracking, not the truck, has stopped reporting." />
}

/** Exceptions needing a person, newest first. */
export function ExceptionPanel({ exceptions, onOpen }: { exceptions: TrackExceptionSummaryDto[]; onOpen?: (id: string) => void }) {
  if (exceptions.length === 0) return null
  return (
    <Card size="small" title="Open exceptions">
      <Flex vertical gap={6}>
        {exceptions.map((e) => (
          <Flex key={e.id} gap={8} align="center" wrap>
            <SeverityTag severity={e.severity} /><strong>{e.number}</strong><span>{alertTypeLabel(e.type)}</span>
            {e.overdue && <Tag color="red">Overdue</Tag>}
            {onOpen && <Button size="small" type="link" onClick={() => onOpen(e.id)}>Open</Button>}
          </Flex>
        ))}
      </Flex>
    </Card>
  )
}

/** The facts an operator wants when a pin is clicked. */
export function ShipmentTrackingPanel({ trip }: { trip: TrackedSummaryDto }) {
  return (
    <Descriptions column={1} size="small" bordered>
      <Descriptions.Item label="Lane">{trip.origin ?? '—'} → {trip.destination ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Carrier">{trip.transporterReference ?? '—'}</Descriptions.Item>
      <Descriptions.Item label="Driver">{trip.driverName ?? '—'}{trip.driverPhone ? ` · ${trip.driverPhone}` : ''}</Descriptions.Item>
      <Descriptions.Item label="Stage"><ShipmentStatusBadge execution={trip.execution} /></Descriptions.Item>
      <Descriptions.Item label="Tracking"><TrackingHealthBadge health={trip.tracking} /> <Age minutes={trip.minutesSinceLastLocation} /></Descriptions.Item>
      <Descriptions.Item label="Delivery risk"><EtaRiskIndicator risk={trip.risk} delayMinutes={trip.delayMinutes} confidence={trip.etaConfidence} /></Descriptions.Item>
      <Descriptions.Item label="Planned arrival">{clock(trip.plannedArrivalAt)}</Descriptions.Item>
      <Descriptions.Item label="Expected arrival">{clock(trip.etaAt)}{trip.etaOverridden ? ' (set by an operator)' : ''}</Descriptions.Item>
      <Descriptions.Item label="Progress">{trip.progressPct == null ? 'Not available' : `${Math.round(trip.progressPct)}%`} · {km(trip.remainingKm)} to go</Descriptions.Item>
      <Descriptions.Item label="Speed">{trip.speedKph == null ? 'Not available' : `${Math.round(trip.speedKph)} km/h`}{trip.moving ? '' : ' (stopped)'}</Descriptions.Item>
      <Descriptions.Item label="Route">{trip.onRoute ? 'On route' : <Tag color="red">Off route</Tag>}</Descriptions.Item>
      <Descriptions.Item label="Exceptions">{trip.openExceptions}</Descriptions.Item>
    </Descriptions>
  )
}

/** The control tower's search and filters. The tiles above it set the same filters. */
export function ControlTowerFilters({ value, onChange }: { value: ListTrackedParams; onChange: (next: ListTrackedParams) => void }) {
  return (
    <Flex gap={8} wrap style={{ marginBottom: 12 }}>
      <Input.Search allowClear placeholder="Shipment, trip, vehicle or driver" style={{ width: 260 }} onSearch={(search) => onChange({ ...value, search: search || undefined })} />
      <Input.Search allowClear placeholder="Customer" style={{ width: 160 }} onSearch={(customer) => onChange({ ...value, customer: customer || undefined })} />
      <Input.Search allowClear placeholder="Origin" style={{ width: 130 }} onSearch={(origin) => onChange({ ...value, origin: origin || undefined })} />
      <Input.Search allowClear placeholder="Destination" style={{ width: 130 }} onSearch={(destination) => onChange({ ...value, destination: destination || undefined })} />
      <Select<TrackingHealth> allowClear placeholder="Tracking" style={{ width: 150 }} value={value.tracking} onChange={(tracking) => onChange({ ...value, tracking })} options={(Object.keys(healthLabel) as TrackingHealth[]).map((v) => ({ value: v, label: healthLabel[v] }))} />
      <Select<RiskStatus> allowClear placeholder="Risk" style={{ width: 160 }} value={value.risk} onChange={(risk) => onChange({ ...value, risk })} options={(Object.keys(riskLabel) as RiskStatus[]).map((v) => ({ value: v, label: riskLabel[v] }))} />
      <Select allowClear placeholder="Exceptions" style={{ width: 150 }} value={value.hasException} onChange={(hasException) => onChange({ ...value, hasException })} options={[{ value: true, label: 'With exceptions' }, { value: false, label: 'Without' }]} />
      <Button onClick={() => onChange({ activeOnly: true, pageSize: value.pageSize })}>Reset</Button>
    </Flex>
  )
}

export type { TrackedDetailDto }
