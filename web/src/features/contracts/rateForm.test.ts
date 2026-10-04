import { describe, expect, it } from 'vitest'
import type { RateInputDto } from '@/lib/api/types'
import { describePricing } from './shared'
import { toForm, toPricing } from './rateForm'

const blank = toForm(null)

describe('rate form → pricing payload', () => {
  it('chains weight slab lower limits from the previous upper limit and leaves the last slab open-ended', () => {
    const form = { ...blank, slabs: [{ toKg: 100, ratePerKg: 12 }, { toKg: 500, ratePerKg: 9 }, { toKg: null, ratePerKg: 7 }], slabMode: 'Whole' as const, slabMinCharge: 150, minChargeableKg: 20 }

    expect(toPricing('Ptl', form)).toEqual({
      kind: 'weightSlabs',
      mode: 'Whole',
      slabs: [
        { fromKg: 0, toKg: 100, ratePerKg: 12 },
        { fromKg: 100, toKg: 500, ratePerKg: 9 },
        { fromKg: 500, toKg: null, ratePerKg: 7 },
      ],
      minCharge: 150,
      minChargeableKg: 20,
    })
  })

  it('ignores a stray upper limit typed into the last slab', () => {
    const form = { ...blank, slabs: [{ toKg: 100, ratePerKg: 5 }, { toKg: 999, ratePerKg: 4 }] }

    const pricing = toPricing('Ptl', form)

    expect(pricing.kind === 'weightSlabs' && pricing.slabs[1]).toEqual({ fromKg: 100, toKg: null, ratePerKg: 4 })
  })

  it('sends the discriminator first so the API can read the payload', () => {
    expect(Object.keys(toPricing('Ftl', { ...blank, amountPerTrip: 1000 }))[0]).toBe('kind')
    expect(Object.keys(toPricing('Ptl', blank))[0]).toBe('kind')
    expect(Object.keys(toPricing('Dedicated', blank))[0]).toBe('kind')
  })

  it('builds a flat-trip or per-km rate for truck-load contracts depending on the chosen basis', () => {
    expect(toPricing('Ftl', { ...blank, ftlBasis: 'flatTrip', amountPerTrip: 42000 })).toEqual({ kind: 'flatTrip', amountPerTrip: 42000 })
    expect(toPricing('Ftl', { ...blank, ftlBasis: 'perKm', ratePerKm: 38, minKm: 200, minCharge: 9000 })).toEqual({ kind: 'perKm', ratePerKm: 38, minKm: 200, minCharge: 9000 })
  })

  it('builds a dedicated-vehicle rate', () => {
    expect(toPricing('Dedicated', { ...blank, monthlyRental: 95000, includedKmPerMonth: 3000, extraKmRate: 14, includedHoursPerMonth: 260, extraHourRate: 250 }))
      .toEqual({ kind: 'dedicated', monthlyRental: 95000, includedKmPerMonth: 3000, extraKmRate: 14, includedHoursPerMonth: 260, extraHourRate: 250 })
  })

  it('round-trips an existing rate through the form without losing anything', () => {
    const rate: RateInputDto = {
      origin: { kind: 'City', state: 'MAHARASHTRA', city: 'PUNE', zoneCode: null },
      destination: { kind: 'Zone', state: null, city: null, zoneCode: 'NORTH' },
      bothWays: true,
      vehicleTypeId: null,
      minDistanceKm: 0,
      maxDistanceKm: 500,
      pricing: { kind: 'weightSlabs', mode: 'Incremental', slabs: [{ fromKg: 0, toKg: 50, ratePerKg: 20 }, { fromKg: 50, toKg: null, ratePerKg: 15 }], minCharge: 300, minChargeableKg: 10 },
    }

    const form = toForm(rate)

    expect(form.slabs).toEqual([{ toKg: 50, ratePerKg: 20 }, { toKg: null, ratePerKg: 15 }])
    expect(toPricing('Ptl', form)).toEqual(rate.pricing)
    expect(form.bothWays).toBe(true)
    expect(form.destination.zoneCode).toBe('NORTH')
  })

  it('starts a new part-load rate with two empty slabs, the first up to 100 kg', () => {
    expect(blank.slabs).toEqual([{ toKg: 100, ratePerKg: null }, { toKg: null, ratePerKg: null }])
  })
})

describe('describePricing', () => {
  it('summarises each shape for tables', () => {
    expect(describePricing({ kind: 'flatTrip', amountPerTrip: 42000 })).toContain('42,000')
    expect(describePricing({ kind: 'perKm', ratePerKm: 38, minKm: 200, minCharge: 0 })).toBe('₹38/km (min 200 km)')
    expect(describePricing({ kind: 'weightSlabs', mode: 'Whole', slabs: [{ fromKg: 0, toKg: 100, ratePerKg: 12 }, { fromKg: 100, toKg: null, ratePerKg: 7 }], minCharge: 0, minChargeableKg: 0 })).toBe('2 weight slabs · ₹7–12/kg')
    expect(describePricing({ kind: 'dedicated', monthlyRental: 95000, includedKmPerMonth: 3000, extraKmRate: 14, includedHoursPerMonth: 0, extraHourRate: 0 })).toContain('3000 km incl.')
  })
})
