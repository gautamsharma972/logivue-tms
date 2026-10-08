import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderWithProviders } from '@/test/renderWithProviders'
import { reportsApi } from './api'
import { canDrill, drillLink, filtersFromSearch } from './drill'
import { formatCell, formatChange, formatKpi, toneOf } from './format'
import { ReportKpiCard } from './ReportKpiCard'
import { ReportPage } from './ReportPage'
import { ReportsHomePage } from './ReportsHomePage'
import { ReportTable } from './ReportTable'
import type { KpiCard, ReportMetadataDto, ReportResult, ReportSummaryDto } from './types'

vi.mock('@/lib/api/endpoints', () => ({ authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() }, downloadFile: vi.fn(), usersApi: { lookup: vi.fn() } }))
vi.mock('./api', () => ({
  reportsApi: { list: vi.fn(), metadata: vi.fn(), run: vi.fn(), lookup: vi.fn(), preferences: vi.fn(), savePreferences: vi.fn(), exportReport: vi.fn(), download: vi.fn(), subscriptions: vi.fn() },
}))
vi.mock('recharts', async () => {
  const actual = await vi.importActual<typeof import('recharts')>('recharts')
  return { ...actual, ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div> }
})

const card = (over: Partial<KpiCard> = {}): KpiCard => ({
  code: 'OTD', name: 'On-time delivery (OTD)', unit: 'Percent', value: 94.2, numerator: 9420, denominator: 10000, measurable: true, note: null, previous: 91.8, samePeriodLastYear: 90, change: 2.4, changePct: null,
  trend: 'up', assessment: 'good', calculationVersion: '1.0', higherIsBetter: true, drillReport: 'R17_DELIVERY_PERFORMANCE', drillFilters: { onTime: 'No' }, ...over,
})

describe('formatting', () => {
  it('shows nothing to show as a dash and never as zero, and formats money, percent and dates by their column type', () => {
    expect(formatCell(null, 'Currency')).toBe('—')
    expect(formatCell(0, 'Percent')).toBe('0%')
    expect(formatCell(1234567, 'Currency')).toBe('₹ 12,34,567')
    expect(formatCell(93.44, 'Percent')).toBe('93.44%')
    expect(formatCell('2026-10-07', 'Date')).toBe('07 Oct 2026')
    expect(formatCell(true, 'Boolean')).toBe('Yes')
    expect(formatCell(12, 'Duration')).toBe('12 min')
  })

  it('writes a KPI as Not measurable when it cannot be judged and uses lakh and crore for large money', () => {
    expect(formatKpi({ unit: 'Percent', value: null, measurable: false })).toBe('Not measurable')
    expect(formatKpi({ unit: 'Currency', value: 4_434_095, measurable: true })).toBe('₹ 44.34 L')
    expect(formatKpi({ unit: 'Currency', value: 4_20_00_000, measurable: true })).toBe('₹ 4.2 Cr')
    expect(formatKpi({ unit: 'Currency', value: 6.71, measurable: true })).toBe('₹ 6.71')
    expect(formatChange(card({ change: 2.4 }))).toBe('+2.4 pts')
    expect(formatChange(card({ change: null }))).toBeNull()
  })

  it('colours the words that carry good, bad or warning meaning', () => {
    expect(toneOf('Delivered')).toBe('success')
    expect(toneOf('SeverelyDelayed')).toBe('error')
    expect(toneOf('AtRisk')).toBe('warning')
    expect(toneOf('Something else')).toBe('default')
  })
})

describe('drill-down links', () => {
  it('keep the filters being viewed, add the clicked row’s values and fixed values, and leave presentation choices behind', () => {
    const link = drillLink('R17_DELIVERY_PERFORMANCE', { transporter: 'transporter', onTime: '=No' }, { transporter: 'Om Logistics', x: 1 }, { period: 'Rolling90', lane: 'Mumbai → Pune', rankBy: 'OTD', f_status: 'x', search: 'abc' })
    const url = new URL(link, 'http://x')
    expect(url.pathname).toBe('/reports/R17_DELIVERY_PERFORMANCE')
    expect(Object.fromEntries(url.searchParams)).toEqual({ period: 'Rolling90', lane: 'Mumbai → Pune', transporter: 'Om Logistics', onTime: 'No' })
  })

  it('cannot be followed from a row that lacks a value the drill needs, and a row filter can be removed on the next page', () => {
    expect(canDrill({ field: 'a', targetReport: 'R', label: 'L', map: { shipment: 'shipment' } }, { shipment: null })).toBe(false)
    expect(canDrill({ field: 'a', targetReport: 'R', label: 'L', map: { shipment: 'shipment', onTime: '=No' } }, { shipment: 'SH-1' })).toBe(true)
    expect(filtersFromSearch(new URLSearchParams('period=Rolling30&page=3&groupBy=lane&sort=x:ASC&transporter=Alpha'))).toEqual({ period: 'Rolling30', transporter: 'Alpha' })
  })
})

describe('KPI card', () => {
  it('shows the value, the numerator and denominator, the earlier periods and links to the report behind it', () => {
    renderWithProviders(<ReportKpiCard card={card()} filters={{ period: 'Rolling30' }} />)
    const el = screen.getByTestId('kpi-OTD')
    expect(within(el).getByText('94.2%')).toBeInTheDocument()
    expect(within(el).getByText('9,420 of 10,000')).toBeInTheDocument()
    expect(within(el).getByText(/\+2.4 pts/)).toBeInTheDocument()
    expect(within(el).getByText(/prev 91.8%/)).toBeInTheDocument()
    expect(el.closest('a')).toHaveAttribute('href', '/reports/R17_DELIVERY_PERFORMANCE?period=Rolling30&onTime=No')
  })

  it('says Not measurable, with the reason, instead of 0%', () => {
    renderWithProviders(<ReportKpiCard card={card({ value: null, measurable: false, note: 'Not measurable: no planned delivery time', previous: null, change: null, trend: 'none' })} filters={{}} />)
    const el = screen.getByTestId('kpi-OTD')
    expect(within(el).getByText('Not measurable')).toBeInTheDocument()
    expect(within(el).getByText(/no planned delivery time/)).toBeInTheDocument()
    expect(within(el).queryByText('0%')).not.toBeInTheDocument()
  })
})

describe('report table', () => {
  const columns = [
    { field: 'transporter', displayName: 'Transporter', dataType: 'Text' as const, format: null, sequence: 1, sortable: true, filterable: true, visible: true },
    { field: 'amount', displayName: 'Amount', dataType: 'Currency' as const, format: null, sequence: 2, sortable: true, filterable: false, visible: true },
    { field: 'status', displayName: 'Status', dataType: 'Status' as const, format: null, sequence: 3, sortable: false, filterable: false, visible: true },
  ]

  it('shows the server’s total, links drillable values, and asks the server for another page or order', async () => {
    const onChange = vi.fn()
    renderWithProviders(
      <ReportTable columns={columns} rows={[{ transporter: 'Alpha', amount: 1500, status: 'Delivered' }]} total={120} page={1} pageSize={50} filters={{ period: 'Rolling30' }} sort={null} onChange={onChange}
        drills={[{ field: 'transporter', targetReport: 'R09_TRANSPORTER_SCORECARD', label: 'Scorecard', map: { transporter: 'transporter' } }]} />,
    )
    expect(screen.getByText('120 rows')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Alpha' })).toHaveAttribute('href', '/reports/R09_TRANSPORTER_SCORECARD?period=Rolling30&transporter=Alpha')
    expect(screen.getByText('₹ 1,500')).toBeInTheDocument()
    expect(screen.getByText('Delivered')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('columnheader', { name: /Amount/ }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ sort: { field: 'amount', direction: 'ASC' } }))
  })

  it('says plainly when there is no data for the filters', () => {
    renderWithProviders(<ReportTable columns={columns} rows={[]} total={0} page={1} pageSize={50} drills={[]} filters={{}} sort={null} onChange={vi.fn()} />)
    expect(screen.getByText('No data found for the selected filters.')).toBeInTheDocument()
  })
})

const summary = (over: Partial<ReportSummaryDto>): ReportSummaryDto => ({
  reportCode: 'R09_TRANSPORTER_SCORECARD', name: 'Transporter performance scorecard', description: 'Scores for each transporter.', category: 'Transporter Management', reportType: 'Analytical', dataSource: 'Transporters',
  refresh: 'OnDemand', requiredPermission: 'reports.transporters', status: 'Active', canOpen: true, vendorSafe: true, exportFormats: ['csv', 'xlsx'], isFavourite: false, ...over,
})

describe('catalogue', () => {
  beforeEach(() => vi.clearAllMocks())

  it('groups reports by category, puts favourites first and searches by what the report measures', async () => {
    vi.mocked(reportsApi.list).mockResolvedValue([summary({ isFavourite: true }), summary({ reportCode: 'R17_DELIVERY_PERFORMANCE', name: 'Delivery performance', category: 'POD & Delivery' })])
    renderWithProviders(<ReportsHomePage />)
    expect(await screen.findByRole('region', { name: 'My reports' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'POD & Delivery' })).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Transporter performance scorecard' }).length).toBeGreaterThan(0)
    await userEvent.type(screen.getByPlaceholderText(/Search reports/), 'transporter')
    await waitFor(() => expect(reportsApi.list).toHaveBeenLastCalledWith('transporter'))
  })

  it('tells a person with no access that there is nothing for them rather than showing an empty page', async () => {
    vi.mocked(reportsApi.list).mockResolvedValue([])
    renderWithProviders(<ReportsHomePage />)
    expect(await screen.findByText('You do not have access to any report yet.')).toBeInTheDocument()
  })
})

describe('report page', () => {
  const meta: ReportMetadataDto = {
    reportCode: 'R17_DELIVERY_PERFORMANCE', name: 'Delivery performance', description: 'Deliveries by outcome.', category: 'POD & Delivery', reportType: 'Analytical', dataSource: 'Deliveries', refresh: 'NearRealTime',
    requiredPermission: 'reports.delivery', status: 'Active', columns: [], filters: [{ name: 'fromDate', dataType: 'Date', required: false, defaultValue: null, lookupSource: null, sequence: 1 }, { name: 'transporter', dataType: 'Text', required: false, defaultValue: null, lookupSource: 'transporter', sequence: 2 }],
    grouping: [], availableGrouping: [{ field: 'transporter', displayName: 'Transporter' }], defaultGroupBy: [], sorting: [], drills: [], exportFormats: ['csv', 'xlsx', 'pdf'], canExport: true, canSchedule: true, isFavourite: false, supportsComparison: true, note: null, version: 1,
  }
  const result: ReportResult = {
    reportCode: 'R17_DELIVERY_PERFORMANCE', reportName: 'Delivery performance', reportType: 'Analytical', generatedAtUtc: '2026-10-07T10:00:00Z', dataFreshnessSeconds: 0, durationMs: 42, totalRows: 1, page: 1, pageSize: 50,
    columns: [{ field: 'delivery', displayName: 'Delivery', dataType: 'Text', format: null, sequence: 1, sortable: true, filterable: false, visible: true }], rows: [{ delivery: 'DLV-1' }], summary: {}, totals: [{ key: 'late', label: 'Late', value: 3, unit: null, drillReport: null, drillFilters: null, tone: 'warn' }],
    cards: [card()], charts: [], sections: [], notes: ['On time is the Deliveries module’s own judgement.'], filtersApplied: { transporter: 'Alpha' }, groupedBy: [], drills: [],
    period: { from: '2026-09-08', to: '2026-10-07', previousFrom: null, previousTo: null, lastYearFrom: null, lastYearTo: null, kind: 'Rolling30' }, calculationVersion: '1.0', dataSourceMode: 'Demo', refresh: 'NearRealTime', fromCache: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(reportsApi.metadata).mockResolvedValue(meta)
    vi.mocked(reportsApi.run).mockResolvedValue(result)
    vi.mocked(reportsApi.lookup).mockResolvedValue({ name: 'transporter', values: ['Alpha', 'Beta'] })
  })

  it('draws a report from its metadata: KPI cards, totals, table, notes, freshness, and says when the data is the demonstration data', async () => {
    renderWithProviders(<ReportPage code="R17_DELIVERY_PERFORMANCE" />, { route: '/reports/R17_DELIVERY_PERFORMANCE?transporter=Alpha' })
    expect(await screen.findByText('Delivery performance')).toBeInTheDocument()
    expect(await screen.findByTestId('kpi-OTD')).toBeInTheDocument()
    expect(screen.getByTestId('total-late')).toBeInTheDocument()
    expect(screen.getByText('DLV-1')).toBeInTheDocument()
    expect(screen.getByText('Showing demonstration data')).toBeInTheDocument()
    expect(screen.getByText(/own judgement/)).toBeInTheDocument()
    expect(screen.getByText(/Last updated/)).toBeInTheDocument()
    expect(reportsApi.run).toHaveBeenCalledWith('R17_DELIVERY_PERFORMANCE', expect.objectContaining({ filters: { transporter: 'Alpha' }, page: 1, pageSize: 50 }))
    expect(screen.getByRole('button', { name: /Export/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Schedule/ })).toBeInTheDocument()
  })

  it('offers no export or schedule to someone who may not, and no grouping the report does not have', async () => {
    vi.mocked(reportsApi.metadata).mockResolvedValue({ ...meta, canExport: false, canSchedule: false, availableGrouping: [] })
    renderWithProviders(<ReportPage code="R17_DELIVERY_PERFORMANCE" />)
    await screen.findByText('Delivery performance')
    expect(screen.queryByRole('button', { name: /Export/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Schedule/ })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Group by')).not.toBeInTheDocument()
  })

  it('shows a clear message, not a blank page, for a report the person may not open', async () => {
    vi.mocked(reportsApi.metadata).mockRejectedValue({ isAxiosError: true, response: { status: 403, data: { code: 'reports.forbidden', title: 'Forbidden', detail: 'You are not allowed to open this report.' } } })
    renderWithProviders(<ReportPage code="R35_FREIGHT_RATING_AUDIT" />)
    expect(await screen.findByText('You cannot open this report')).toBeInTheDocument()
  })
})
