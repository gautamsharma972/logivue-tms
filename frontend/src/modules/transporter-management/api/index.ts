import { fetchBlob, http, toQuery } from '../../../core/http';
import type {
  AlertDto,
  Benchmark,
  CapacityDay,
  ClaimRecord,
  ComplianceReport,
  DriverDto,
  DocumentDto,
  LaneDto,
  OperationalPeriod,
  PagedResult,
  PlacementDto,
  PodDto,
  RankingResult,
  Scorecard,
  TenderDetail,
  TenderInvitation,
  TransporterDetail,
  TransporterListItem,
  TransporterStatus,
  VehicleDto,
  VendorDashboard,
  VendorLoad,
} from '../types';

export type Period = {
  from: string;
  to: string;
};

export type RankingScopeParams = Period & {
  originLocationReference?: number;
  destinationLocationReference?: number;
  serviceType?: string;
  vehicleTypeReference?: number;
  region?: string;
  transporterTypeId?: number;
};

export const transporterApi = {
  list: (params: { search?: string; status?: TransporterStatus; page?: number; pageSize?: number }) =>
    http.get<PagedResult<TransporterListItem>>(`/api/v1/transporters${toQuery(params)}`),
  get: (id: number) => http.get<TransporterDetail>(`/api/v1/transporters/${id}`),
  compliance: (id: number) => http.get<ComplianceReport>(`/api/v1/transporters/${id}/compliance`),
  vehicles: (id: number) => http.get<PagedResult<VehicleDto>>(`/api/v1/transporters/${id}/vehicles?pageSize=100`),
  drivers: (id: number) => http.get<DriverDto[]>(`/api/v1/transporters/${id}/drivers`),
  documents: (id: number) => http.get<DocumentDto[]>(`/api/v1/transporters/${id}/documents`),
  lanes: (id: number) => http.get<PagedResult<LaneDto>>(`/api/v1/transporters/${id}/lanes?pageSize=100`),
  operations: (id: number, period: Period) =>
    http.get<OperationalPeriod>(`/api/v1/transporters/performance/${id}/operations${toQuery(period)}`),
  scorecards: (id: number) => http.get<Scorecard[]>(`/api/v1/transporters/${id}/scorecards`),
  generateScorecard: (id: number, period: Period) => http.post<Scorecard>(`/api/v1/transporters/${id}/scorecards`, period),
  claims: (id: number, period: Period) => http.get<ClaimRecord[]>(`/api/v1/transporters/${id}/claims${toQuery(period)}`),
  recordClaim: (id: number, body: { claimType: string; claimDate: string; claimValue: number; loadReference?: string; remarks?: string }) =>
    http.post<ClaimRecord>(`/api/v1/transporters/${id}/claims`, body),
  resolveClaim: (id: number, claimId: number) => http.post<ClaimRecord>(`/api/v1/transporters/${id}/claims/${claimId}/resolve`),
  recordLoadCost: (id: number, body: { loadReference: string; serviceDate: string; agreedAmount: number; invoicedAmount: number }) =>
    http.post(`/api/v1/transporters/${id}/load-costs`, body),
  capacity: (id: number, period: Period) => http.get<CapacityDay[]>(`/api/v1/transporters/${id}/capacity${toQuery(period)}`),
};

export const rankingApi = {
  rank: (scope: RankingScopeParams, metric: string) =>
    http.get<RankingResult>(`/api/v1/transporters/rankings${toQuery({ ...scope, metric })}`),
  benchmark: (transporterId: number, scope: RankingScopeParams) =>
    http.get<Benchmark>(`/api/v1/transporters/benchmark${toQuery({ transporterId, ...scope })}`),
};

export const tenderApi = {
  list: (params: { tenderNumber?: string; status?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<TenderInvitation>>(`/api/v1/tenders${toQuery(params)}`),
  get: (id: number) => http.get<TenderDetail>(`/api/v1/tenders/${id}`),
};

export const placementApi = {
  list: (params: { transporterId?: number; status?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<PlacementDto>>(`/api/v1/vehicle-placement${toQuery(params)}`),
};

export const alertApi = {
  list: (params: { transporterId?: number; status?: string; severity?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<AlertDto>>(`/api/v1/transporter-management/alerts${toQuery(params)}`),
  acknowledge: (id: number) => http.post<AlertDto>(`/api/v1/transporter-management/alerts/${id}/acknowledge`),
  resolve: (id: number, comments?: string) =>
    http.post<AlertDto>(`/api/v1/transporter-management/alerts/${id}/resolve`, { comments: comments ?? null }),
};

export const vendorApi = {
  dashboard: () => http.get<VendorDashboard>('/api/v1/vendor/dashboard'),
  tenders: (page = 1) => http.get<PagedResult<TenderInvitation>>(`/api/v1/vendor/tenders${toQuery({ page, pageSize: 25 })}`),
  tender: (id: number) => http.get<TenderDetail>(`/api/v1/vendor/tenders/${id}`),
  acceptTender: (id: number, quotedRate?: number) =>
    http.post<TenderDetail>(`/api/v1/vendor/tenders/${id}/accept`, { comments: null, quotedRate: quotedRate ?? null }),
  rejectTender: (id: number, reasonCode: string, comments?: string) =>
    http.post<TenderDetail>(`/api/v1/vendor/tenders/${id}/reject`, { reasonCode, comments: comments ?? null }),
  loads: () => http.get<VendorLoad[]>('/api/v1/vendor/loads'),
  placements: (page = 1) => http.get<PagedResult<PlacementDto>>(`/api/v1/vendor/placements${toQuery({ page, pageSize: 25 })}`),
  confirmPlacement: (id: number) => http.post<PlacementDto>(`/api/v1/vendor/placements/${id}/confirm`),
  pods: (page = 1) => http.get<PagedResult<PodDto>>(`/api/v1/vendor/pods${toQuery({ page, pageSize: 25 })}`),
  uploadPod: (loadReference: string, form: FormData) => http.post<PodDto>(`/api/v1/vendor/loads/${encodeURIComponent(loadReference)}/pod`, form),
  drivers: () => http.get<DriverDto[]>('/api/v1/vendor/drivers'),
  addDriver: (body: { fullName: string; mobile: string; licenceNumber: string }) => http.post<DriverDto>('/api/v1/vendor/drivers', body),
  updateDriver: (id: number, body: { fullName: string; mobile: string; licenceNumber: string }) =>
    http.put<DriverDto>(`/api/v1/vendor/drivers/${id}`, body),
  uploadDriverDocument: (driverId: number, form: FormData) =>
    http.post<DocumentDto>(`/api/v1/vendor/drivers/${driverId}/documents`, form),
  capacity: (period: Period) => http.get<CapacityDay[]>(`/api/v1/vendor/capacity${toQuery(period)}`),
  saveCapacity: (body: { date: string; vehiclesCommitted: number; vehiclesAvailable: number }) =>
    http.put<CapacityDay>('/api/v1/vendor/capacity', body),
};

/** Opens a stored POD file. The file endpoint needs the bearer token, so it is fetched and opened as a blob. */
export async function openPodFile(id: number, vendor: boolean): Promise<void> {
  const path = vendor ? `/api/v1/vendor/pods/${id}/file` : `/api/v1/pods/${id}/file`;
  const blob = await fetchBlob(path);
  window.open(URL.createObjectURL(blob), '_blank', 'noopener');
}
