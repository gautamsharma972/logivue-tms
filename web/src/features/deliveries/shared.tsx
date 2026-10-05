import { CheckCircleFilled, ExclamationCircleFilled, InfoCircleFilled, WarningFilled } from '@ant-design/icons'
import { Tag, Typography } from 'antd'
import type {
  DeliveryExceptionSeverity, DeliveryExceptionStatus, DeliveryExceptionType, DeliveryOutcome, DeliveryStatus, GeofenceStatus, OcrFieldStatus, OcrStatus, ProofStatus, ValidationOutcome,
} from '@/lib/api/types'

export const deliveryStatusLabel: Record<DeliveryStatus, string> = {
  Planned: 'Planned', Assigned: 'Assigned', EnRoute: 'En route', Arrived: 'Arrived', Attempted: 'Attempted', Delivered: 'Delivered', PartiallyDelivered: 'Partially delivered',
  Refused: 'Refused', Failed: 'Failed', Closed: 'Closed', Cancelled: 'Cancelled',
}

const deliveryColor: Record<DeliveryStatus, string> = {
  Planned: 'default', Assigned: 'blue', EnRoute: 'cyan', Arrived: 'geekblue', Attempted: 'gold', Delivered: 'green', PartiallyDelivered: 'orange', Refused: 'red', Failed: 'red', Closed: 'default', Cancelled: 'default',
}

export function DeliveryStatusTag({ status }: { status: DeliveryStatus }) {
  return <Tag color={deliveryColor[status]}>{deliveryStatusLabel[status]}</Tag>
}

export const outcomeLabel: Record<DeliveryOutcome, string> = {
  Full: 'Delivered in full', Partial: 'Partially delivered', Shortage: 'Shortage', Damaged: 'Damaged', Refused: 'Customer refused', Failed: 'Delivery failed',
}

export const proofStatusLabel: Record<ProofStatus, string> = {
  Pending: 'No proof yet', Draft: 'Being prepared', Captured: 'Ready to submit', Submitted: 'Submitted', UnderReview: 'Under review', Accepted: 'Accepted', Rejected: 'Rejected',
  ResubmissionRequired: 'Resubmission needed', Cancelled: 'Cancelled',
}

const proofColor: Record<ProofStatus, string> = {
  Pending: 'default', Draft: 'default', Captured: 'blue', Submitted: 'gold', UnderReview: 'purple', Accepted: 'green', Rejected: 'red', ResubmissionRequired: 'orange', Cancelled: 'default',
}

export function ProofStatusTag({ status }: { status: ProofStatus }) {
  return <Tag color={proofColor[status]}>{proofStatusLabel[status]}</Tag>
}

export const proofStatusOptions = (Object.keys(proofStatusLabel) as ProofStatus[]).map((value) => ({ value, label: proofStatusLabel[value] }))

export const deliveryStatusOptions = (Object.keys(deliveryStatusLabel) as DeliveryStatus[]).map((value) => ({ value, label: deliveryStatusLabel[value] }))

export const exceptionTypeLabel: Record<DeliveryExceptionType, string> = {
  Shortage: 'Shortage', Damage: 'Damage', CustomerRefusal: 'Customer refusal', DeliveryFailed: 'Delivery failed', LateDelivery: 'Late delivery', AddressIssue: 'Address issue', PodMissing: 'POD missing',
  PodRejected: 'POD rejected', QuantityMismatch: 'Quantity mismatch', GpsException: 'Location outside geofence', SignatureMissing: 'Signature missing', OcrValidationFailed: 'Paper POD does not match',
  DuplicatePod: 'Duplicate POD', PartialDelivery: 'Partial delivery',
}

export const exceptionTypeOptions = (Object.keys(exceptionTypeLabel) as DeliveryExceptionType[]).map((value) => ({ value, label: exceptionTypeLabel[value] }))

export const exceptionStatusLabel: Record<DeliveryExceptionStatus, string> = {
  Open: 'Open', Acknowledged: 'Acknowledged', UnderInvestigation: 'Under investigation', ActionRequired: 'Action required', Resolved: 'Resolved', Closed: 'Closed', Escalated: 'Escalated',
}

export const exceptionStatusOptions = (Object.keys(exceptionStatusLabel) as DeliveryExceptionStatus[]).map((value) => ({ value, label: exceptionStatusLabel[value] }))

const severityColor: Record<DeliveryExceptionSeverity, string> = { Low: 'default', Medium: 'gold', High: 'orange', Critical: 'red' }

export function SeverityTag({ severity }: { severity: DeliveryExceptionSeverity }) {
  return <Tag color={severityColor[severity]}>{severity}</Tag>
}

export function ExceptionStatusTag({ status }: { status: DeliveryExceptionStatus }) {
  const color = status === 'Escalated' ? 'red' : status === 'Resolved' || status === 'Closed' ? 'green' : status === 'Open' ? 'gold' : 'blue'
  return <Tag color={color}>{exceptionStatusLabel[status]}</Tag>
}

export const geofenceLabel: Record<GeofenceStatus, string> = {
  NotApplicable: 'Not applicable', Inside: 'Inside the geofence', Outside: 'Outside the geofence', GpsUnavailable: 'No location', AccuracyInsufficient: 'Location too vague',
}

/** A check's result as an icon and word, never colour alone. */
export function ValidationMark({ status }: { status: ValidationOutcome }) {
  switch (status) {
    case 'Valid':
      return <span><CheckCircleFilled style={{ color: '#52c41a' }} /> OK</span>
    case 'Warning':
      return <span><InfoCircleFilled style={{ color: '#faad14' }} /> Note</span>
    case 'RequiresReview':
      return <span><WarningFilled style={{ color: '#fa8c16' }} /> Review</span>
    default:
      return <span><ExclamationCircleFilled style={{ color: '#f5222d' }} /> Fails</span>
  }
}

export function OcrFieldMark({ status }: { status: OcrFieldStatus }) {
  switch (status) {
    case 'Matched':
      return <span><CheckCircleFilled style={{ color: '#52c41a' }} /> Matches</span>
    case 'Mismatch':
      return <span><ExclamationCircleFilled style={{ color: '#f5222d' }} /> Differs</span>
    case 'LowConfidence':
      return <span><WarningFilled style={{ color: '#fa8c16' }} /> Unsure</span>
    default:
      return <Typography.Text type="secondary">Not compared</Typography.Text>
  }
}

export const ocrStatusLabel: Record<OcrStatus, string> = { Queued: 'Waiting to be read', Processing: 'Being read', Completed: 'Read', Failed: 'Could not be read' }

export const qty = (value: number | null | undefined) => (value == null ? '—' : Number(value).toLocaleString('en-IN', { maximumFractionDigits: 3 }))

export const pct = (value: number | null | undefined) => (value == null ? '—' : `${Math.round(value * 100)}%`)
