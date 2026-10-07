import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { freightApi } from '@/lib/api/endpoints'
import type { ContractDashboardDto, DphOverviewDto, ImportBatchDto, RatingDetailDto, RatingOptionDto, RatingResultDto, RatingSummaryDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { ContractDashboardPage } from './ContractDashboardPage'
import { DphManagementPage } from './DphManagementPage'
import { RateImportPage } from './RateImportPage'
import { RatingHistoryPage } from './RatingHistoryPage'
import { RatingResultView } from './ratingParts'
import { RateSimulatorPage } from './RateSimulatorPage'

const auth = vi.hoisted(() => ({ permissions: new Set<string>() }))

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: null, can: (p: string) => auth.permissions.has(p), status: 'authenticated', login: vi.fn(), logout: vi.fn() }),
  Can: ({ permission, children }: { permission: string; children: React.ReactNode }) => (auth.permissions.has(permission) ? children : null),
}))

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  contractsApi: { vehicleTypes: vi.fn().mockResolvedValue([]) },
  transportersApi: { list: vi.fn().mockResolvedValue({ items: [] }) },
  freightApi: {
    dashboard: vi.fn(), expiry: vi.fn(), coverage: vi.fn(), validation: vi.fn(), usage: vi.fn(), reports: vi.fn().mockResolvedValue([]), downloadReport: vi.fn(),
    ratings: vi.fn(), rating: vi.fn(), reproduce: vi.fn(), override: vi.fn(), clearOverride: vi.fn(), dph: vi.fn(), priceIndex: vi.fn().mockResolvedValue([]), calculateDph: vi.fn(),
    imports: vi.fn().mockResolvedValue([]), upload: vi.fn(), import: vi.fn(), correctRow: vi.fn(), applyImport: vi.fn(), discardImport: vi.fn(), template: vi.fn(), simulate: vi.fn(), calculate: vi.fn(),
  },
}))

const option = (over: Partial<RatingOptionDto> = {}): RatingOptionDto => ({
  contractId: 'c1', contractReference: 'CNT-ABC-2026', contractRevision: 3, transporterId: 't1', transporterName: 'ABC Logistics', rate: { id: 'r1', code: 'RATE-MUM-PUN-32FT', version: 4, lane: 'Mumbai → Pune', priority: 100, service: 'Ftl' },
  dphRule: 'DPH', dphVersion: 4, baseFreight: 38000, dphAdjustment: 1520, accessorialAmount: 1800, discountAmount: 0, totalFreight: 41320, currency: 'INR', transitSlaMinutes: 2160, contractValidFrom: '2026-10-01', contractValidTo: '2027-09-30',
  lines: [
    { sequence: 1, type: 'BASE_FREIGHT', description: 'Flat rate for the trip', quantity: null, unit: null, rate: null, amount: 38000, reference: null },
    { sequence: 2, type: 'DPH', description: 'DPH +4%', quantity: null, unit: null, rate: 4, amount: 1520, reference: 'DPH V4' },
    { sequence: 3, type: 'TOLL', description: 'Toll: passed through at cost', quantity: 1800, unit: 'TRIP', rate: null, amount: 1800, reference: null },
  ],
  reasons: ['✓ Active contract CNT-ABC-2026 on 07 Oct 2026', '✓ exact lane: Mumbai → Pune', '✓ Weight slab 10000–15000 kg'], notes: [], chargeableWeightKg: null, ...over,
})

const success = (): RatingResultDto => ({
  qualified: true, errorCode: null, message: null, advice: [], ratingId: null, ratingReference: null, committed: false, calculationVersion: '1.0', shipmentDate: '2026-10-07', selected: option(), options: [option()],
  exclusions: [{ contractReference: 'CNT-ABC-2026', rateReference: 'RATE-OLD V1', reasonCode: 'RATE_EXPIRED', reason: 'expired on 30 Sep 2026' }],
  trace: [{ stage: 'Input', text: 'Mumbai → Pune · Ftl', ok: true }, { stage: 'Result', text: 'CNT-ABC-2026 · total INR 41320', ok: true }],
})

beforeEach(() => {
  vi.clearAllMocks()
  auth.permissions = new Set(['contracts.read'])
})

describe('rating result', () => {
  it('shows the total, every line, why the rate was chosen, what was left out and the trace', () => {
    renderWithProviders(<RatingResultView result={success()} />)

    expect(screen.getByText(/CNT-ABC-2026 V3 · RATE-MUM-PUN-32FT V4/)).toBeInTheDocument()
    expect(screen.getAllByText('₹41,320.00').length).toBeGreaterThan(0)
    expect(screen.getByText('Flat rate for the trip')).toBeInTheDocument()
    expect(screen.getByText('DPH +4%')).toBeInTheDocument()
    expect(screen.getByText('exact lane: Mumbai → Pune')).toBeInTheDocument()
    expect(screen.getByText('expired on 30 Sep 2026')).toBeInTheDocument()
    expect(screen.getByText('Mumbai → Pune · Ftl')).toBeInTheDocument()
  })

  it('says plainly when there is no rate and what to check, and shows no freight', () => {
    renderWithProviders(<RatingResultView result={{ ...success(), qualified: false, selected: null, options: [], errorCode: 'FREIGHT_RATE_NOT_FOUND', message: 'No applicable active freight rate was found for Mumbai → Nagpur.', advice: ['Check the lane rate and the zone rate'], exclusions: [] }} />)

    expect(screen.getByText('FREIGHT RATE NOT FOUND')).toBeInTheDocument()
    expect(screen.getByText(/No applicable active freight rate/)).toBeInTheDocument()
    expect(screen.getByText(/Check the lane rate and the zone rate/)).toBeInTheDocument()
    expect(screen.queryByText('Total freight')).toBeNull()
  })
})

describe('simulator', () => {
  it('lets anyone calculate, but offers to keep a rating only to those allowed to', async () => {
    const { unmount } = renderWithProviders(<RateSimulatorPage />)
    expect(screen.getByRole('button', { name: /calculate/i })).toBeInTheDocument()
    expect(screen.queryByPlaceholderText('Shipment reference, to keep this rating')).toBeNull()
    unmount()

    auth.permissions = new Set(['contracts.read', 'contracts.rate'])
    renderWithProviders(<RateSimulatorPage />)
    expect(screen.getByPlaceholderText('Shipment reference, to keep this rating')).toBeInTheDocument()
  })
})

describe('rating history', () => {
  const summary = (over: Partial<RatingSummaryDto> = {}): RatingSummaryDto => ({
    id: 'k1', reference: 'FR-000012', shipmentReference: 'SH-10025', committed: true, qualified: true, errorCode: null, lane: 'Mumbai → Pune', service: 'Ftl', shipmentDate: '2026-07-10', transporterId: 't1', transporterName: 'ABC Logistics',
    contractReference: 'CNT-ABC-2026', contractRevision: 2, rateCode: 'RATE-MUM-PUN-32FT', rateVersion: 7, totalFreight: 39800, overrideAmount: null, currency: 'INR', calculationVersion: '1.0', calculatedAt: '2026-07-10T09:00:00Z', ...over,
  })

  it('lists what was rated under which versions and no-rate attempts, and opens the full record with a reproduce check', async () => {
    const user = userEvent.setup()
    vi.mocked(freightApi.ratings).mockResolvedValue({ items: [summary(), summary({ id: 'k2', reference: 'FR-000013', shipmentReference: null, committed: false, qualified: false, contractReference: null, rateCode: null, rateVersion: null, totalFreight: 0, lane: 'Mumbai → Nowhere' })], page: 1, pageSize: 20, totalCount: 2, totalPages: 1 })
    const detail: RatingDetailDto = { summary: summary(), result: { ...success(), ratingId: 'k1', committed: true }, request: null, overrideAmount: null, overrideReason: null, overrideApprovedBy: null, overriddenAt: null, dphRule: 'DPH', dphVersion: 4 }
    vi.mocked(freightApi.rating).mockResolvedValue(detail)
    vi.mocked(freightApi.reproduce).mockResolvedValue({ matches: true, recalculated: true, storedCalculationVersion: '1.0', engineVersion: '1.0', storedTotal: 39800, recalculatedTotal: 39800, differences: [], message: 'Rated again from the stored request, the same freight and the same lines were produced.' })
    renderWithProviders(<RatingHistoryPage />)

    expect(await screen.findByText('CNT-ABC-2026 V2')).toBeInTheDocument()
    expect(screen.getByText('RATE-MUM-PUN-32FT V7')).toBeInTheDocument()
    expect(screen.getByText('₹39,800.00')).toBeInTheDocument()
    expect(screen.getByText('No rate')).toBeInTheDocument()

    await user.click(screen.getByText('FR-000012'))
    await user.click(await screen.findByRole('tab', { name: 'Reproduce' }))
    await user.click(await screen.findByRole('button', { name: /rate it again/i }))
    expect(await screen.findByText('Reproduced')).toBeInTheDocument()
  })
})

describe('contract dashboard', () => {
  it('puts the loads without a rate where they cannot be missed', async () => {
    const dash: ContractDashboardDto = {
      totalContracts: 24, active: 14, draft: 3, pendingApproval: 1, expiringSoon: 3, expired: 2, suspended: 1, activeRates: 180, ratesExpiring: 9, dphRules: 13, dphRevisionsDue: 2, uncoveredLanes: 3, validationErrors: 2, failedRatings30Days: 7, asOf: '2026-10-07T09:00:00Z',
    }
    vi.mocked(freightApi.dashboard).mockResolvedValue(dash)
    vi.mocked(freightApi.expiry).mockResolvedValue({ bands: [7, 15, 30, 60, 90], items: [{ kind: 'Contract', reference: 'CN-00003', title: 'Carrier 3', contractId: 'c3', contractNumber: 'CNT-FTL-03', expiresOn: '2026-10-30', daysLeft: 23, band: '30 days' }] })
    vi.mocked(freightApi.coverage).mockResolvedValue({ requiredLanes: 20, coveredLanes: 17, uncoveredLanes: 3, fallbackCovered: 4, activeContracts: 14, activeRates: 180, ratesExpiring: 9, duplicateRates: 0, overlappingRates: 1, loadsWithoutRate: 7, uncovered: [] })
    vi.mocked(freightApi.validation).mockResolvedValue({ contractsChecked: 2, errors: 2, warnings: 1, contracts: [] })
    vi.mocked(freightApi.usage).mockResolvedValue([])
    renderWithProviders(<ContractDashboardPage />)

    expect(await screen.findByText('7 load(s) in the last 30 days had no applicable contractual rate')).toBeInTheDocument()
    expect(screen.getByLabelText('Active contracts')).toHaveTextContent('14')
    expect(screen.getByLabelText('Uncovered lanes')).toHaveTextContent('3')
    expect(await screen.findByText('CNT-FTL-03')).toBeInTheDocument()
    expect(screen.getByText('30 days')).toBeInTheDocument()
  })
})

describe('diesel adjustment', () => {
  it('shows each rule against today\'s diesel and flags the ones whose period has not been fixed', async () => {
    const row: DphOverviewDto = {
      rule: { id: 'd1', contractId: 'c1', contractNumber: 'CNT-ABC-2026', contractRevision: 3, code: 'DPH', version: 4, effectiveFrom: '2026-10-01', effectiveTo: '2026-10-31', inForce: true,
        spec: { code: 'DPH', name: 'Diesel', formula: 'PercentageVariation', region: 'MUMBAI', baseDieselPrice: 90, baseDate: '2026-04-01', fuelComponentPercent: 30, thresholdPercent: 0, stepPercent: 0, fixedAmountPerStep: 0, perKmPerStep: 0,
          impactPercentPerStep: 0, capPercent: null, onExcessOnly: false, direction: 'Both', frequency: 'Monthly', adjustmentDecimals: 2, effectiveFrom: null, effectiveTo: null, isDefault: true } },
      currentPrice: 99, variationPercent: 10, adjustmentPercent: 3, priceDate: '2026-10-01', revisionDue: true,
    }
    vi.mocked(freightApi.dph).mockResolvedValue([row])
    renderWithProviders(<DphManagementPage />)

    expect(await screen.findByText('CNT-ABC-2026 V3')).toBeInTheDocument()
    expect(screen.getByText('+10%')).toBeInTheDocument()
    expect(screen.getByText('+3%')).toBeInTheDocument()
    expect(screen.getByText('Revision due')).toBeInTheDocument()
    expect(screen.getByText('₹99')).toBeInTheDocument()
  })
})

describe('rate import', () => {
  const batch = (over: Partial<ImportBatchDto> = {}): ImportBatchDto => ({
    id: 'b1', reference: 'IMP-00001', fileName: 'rates.xlsx', mode: 'Append', status: 'Previewed', contractNumber: 'CN-00009', contractId: 'c9', appliedContractId: null, rowCount: 3, errorRows: 1, warningRows: 0, createdAt: '2026-10-07T09:00:00Z', appliedAt: null,
    rows: [
      { rowNumber: 2, status: 'Valid', values: { origin: 'Mumbai, Maharashtra', destination: 'Pune, Maharashtra', rate: '38000', ratetype: 'FIXED' }, issues: [] },
      { rowNumber: 3, status: 'Error', values: { origin: 'Mumbai, Maharashtra', destination: 'Surat, Gujarat', rate: '40000', ratetype: 'FIXED' }, issues: [{ severity: 'Error', row: 3, field: 'weight', code: 'RATE_TERMS_INVALID', message: 'The weight band is invalid: the upper limit must be above the lower limit' }] },
      { rowNumber: 4, status: 'Valid', values: { origin: 'Maharashtra', destination: 'Gujarat', rate: '10', ratetype: 'PER_KG' }, issues: [] },
    ], ...over,
  })

  it('shows every row checked, names the problem, and will not import until the bad rows are skipped or fixed', async () => {
    const user = userEvent.setup()
    auth.permissions = new Set(['contracts.read', 'contracts.manage'])
    vi.mocked(freightApi.upload).mockResolvedValue(batch())
    vi.mocked(freightApi.import).mockResolvedValue(batch())
    const { container } = renderWithProviders(<RateImportPage />)

    await user.upload(container.querySelector('input[type=file]')!, new File(['x'], 'rates.xlsx'))

    expect(await screen.findByText(/IMP-00001/)).toBeInTheDocument()
    expect(screen.getByText('1 with errors')).toBeInTheDocument()
    expect(screen.getByText(/The weight band is invalid/)).toBeInTheDocument()
    const apply = screen.getByRole('button', { name: /put the valid rows into a draft/i })
    expect(apply).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: /skip the 1 row/i }))
    await waitFor(() => expect(apply).toBeEnabled())
  })
})
