import { Tag } from 'antd'
import type { ContractStatus, ContractType, Pricing } from '@/lib/api/types'
import { formatInr } from '@/lib/format'

const statusColor: Record<ContractStatus, string> = {
  Draft: 'default',
  PendingApproval: 'gold',
  Active: 'green',
  Rejected: 'red',
  Terminated: 'volcano',
  Superseded: 'purple',
  Expired: 'default',
  Suspended: 'orange',
  Cancelled: 'default',
}

const statusLabel: Record<ContractStatus, string> = {
  Draft: 'Draft',
  PendingApproval: 'Pending approval',
  Active: 'Active',
  Rejected: 'Rejected',
  Terminated: 'Terminated',
  Superseded: 'Superseded',
  Expired: 'Expired',
  Suspended: 'Suspended',
  Cancelled: 'Cancelled',
}

export function ContractStatusTag({ status }: { status: ContractStatus }) {
  return <Tag color={statusColor[status]}>{statusLabel[status]}</Tag>
}

export const typeLabel: Record<ContractType, string> = { Ftl: 'Full truck load', Ptl: 'Part load', Dedicated: 'Dedicated vehicle' }

export function ContractTypeTag({ type }: { type: ContractType }) {
  return <Tag variant="filled">{type.toUpperCase()}</Tag>
}

/** One-line description of a rate, for tables. */
export function describePricing(p: Pricing): string {
  switch (p.kind) {
    case 'flatTrip':
      return `${formatInr(p.amountPerTrip)} per trip`
    case 'perKm':
      return `₹${p.ratePerKm}/km${p.minKm ? ` (min ${p.minKm} km)` : ''}${p.minCharge ? `, min ${formatInr(p.minCharge)}` : ''}`
    case 'weightSlabs': {
      const first = p.slabs[0]
      const last = p.slabs[p.slabs.length - 1]
      return `${p.slabs.length} weight slab${p.slabs.length === 1 ? '' : 's'}${first && last ? ` · ₹${last.ratePerKg}–${first.ratePerKg}/kg` : ''}`
    }
    case 'dedicated':
      return `${formatInr(p.monthlyRental)}/month · ${p.includedKmPerMonth} km incl.`
    case 'slabRate': {
      if (p.slabs.length === 1) return p.slabs[0]!.type === 'Fixed' ? `${formatInr(p.slabs[0]!.rate)} fixed` : `₹${p.slabs[0]!.rate}/${p.unit.toLowerCase()}`
      return `${p.slabs.length} ${p.dimension.toLowerCase()} slabs · ${p.method === 'BaseExcess' ? 'base + excess' : p.method.toLowerCase()}`
    }
  }
}

export const expiryColor = (days: number | null) => (days === null ? 'default' : days < 0 ? 'red' : days <= 15 ? 'volcano' : days <= 30 ? 'gold' : 'green')
