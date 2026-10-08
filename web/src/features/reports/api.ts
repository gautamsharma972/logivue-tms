import { downloadFile } from '@/lib/api/endpoints'
import { http } from '@/lib/api/client'
import type { PagedResult } from '@/lib/api/types'
import type {
  AuditEntryDto, DataScopeDto, DataSourceStatusDto, ExportOutcome, KpiDefinitionDto, LookupDto, PreferenceDto, ReportJobDto, ReportMetadataDto, ReportRequest, ReportResult,
  ReportSummaryDto, SaveSubscriptionRequest, SettingsDto, SubscriptionDto, ReportSettings,
} from './types'

const v1 = '/api/v1'
const r = `${v1}/reports`

export const reportsApi = {
  list: (search?: string) => http.get<ReportSummaryDto[]>(r, { params: { search: search || undefined } }).then((x) => x.data),
  metadata: (code: string) => http.get<ReportMetadataDto>(`${r}/${code}/metadata`).then((x) => x.data),
  run: (code: string, body: ReportRequest) => http.post<ReportResult>(`${r}/${code}/execute`, body).then((x) => x.data),
  exportReport: (code: string, body: { filters?: Record<string, string>; groupBy?: string[]; sort?: { field: string; direction: string }[]; format: string; background?: boolean }) =>
    http.post<ExportOutcome>(`${r}/${code}/export`, body).then((x) => x.data),
  lookup: (name: string) => http.get<LookupDto>(`${r}/lookups/${name}`).then((x) => x.data),
  preferences: () => http.get<PreferenceDto>(`${r}/preferences`).then((x) => x.data),
  savePreferences: (body: Partial<PreferenceDto>) => http.put<PreferenceDto>(`${r}/preferences`, body).then((x) => x.data),

  jobs: () => http.get<ReportJobDto[]>(`${r}/jobs`).then((x) => x.data),
  job: (id: string) => http.get<ReportJobDto>(`${r}/jobs/${id}`).then((x) => x.data),
  cancelJob: (id: string) => http.post<ReportJobDto>(`${r}/jobs/${id}/cancel`).then((x) => x.data),
  retryJob: (id: string) => http.post<ReportJobDto>(`${r}/jobs/${id}/retry`).then((x) => x.data),
  download: (job: ReportJobDto) => downloadFile(`${r}/jobs/${job.id}/download`, job.fileName ?? `${job.reportCode}.${job.format}`),

  subscriptions: (all = false) => http.get<SubscriptionDto[]>(`${v1}/report-subscriptions`, { params: { all: all || undefined } }).then((x) => x.data),
  createSubscription: (body: SaveSubscriptionRequest) => http.post<SubscriptionDto>(`${v1}/report-subscriptions`, body).then((x) => x.data),
  updateSubscription: (id: string, body: SaveSubscriptionRequest) => http.put<SubscriptionDto>(`${v1}/report-subscriptions/${id}`, body).then((x) => x.data),
  deleteSubscription: (id: string) => http.delete(`${v1}/report-subscriptions/${id}`),
  runSubscriptionNow: (id: string) => http.post<ReportJobDto>(`${v1}/report-subscriptions/${id}/run-now`).then((x) => x.data),

  dataSource: () => http.get<DataSourceStatusDto>(`${r}/data-source`).then((x) => x.data),
  settings: () => http.get<SettingsDto>(`${r}/settings`).then((x) => x.data),
  saveSettings: (settings: ReportSettings, version: number) => http.put<SettingsDto>(`${r}/settings`, { settings, version }).then((x) => x.data),
  rebuildSummary: (days: number) => http.post<number>(`${r}/summary/rebuild`, null, { params: { days } }).then((x) => x.data),
  updateDefinition: (code: string, body: { name?: string; description?: string; status?: string; columns?: { field: string; displayName?: string; visible?: boolean }[]; version: number }) =>
    http.put<ReportMetadataDto>(`${r}/${code}/definition`, body).then((x) => x.data),
  scopes: (userId?: string) => http.get<DataScopeDto[]>(`${r}/scopes`, { params: { userId } }).then((x) => x.data),
  saveScope: (body: DataScopeDto) => http.put<DataScopeDto>(`${r}/scopes`, body).then((x) => x.data),
  audit: (params: { report?: string; action?: string; page?: number; pageSize?: number }) => http.get<PagedResult<AuditEntryDto>>(`${r}/audit`, { params }).then((x) => x.data),
  kpis: () => http.get<KpiDefinitionDto[]>(`${v1}/kpis`).then((x) => x.data),
}
