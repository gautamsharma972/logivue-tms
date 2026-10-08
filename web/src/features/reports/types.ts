/** The reporting API's shapes (Module 6). Values in rows are whatever the report produced: numbers, text, ISO dates. */
export type ReportCell = string | number | boolean | null
export type ReportRow = Record<string, ReportCell>

export type FieldType = 'Text' | 'Whole' | 'Number' | 'Percent' | 'Currency' | 'Date' | 'DateTime' | 'Duration' | 'Boolean' | 'Status'
export type ReportKind = 'Dashboard' | 'Operational' | 'Analytical' | 'Drilldown'

export interface ReportSummaryDto {
  reportCode: string
  name: string
  description: string
  category: string
  reportType: ReportKind
  dataSource: string
  refresh: string
  requiredPermission: string
  status: string
  canOpen: boolean
  vendorSafe: boolean
  exportFormats: string[]
  isFavourite: boolean
}

export interface ColumnDto {
  field: string
  displayName: string
  dataType: FieldType
  format: string | null
  sequence: number
  sortable: boolean
  filterable: boolean
  visible: boolean
}

export interface FilterDto {
  name: string
  dataType: FieldType
  required: boolean
  defaultValue: string | null
  lookupSource: string | null
  sequence: number
}

export interface GroupingDto {
  field: string
  displayName: string
}

export interface DrillDto {
  field: string
  targetReport: string
  label: string
  map: Record<string, string>
}

export interface ReportMetadataDto {
  reportCode: string
  name: string
  description: string
  category: string
  reportType: ReportKind
  dataSource: string
  refresh: string
  requiredPermission: string
  status: string
  columns: ColumnDto[]
  filters: FilterDto[]
  grouping: GroupingDto[]
  availableGrouping: GroupingDto[]
  defaultGroupBy: string[]
  sorting: { field: string; descending: boolean }[]
  drills: DrillDto[]
  exportFormats: string[]
  canExport: boolean
  canSchedule: boolean
  isFavourite: boolean
  supportsComparison: boolean
  note: string | null
  version: number
}

export interface KpiCard {
  code: string
  name: string
  unit: 'Percent' | 'Count' | 'Currency' | 'Number' | 'Minutes' | 'Ratio'
  value: number | null
  numerator: number | null
  denominator: number | null
  measurable: boolean
  note: string | null
  previous: number | null
  samePeriodLastYear: number | null
  change: number | null
  changePct: number | null
  trend: 'up' | 'down' | 'flat' | 'none'
  assessment: 'good' | 'bad' | 'neutral'
  calculationVersion: string
  higherIsBetter: boolean
  drillReport: string | null
  drillFilters: Record<string, string> | null
}

export interface ChartSeries {
  key: string
  name: string
  colour?: string | null
}

export interface ChartLine {
  axis: 'x' | 'y'
  value: number
  label: string
}

export interface ChartData {
  id: string
  title: string
  kind: 'line' | 'bar' | 'stackedBar' | 'donut' | 'heatmap' | 'scatter' | 'map'
  xField: string | null
  series: ChartSeries[]
  data: ReportRow[]
  note: string | null
  yLabel: string | null
  xLabel: string | null
  drillReport: string | null
  drillMap: Record<string, string> | null
  lines: ChartLine[] | null
}

export interface SectionItem {
  label: string
  value: ReportCell
  format: string | null
  drillReport: string | null
  drillFilters: Record<string, string> | null
}

export interface ReportSection {
  key: string
  title: string
  module: string | null
  items: SectionItem[]
  table: ReportRow[] | null
  tableColumns: { field: string; display: string; type: FieldType | number }[] | null
}

export interface TotalDto {
  key: string
  label: string
  value: ReportCell
  unit: string | null
  drillReport: string | null
  drillFilters: Record<string, string> | null
  tone: string | null
}

export interface ReportResult {
  reportCode: string
  reportName: string
  reportType: ReportKind
  generatedAtUtc: string
  dataFreshnessSeconds: number
  durationMs: number
  totalRows: number
  page: number
  pageSize: number
  columns: ColumnDto[]
  rows: ReportRow[]
  summary: Record<string, ReportCell>
  totals: TotalDto[]
  cards: KpiCard[]
  charts: ChartData[]
  sections: ReportSection[]
  notes: string[]
  filtersApplied: Record<string, string>
  groupedBy: string[]
  drills: DrillDto[]
  period: { from: string; to: string; previousFrom: string | null; previousTo: string | null; lastYearFrom: string | null; lastYearTo: string | null; kind: string }
  calculationVersion: string
  dataSourceMode: 'Live' | 'Demo' | 'Mixed'
  refresh: string
  fromCache: boolean
}

export interface ReportRequest {
  filters?: Record<string, string>
  groupBy?: string[]
  sort?: { field: string; direction: 'ASC' | 'DESC' }[]
  page?: number
  pageSize?: number
  refresh?: boolean
}

export interface ReportJobDto {
  id: string
  jobReference: string
  reportCode: string
  reportName: string
  format: string
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Expired' | 'Cancelled'
  requestedAt: string
  startedAt: string | null
  completedAt: string | null
  progress: number
  rowCount: number | null
  sizeBytes: number | null
  fileName: string | null
  errorMessage: string | null
  expiresAt: string | null
  downloadCount: number
  canDownload: boolean
  fromSchedule: boolean
}

export interface ExportOutcome {
  job: ReportJobDto
  immediate: boolean
}

export interface ScheduleDefinition {
  time?: string | null
  dayOfWeek?: string | null
  dayOfMonth?: number | null
  everyMinutes?: number | null
}

export interface SubscriptionDto {
  id: string
  reportCode: string
  reportName: string
  name: string
  filters: Record<string, string>
  scheduleType: 'Daily' | 'Weekly' | 'Monthly' | 'Custom'
  schedule: ScheduleDefinition
  format: string
  timeZone: string
  recipients: string[]
  active: boolean
  nextRunAt: string | null
  lastRunAt: string | null
  consecutiveFailures: number
  userId: string
  userName: string | null
  version: number
  summary: string | null
}

export interface SaveSubscriptionRequest {
  reportCode: string
  name?: string | null
  filters?: Record<string, string>
  scheduleType: string
  schedule: ScheduleDefinition
  format?: string
  timeZone?: string
  recipients?: string[]
  active?: boolean
  version?: number | null
}

export interface PreferenceDto {
  favourites: string[]
  widgets: string[]
  defaultFilters: Record<string, string>
  refreshSeconds: number
}

export interface ReportSettings {
  dataSource: 'Live' | 'Demo'
  ageingBuckets: number[]
  ageingUsesWorkingDays: boolean
  workingDays: number[]
  holidays: string[]
  otpGraceMinutes: number
  otdGraceMinutes: number
  etaToleranceMinutes: number
  classificationMethod: 'Median' | 'Average'
  defaultPeriodDays: number
  topN: number
  syncExportRows: number
  exportMaxRows: number
  jobKeepHours: number
  slowReportMs: number
  dashboardCacheSeconds: number
  metadataCacheSeconds: number
  controlTowerCacheSeconds: number
  maxPageSize: number
  scheduleStopAfterFailures: number
  maxSubscriptionsPerUser: number
  expiryBands: number[]
  regionMap: Record<string, string>
}

export interface SettingsDto {
  settings: ReportSettings
  version: number
}

export interface DataSourceStatusDto {
  mode: 'Live' | 'Demo'
  providers: Record<string, string>
  demoAvailable: boolean
}

export interface KpiDefinitionDto {
  kpiCode: string
  kpiName: string
  description: string
  formula: string
  unit: string
  numerator: string
  denominator: string
  aggregation: string
  applicableModule: string
  applicableServiceTypes: string
  sourceOfTruth: string
  calculationVersion: string
  higherIsBetter: boolean
  status: string
}

export interface AuditEntryDto {
  id: string
  reportCode: string
  action: string
  requestedBy: string | null
  userName: string | null
  parameters: string | null
  performedAtUtc: string
  exportFormat: string | null
  durationMs: number | null
  rowCount: number | null
  outcome: string | null
}

export interface DataScopeDto {
  userId: string
  dimension: string
  values: string[]
}

export interface LookupDto {
  name: string
  values: string[]
}
