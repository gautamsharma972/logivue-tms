import { Tag, Tooltip } from 'antd'
import type { ComplianceDto, ExpiryStatus, TransporterStatus } from '@/lib/api/types'

const statusColor: Record<TransporterStatus, string> = {
  Draft: 'default',
  PendingApproval: 'gold',
  Active: 'green',
  Rejected: 'red',
  Suspended: 'volcano',
}

const statusLabel: Record<TransporterStatus, string> = {
  Draft: 'Draft',
  PendingApproval: 'Pending approval',
  Active: 'Active',
  Rejected: 'Rejected',
  Suspended: 'Suspended',
}

export function TransporterStatusTag({ status }: { status: TransporterStatus }) {
  return <Tag color={statusColor[status]}>{statusLabel[status]}</Tag>
}

const complianceColor = { Compliant: 'green', ExpiringSoon: 'gold', NonCompliant: 'red' } as const
const complianceLabel = { Compliant: 'Compliant', ExpiringSoon: 'Expiring soon', NonCompliant: 'Not compliant' } as const

/** Shows whether papers are in order; hover lists exactly what is wrong. */
export function ComplianceTag({ compliance }: { compliance: ComplianceDto }) {
  const tag = <Tag color={complianceColor[compliance.status]}>{complianceLabel[compliance.status]}</Tag>
  if (compliance.issues.length === 0) return tag
  return (
    <Tooltip title={<div>{compliance.issues.map((i) => <div key={i}>{i}</div>)}</div>}>
      {tag}
    </Tooltip>
  )
}

const expiryColor: Record<ExpiryStatus, string> = { NoExpiry: 'default', Valid: 'green', ExpiringSoon: 'gold', Expired: 'red' }
const expiryLabel: Record<ExpiryStatus, string> = { NoExpiry: 'No expiry', Valid: 'Valid', ExpiringSoon: 'Expiring soon', Expired: 'Expired' }

export function ExpiryTag({ status }: { status: ExpiryStatus }) {
  return <Tag color={expiryColor[status]}>{expiryLabel[status]}</Tag>
}
