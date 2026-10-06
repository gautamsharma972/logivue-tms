import { Tag, Tooltip } from 'antd'
import dayjs from 'dayjs'
import type { RiskStatus, TrackAlertType, TrackExceptionStatus, TrackingExecution, TrackingHealth, TrackSeverity } from '@/lib/api/types'

export const healthColour: Record<TrackingHealth, string> = { NotStarted: 'default', Healthy: 'green', Stale: 'orange', Lost: 'red', Completed: 'blue' }
export const healthLabel: Record<TrackingHealth, string> = { NotStarted: 'Not started', Healthy: 'Live', Stale: 'Stale', Lost: 'Tracking lost', Completed: 'Completed' }
export const riskColour: Record<RiskStatus, string> = { Unknown: 'default', OnTime: 'green', AtRisk: 'gold', Delayed: 'orange', SeverelyDelayed: 'red' }
export const riskLabel: Record<RiskStatus, string> = { Unknown: 'Unknown', OnTime: 'On time', AtRisk: 'At risk', Delayed: 'Delayed', SeverelyDelayed: 'Severely delayed' }
export const severityColour: Record<TrackSeverity, string> = { Informational: 'blue', Warning: 'gold', High: 'orange', Critical: 'red' }

/** The pin colour on the map: tracking trouble outranks lateness, because a lost phone means we do not know the lateness. */
export const pinColour = (health: TrackingHealth, risk: RiskStatus): string =>
  health === 'Lost' ? '#cf1322' : health === 'Stale' ? '#fa8c16' : health === 'Completed' ? '#8c8c8c' : health === 'NotStarted' ? '#bfbfbf' : risk === 'SeverelyDelayed' ? '#cf1322' : risk === 'Delayed' ? '#fa8c16' : risk === 'AtRisk' ? '#d4b106' : '#389e0d'

const words = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')
export const executionLabel = (e: TrackingExecution) => words(e)
export const alertTypeLabel = (t: TrackAlertType) => words(t)
export const exceptionStatusLabel = (s: TrackExceptionStatus) => words(s)

export const HealthTag = ({ health }: { health: TrackingHealth }) => <Tag color={healthColour[health]}>{healthLabel[health]}</Tag>
export const RiskTag = ({ risk }: { risk: RiskStatus }) => <Tag color={riskColour[risk]}>{riskLabel[risk]}</Tag>
export const SeverityTag = ({ severity }: { severity: TrackSeverity }) => <Tag color={severityColour[severity]}>{severity}</Tag>

export const clock = (iso: string | null | undefined) => (iso ? dayjs(iso).format('DD MMM, HH:mm') : 'Not available')

export function Delay({ minutes }: { minutes: number }) {
  if (minutes <= 0) return <span>On schedule</span>
  const text = minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min late` : `${minutes} min late`
  return <span style={{ color: minutes > 30 ? '#cf1322' : '#d48806' }}>{text}</span>
}

export function Age({ minutes }: { minutes: number | null | undefined }) {
  if (minutes == null) return <span>No location yet</span>
  const text = minutes < 1 ? 'just now' : minutes < 60 ? `${minutes} min ago` : `${Math.floor(minutes / 60)} h ${minutes % 60} min ago`
  return <Tooltip title="Time since the last GPS fix"><span>{text}</span></Tooltip>
}

export const km = (v: number | null | undefined) => (v == null ? 'Not available' : `${Math.round(v * 10) / 10} km`)
