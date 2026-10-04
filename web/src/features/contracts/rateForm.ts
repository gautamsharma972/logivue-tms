import type { ContractType, PlaceDto, Pricing, RateInputDto, SlabMode, WeightSlab } from '@/lib/api/types'
import { anywhere } from './PlaceInput'

export interface SlabRow {
  toKg: number | null
  ratePerKg: number | null
}

export interface RateForm {
  origin: PlaceDto
  destination: PlaceDto
  bothWays: boolean
  vehicleTypeId?: string
  minDistanceKm: number | null
  maxDistanceKm: number | null
  ftlBasis: 'flatTrip' | 'perKm'
  amountPerTrip: number | null
  ratePerKm: number | null
  minKm: number | null
  minCharge: number | null
  slabMode: SlabMode
  slabs: SlabRow[]
  slabMinCharge: number | null
  minChargeableKg: number | null
  monthlyRental: number | null
  includedKmPerMonth: number | null
  extraKmRate: number | null
  includedHoursPerMonth: number | null
  extraHourRate: number | null
}


export function toForm(rate: RateInputDto | null): RateForm {
  const p = rate?.pricing
  return {
    origin: rate?.origin ?? anywhere,
    destination: rate?.destination ?? anywhere,
    bothWays: rate?.bothWays ?? false,
    vehicleTypeId: rate?.vehicleTypeId ?? undefined,
    minDistanceKm: rate?.minDistanceKm ?? null,
    maxDistanceKm: rate?.maxDistanceKm ?? null,
    ftlBasis: p?.kind === 'perKm' ? 'perKm' : 'flatTrip',
    amountPerTrip: p?.kind === 'flatTrip' ? p.amountPerTrip : null,
    ratePerKm: p?.kind === 'perKm' ? p.ratePerKm : null,
    minKm: p?.kind === 'perKm' ? p.minKm : 0,
    minCharge: p?.kind === 'perKm' ? p.minCharge : 0,
    slabMode: p?.kind === 'weightSlabs' ? p.mode : 'Whole',
    slabs: p?.kind === 'weightSlabs' ? p.slabs.map((s) => ({ toKg: s.toKg, ratePerKg: s.ratePerKg })) : [{ toKg: 100, ratePerKg: null }, { toKg: null, ratePerKg: null }],
    slabMinCharge: p?.kind === 'weightSlabs' ? p.minCharge : 0,
    minChargeableKg: p?.kind === 'weightSlabs' ? p.minChargeableKg : 0,
    monthlyRental: p?.kind === 'dedicated' ? p.monthlyRental : null,
    includedKmPerMonth: p?.kind === 'dedicated' ? p.includedKmPerMonth : null,
    extraKmRate: p?.kind === 'dedicated' ? p.extraKmRate : 0,
    includedHoursPerMonth: p?.kind === 'dedicated' ? p.includedHoursPerMonth : 0,
    extraHourRate: p?.kind === 'dedicated' ? p.extraHourRate : 0,
  }
}

export function toPricing(type: ContractType, v: RateForm): Pricing {
  if (type === 'Ptl') {
    let from = 0
    const slabs: WeightSlab[] = v.slabs.map((row, i) => {
      const last = i === v.slabs.length - 1
      const slab = { fromKg: from, toKg: last ? null : row.toKg, ratePerKg: row.ratePerKg ?? 0 }
      from = row.toKg ?? from
      return slab
    })
    return { kind: 'weightSlabs', mode: v.slabMode, slabs, minCharge: v.slabMinCharge ?? 0, minChargeableKg: v.minChargeableKg ?? 0 }
  }
  if (type === 'Dedicated') {
    return {
      kind: 'dedicated',
      monthlyRental: v.monthlyRental ?? 0,
      includedKmPerMonth: v.includedKmPerMonth ?? 0,
      extraKmRate: v.extraKmRate ?? 0,
      includedHoursPerMonth: v.includedHoursPerMonth ?? 0,
      extraHourRate: v.extraHourRate ?? 0,
    }
  }
  return v.ftlBasis === 'perKm'
    ? { kind: 'perKm', ratePerKm: v.ratePerKm ?? 0, minKm: v.minKm ?? 0, minCharge: v.minCharge ?? 0 }
    : { kind: 'flatTrip', amountPerTrip: v.amountPerTrip ?? 0 }
}

