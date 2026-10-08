import dayjs from 'dayjs'
import type { FieldType, KpiCard, ReportCell } from './types'

const inr = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 })
const num = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 })

/** A cell as text, by what kind of value its column holds. Nothing to show is a dash, never a zero. */
export function formatCell(value: ReportCell | undefined, type: FieldType | string): string {
  if (value === null || value === undefined || value === '') return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'string') {
    if (type === 'Date' && /^\d{4}-\d{2}-\d{2}/.test(value)) return dayjs(value).format('DD MMM YYYY')
    if (type === 'DateTime' && /^\d{4}-\d{2}-\d{2}T/.test(value)) return dayjs(value).format('DD MMM YYYY, HH:mm')
    return value
  }
  switch (type) {
    case 'Currency':
      return `₹ ${inr.format(value)}`
    case 'Percent':
      return `${num.format(value)}%`
    case 'Whole':
      return inr.format(value)
    case 'Duration':
      return `${inr.format(value)} min`
    default:
      return num.format(value)
  }
}

export function formatKpi(card: Pick<KpiCard, 'unit' | 'value' | 'measurable'>): string {
  if (!card.measurable || card.value === null) return 'Not measurable'
  switch (card.unit) {
    case 'Currency':
      if (Math.abs(card.value) < 100) return `₹ ${num.format(card.value)}`
      return card.value >= 10_000_000 ? `₹ ${num.format(card.value / 10_000_000)} Cr` : card.value >= 100_000 ? `₹ ${num.format(card.value / 100_000)} L` : `₹ ${inr.format(card.value)}`
    case 'Percent':
      return `${num.format(card.value)}%`
    case 'Minutes':
      return `${num.format(card.value)} min`
    case 'Count':
      return inr.format(card.value)
    default:
      return num.format(card.value)
  }
}

export function formatChange(card: KpiCard): string | null {
  if (card.change === null) return null
  const sign = card.change > 0 ? '+' : ''
  const unit = card.unit === 'Percent' ? ' pts' : card.unit === 'Currency' ? '' : ''
  const body = card.unit === 'Currency' ? `${sign}${Math.abs(card.change) < 100 ? num.format(card.change) : inr.format(card.change)}` : `${sign}${num.format(card.change)}${unit}`
  return card.changePct === null ? body : `${body} (${sign}${num.format(card.changePct)}%)`
}

export function humanise(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/_/g, ' ')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

/** Static choices for filters whose values are a fixed list rather than read from the data. */
export const STATIC_OPTIONS: Record<string, string[]> = {
  unplannedReason: ['NoVehicle', 'PayloadExceeded', 'VolumeExceeded', 'SlaImpossible', 'NoCompatibleVehicle', 'NoRate', 'NoRoute', 'LockedConflict', 'Other'],
  tenderOutcome: ['Accepted', 'Rejected', 'Expired', 'Withdrawn', 'Pending'],
  placementOutcome: ['OnTime', 'Late', 'NoShow', 'Replaced'],
  deliveryStatus: ['Delivered', 'PartiallyDelivered', 'Failed', 'Refused', 'InTransit', 'Pending'],
  podStatus: ['Pending', 'Submitted', 'Accepted', 'Rejected', 'ResubmissionRequired'],
  risk: ['OnTime', 'AtRisk', 'Delayed', 'SeverelyDelayed'],
  trackingHealth: ['Healthy', 'Stale', 'Lost', 'NotStarted', 'Completed'],
  dwellKind: ['Origin', 'Customer', 'Hub', 'Unplanned'],
  contractStatus: ['Active', 'Draft', 'PendingApproval', 'Suspended', 'Expired'],
}

/** A coloured tag for words that carry good, bad or warning meaning. */
export function toneOf(text: string): 'success' | 'error' | 'warning' | 'processing' | 'default' {
  const t = text.toLowerCase()
  if (['delivered', 'accepted', 'ontime', 'healthy', 'completed', 'active', 'resolved', 'yes', 'lane', 'low cost / high performance', 'good'].includes(t)) return 'success'
  if (['failed', 'refused', 'rejected', 'lost', 'delayed', 'severelydelayed', 'critical', 'noshow', 'expired', 'none', 'no', 'high cost / low performance', 'bad', 'open'].includes(t)) return 'error'
  if (['late', 'atrisk', 'stale', 'warning', 'high', 'partiallydelivered', 'pending', 'submitted', 'resubmissionrequired', 'fallback', 'zone', 'replaced', 'inprogress'].includes(t)) return 'warning'
  if (['intransit', 'queued', 'running'].includes(t)) return 'processing'
  return 'default'
}
