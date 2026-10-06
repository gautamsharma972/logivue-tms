import { http } from './client'
import type {
  ListTrackedParams, TrackedSummaryDto, TrackedDetailDto, TimelineEntryDto, TrackEtaDto, TrackRouteDto, TrackLocationDto, CurrentLocationDto, VehicleTrackingDto, TrackAlertDto, TrackExceptionSummaryDto,
  TrackExceptionDto, ControlTowerSummaryDto, GeofenceDto, SaveGeofenceRequest, TrackingHealthDto, JourneyAnalyticsDto, CustomerLinkDto, CreatedLinkDto, CustomerTrackingDto, TrackingSettingDto,
  ReplayDto, TrackingComplianceDto, MobileTripDto, TrackingSessionDto, QueuedFix, TrackBatchResult, DelayReason, TrackAlertStatus, TrackSeverity, TrackAlertType, TrackExceptionStatus,
  AgeingDto,
  BranchDto,
  ContactDto,
  SaveBranchRequest,
  SaveContactRequest,
  AlertDto,
  AlertSeverity,
  AlertStatus,
  CapacityDayDto,
  ClaimDto,
  ClaimType,
  LoadCostDto,
  PlacementDto,
  PlacementStatus,
  CandidateEvaluationDto,
  CapabilityDto,
  CapabilityTypeDto,
  PlanningRuleDto,
  PlanningRuleType,
  RecommendationResultDto,
  SelectionRequest,
  BenchmarkDto,
  ExecutionDto,
  ExecutionEventType,
  LaneDto,
  PerformanceDto,
  RankingParams,
  RankingResultDto,
  SaveLaneRequest,
  ScorecardDto,
  TransporterSettingDto,
  CommitMilkRunResult,
  ModeChoice,
  LockKind,
  CompatibilityRuleDto,
  SaveCompatibilityRuleRequest,
  ListPodParams,
  PodDocumentDto,
  PodLineDto,
  RecordDeliveryRequest,
  DashboardDto,
  MilkRunDto,
  MilkRunPlan,
  SaveMilkRunRequest,
  EditPlanRequest,
  DistanceDto,
  ListLocationsParams,
  LocationDto,
  SaveLocationRequest,
  ComparisonDto,
  PlanOptions,
  PlanSnapshot,
  RunDto,
  RunSummaryDto,
  RunVersionDto,
  AdviceDto,
  AdviceRequest,
  CreateShipmentRequest,
  FleetOptionsDto,
  ListOrdersParams,
  ListShipmentsParams,
  OrderDto,
  ShipmentDto,
  ShipmentQuotesDto,
  ProofAgeingDto,
  AgeingItemDto,
  AgeingStage,
  BillingStatusDto,
  ClaimResultDto,
  ProofComplianceDto,
  DashboardSummaryDto,
  DeliveryNotificationDto,
  ProofPerformanceDto,
  AttemptRequest,
  CompleteDeliveryRequest,
  DeliveryDto,
  DeliveryExceptionDto,
  DeliveryExceptionSummaryDto,
  DeliverySettingDto,
  DeliverySummaryDto,
  FailDeliveryRequest,
  ListDeliveriesParams,
  ListDeliveryExceptionsParams,
  ListPodsParams,
  MobileBundleDto,
  PodDto,
  PodEvidenceDto,
  PodReviewDto,
  PodSummaryDto,
  RefuseDeliveryRequest,
  SaveDeliveryRequest,
  SyncCommand,
  SyncResultDto,
  DeviceContext,
  EvidenceType,
  OcrResultDto,
  ProofRequest,
  MasterEntryDto,
  SaveMasterItemRequest,
  DocumentRuleDto,
  SaveDocumentRuleRequest,
  StartTenderRequest,
  TenderDto,
  ShipmentSummaryDto,
  SaveOrderRequest,
  SuggestedLoadDto,
  UpdateShipmentPlanRequest,
  UtilizationDto,
  AuditLogDto,
  ContractDocumentDto,
  ContractDto,
  ContractSummaryDto,
  DieselPriceDto,
  ListContractsParams,
  QuoteRequest,
  QuoteResultDto,
  RateCardDto,
  RateInputDto,
  SaveContractRequest,
  ZoneDto,
  ZoneMember,
  ComplianceItemDto,
  DocumentDto,
  DriverDto,
  ListTransportersParams,
  OwnerKind,
  SaveBankRequest,
  SaveDriverRequest,
  SaveTransporterRequest,
  SaveVehicleRequest,
  TransporterDto,
  TransporterLookupDto,
  TransporterSummaryDto,
  UploadDocumentInput,
  VehicleDto,
  SaveVehicleTypeRequest,
  VehicleTypeDto,
  CreateDelegationRequest,
  DelegationDto,
  DelegationsDto,
  ListRequestsParams,
  PolicyDto,
  RequestDto,
  RequestSummaryDto,
  SavePolicyRequest,
  UserLookupDto,
  AuthResponse,
  CreateUserRequest,
  ListAuditLogsParams,
  ListUsersParams,
  PagedResult,
  PermissionDefinition,
  RoleDto,
  SaveRoleRequest,
  UpdateUserRequest,
  UserDto,
  UserProfile,
} from './types'

const v1 = '/api/v1'

export const authApi = {
  login: (body: { tenantCode: string; email: string; password: string }) =>
    http.post<AuthResponse>(`${v1}/auth/login`, body).then((r) => r.data),
  logout: () => http.post(`${v1}/auth/logout`),
  changePassword: (body: { currentPassword: string; newPassword: string }) =>
    http.post<AuthResponse>(`${v1}/auth/change-password`, body).then((r) => r.data),
  forgotPassword: (body: { tenantCode: string; email: string }) => http.post(`${v1}/auth/forgot-password`, body),
  resetPassword: (body: { token: string; newPassword: string }) => http.post(`${v1}/auth/reset-password`, body),
  me: () => http.get<UserProfile>(`${v1}/auth/me`).then((r) => r.data),
}

export const usersApi = {
  list: (params: ListUsersParams) =>
    http.get<PagedResult<UserDto>>(`${v1}/users`, { params }).then((r) => r.data),
  lookup: (search?: string) =>
    http.get<UserLookupDto[]>(`${v1}/users/lookup`, { params: { search } }).then((r) => r.data),
  create: (body: CreateUserRequest) => http.post<UserDto>(`${v1}/users`, body).then((r) => r.data),
  update: (id: string, body: UpdateUserRequest) =>
    http.put<UserDto>(`${v1}/users/${id}`, body).then((r) => r.data),
}

export const rolesApi = {
  list: () => http.get<RoleDto[]>(`${v1}/roles`).then((r) => r.data),
  create: (body: SaveRoleRequest) => http.post<RoleDto>(`${v1}/roles`, body).then((r) => r.data),
  update: (id: string, body: SaveRoleRequest) =>
    http.put<RoleDto>(`${v1}/roles/${id}`, body).then((r) => r.data),
  permissions: () => http.get<PermissionDefinition[]>(`${v1}/permissions`).then((r) => r.data),
}

export const auditApi = {
  list: (params: ListAuditLogsParams) =>
    http.get<PagedResult<AuditLogDto>>(`${v1}/audit-logs`, { params }).then((r) => r.data),
}

export const approvalsApi = {
  requests: (params: ListRequestsParams) =>
    http.get<PagedResult<RequestSummaryDto>>(`${v1}/approvals/requests`, { params }).then((r) => r.data),
  request: (id: string) => http.get<RequestDto>(`${v1}/approvals/requests/${id}`).then((r) => r.data),
  approve: (id: string, comment?: string) =>
    http.post<RequestDto>(`${v1}/approvals/requests/${id}/approve`, { comment }).then((r) => r.data),
  reject: (id: string, comment: string) =>
    http.post<RequestDto>(`${v1}/approvals/requests/${id}/reject`, { comment }).then((r) => r.data),
  cancel: (id: string) => http.post<RequestDto>(`${v1}/approvals/requests/${id}/cancel`).then((r) => r.data),

  policies: () => http.get<PolicyDto[]>(`${v1}/approvals/policies`).then((r) => r.data),
  stepPermissions: () =>
    http.get<PermissionDefinition[]>(`${v1}/approvals/policies/permissions`).then((r) => r.data),
  savePolicy: (documentType: string, body: SavePolicyRequest) =>
    http.put<PolicyDto>(`${v1}/approvals/policies/${documentType}`, body).then((r) => r.data),

  delegations: () => http.get<DelegationsDto>(`${v1}/approvals/delegations`).then((r) => r.data),
  createDelegation: (body: CreateDelegationRequest) =>
    http.post<DelegationDto>(`${v1}/approvals/delegations`, body).then((r) => r.data),
  revokeDelegation: (id: string) => http.delete(`${v1}/approvals/delegations/${id}`),
}

const t = (id: string) => `${v1}/transporters/${id}`

export const transportersApi = {
  list: (params: ListTransportersParams) =>
    http.get<PagedResult<TransporterSummaryDto>>(`${v1}/transporters`, { params }).then((r) => r.data),
  lookup: (search?: string) =>
    http.get<TransporterLookupDto[]>(`${v1}/transporters/lookup`, { params: { search } }).then((r) => r.data),
  get: (id: string) => http.get<TransporterDto>(t(id)).then((r) => r.data),
  create: (body: SaveTransporterRequest) => http.post<TransporterDto>(`${v1}/transporters`, body).then((r) => r.data),
  update: (id: string, body: SaveTransporterRequest) => http.put<TransporterDto>(t(id), body).then((r) => r.data),
  updateBank: (id: string, body: SaveBankRequest) => http.put<TransporterDto>(`${t(id)}/bank`, body).then((r) => r.data),
  submit: (id: string) => http.post<TransporterDto>(`${t(id)}/submit`).then((r) => r.data),
  suspend: (id: string, reason: string) => http.post<TransporterDto>(`${t(id)}/suspend`, { reason }).then((r) => r.data),
  reactivate: (id: string) => http.post<TransporterDto>(`${t(id)}/reactivate`).then((r) => r.data),

  vehicleTypes: () => http.get<VehicleTypeDto[]>(`${v1}/vehicle-types`).then((r) => r.data),
  createVehicleType: (body: SaveVehicleTypeRequest) => http.post<VehicleTypeDto>(`${v1}/vehicle-types`, body).then((r) => r.data),
  updateVehicleType: (id: string, body: SaveVehicleTypeRequest) => http.put<VehicleTypeDto>(`${v1}/vehicle-types/${id}`, body).then((r) => r.data),
  vehicles: (id: string, params: { search?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<VehicleDto>>(`${t(id)}/vehicles`, { params }).then((r) => r.data),
  createVehicle: (id: string, body: SaveVehicleRequest) => http.post<VehicleDto>(`${t(id)}/vehicles`, body).then((r) => r.data),
  updateVehicle: (vehicleId: string, body: SaveVehicleRequest) => http.put<VehicleDto>(`${v1}/vehicles/${vehicleId}`, body).then((r) => r.data),
  drivers: (id: string, params: { search?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<DriverDto>>(`${t(id)}/drivers`, { params }).then((r) => r.data),
  createDriver: (id: string, body: SaveDriverRequest) => http.post<DriverDto>(`${t(id)}/drivers`, body).then((r) => r.data),
  updateDriver: (driverId: string, body: SaveDriverRequest) => http.put<DriverDto>(`${v1}/drivers/${driverId}`, body).then((r) => r.data),

  documents: (id: string, params?: { ownerKind?: OwnerKind; ownerId?: string; includeSuperseded?: boolean }) =>
    http.get<DocumentDto[]>(`${t(id)}/documents`, { params }).then((r) => r.data),
  uploadDocument: (id: string, input: UploadDocumentInput) => {
    const form = new FormData()
    form.set('ownerKind', input.ownerKind)
    form.set('ownerId', input.ownerId)
    form.set('kind', input.kind)
    if (input.number) form.set('number', input.number)
    if (input.issuedOn) form.set('issuedOn', input.issuedOn)
    if (input.expiresOn) form.set('expiresOn', input.expiresOn)
    form.set('file', input.file)
    return http.post<DocumentDto>(`${t(id)}/documents`, form).then((r) => r.data)
  },
  deleteDocument: (documentId: string) => http.delete(`${v1}/documents/${documentId}`),
  /** Fetched with the bearer token (a plain link would be unauthenticated), then handed to the browser as a file. */
  downloadDocument: async (documentId: string, fileName: string) => {
    const { data } = await http.get<Blob>(`${v1}/documents/${documentId}/file`, { responseType: 'blob' })
    const url = URL.createObjectURL(data)
    const link = Object.assign(document.createElement('a'), { href: url, download: fileName })
    link.click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },
  compliance: (params: { withinDays?: number; page?: number; pageSize?: number }) =>
    http.get<PagedResult<ComplianceItemDto>>(`${v1}/transporters/compliance`, { params }).then((r) => r.data),
}

const c = (id: string) => `${v1}/contracts/${id}`

export const contractsApi = {
  list: (params: ListContractsParams) => http.get<PagedResult<ContractSummaryDto>>(`${v1}/contracts`, { params }).then((r) => r.data),
  expiring: (withinDays: number, pageSize = 50) =>
    http.get<PagedResult<ContractSummaryDto>>(`${v1}/contracts/expiring`, { params: { withinDays, pageSize } }).then((r) => r.data),
  get: (id: string) => http.get<ContractDto>(c(id)).then((r) => r.data),
  create: (body: SaveContractRequest) => http.post<ContractDto>(`${v1}/contracts`, body).then((r) => r.data),
  update: (id: string, body: SaveContractRequest) => http.put<ContractDto>(c(id), body).then((r) => r.data),
  rates: (id: string) => http.get<RateCardDto[]>(`${c(id)}/rates`).then((r) => r.data),
  saveRates: (id: string, rates: RateInputDto[], version: number) =>
    http.put<ContractDto>(`${c(id)}/rates`, { rates, version }).then((r) => r.data),
  submit: (id: string) => http.post<ContractDto>(`${c(id)}/submit`).then((r) => r.data),
  terminate: (id: string, reason: string) => http.post<ContractDto>(`${c(id)}/terminate`, { reason }).then((r) => r.data),
  revise: (id: string, effectiveFrom: string, effectiveTo: string) =>
    http.post<ContractDto>(`${c(id)}/revise`, { effectiveFrom, effectiveTo }).then((r) => r.data),

  documents: (id: string) => http.get<ContractDocumentDto[]>(`${c(id)}/documents`).then((r) => r.data),
  uploadDocument: (id: string, input: { kind: string; title: string; file: File }) => {
    const form = new FormData()
    form.set('kind', input.kind)
    form.set('title', input.title)
    form.set('file', input.file)
    return http.post<ContractDocumentDto>(`${c(id)}/documents`, form).then((r) => r.data)
  },
  deleteDocument: (documentId: string) => http.delete(`${v1}/contract-documents/${documentId}`),
  downloadDocument: async (documentId: string, fileName: string) => {
    const { data } = await http.get<Blob>(`${v1}/contract-documents/${documentId}/file`, { responseType: 'blob' })
    const url = URL.createObjectURL(data)
    Object.assign(document.createElement('a'), { href: url, download: fileName }).click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  },

  vehicleTypes: () => http.get<{ id: string; code: string; name: string; payloadKg: number }[]>(`${v1}/contracts/lookups/vehicle-types`).then((r) => r.data),
  quote: (body: QuoteRequest) => http.post<QuoteResultDto>(`${v1}/freight/quote`, body).then((r) => r.data),

  zones: () => http.get<ZoneDto[]>(`${v1}/zones`).then((r) => r.data),
  createZone: (body: { code: string; name: string; members: ZoneMember[] }) => http.post<ZoneDto>(`${v1}/zones`, { ...body, version: null }).then((r) => r.data),
  updateZone: (id: string, body: { code: string; name: string; members: ZoneMember[]; version: number }) =>
    http.put<ZoneDto>(`${v1}/zones/${id}`, body).then((r) => r.data),
  dieselPrices: (region?: string) => http.get<DieselPriceDto[]>(`${v1}/diesel-prices`, { params: { region } }).then((r) => r.data),
  addDieselPrice: (body: { region: string; effectiveFrom: string; pricePerLitre: number }) =>
    http.post<DieselPriceDto>(`${v1}/diesel-prices`, body).then((r) => r.data),
}

const sh = (id: string) => `${v1}/shipments/${id}`

export const ordersApi = {
  list: (params: ListOrdersParams) => http.get<PagedResult<OrderDto>>(`${v1}/orders`, { params }).then((r) => r.data),
  create: (body: SaveOrderRequest) => http.post<OrderDto>(`${v1}/orders`, body).then((r) => r.data),
  update: (id: string, body: SaveOrderRequest) => http.put<OrderDto>(`${v1}/orders/${id}`, body).then((r) => r.data),
  cancel: (id: string, reason: string) => http.post<OrderDto>(`${v1}/orders/${id}/cancel`, { reason }).then((r) => r.data),
}

export const performanceApi = {
  get: (id: string, from: string, to: string) => http.get<PerformanceDto>(`${v1}/transporters/${id}/performance`, { params: { from, to } }).then((r) => r.data),
  recalculate: (id: string, from: string, to: string) => http.post(`${v1}/transporters/${id}/performance/recalculate`, { from, to }).then((r) => r.data),
  scorecards: (id: string) => http.get<ScorecardDto[]>(`${v1}/transporters/${id}/scorecards`).then((r) => r.data),
  generateScorecard: (id: string, from: string, to: string) => http.post<ScorecardDto>(`${v1}/transporters/${id}/scorecards`, { from, to }).then((r) => r.data),
  rankings: (params: RankingParams) => http.get<RankingResultDto>(`${v1}/transporters/rankings`, { params }).then((r) => r.data),
  benchmark: (id: string, from: string, to: string, laneId?: string) => http.get<BenchmarkDto>(`${v1}/transporters/${id}/benchmark`, { params: { from, to, laneId } }).then((r) => r.data),
  executions: (id: string) => http.get<ExecutionDto[]>(`${v1}/transporters/${id}/executions`).then((r) => r.data),
  recordEvent: (executionId: string, eventType: ExecutionEventType, eventAt: string, delayReasonCode?: string, remarks?: string) =>
    http.post<ExecutionDto>(`${v1}/executions/${executionId}/events`, { eventType, eventAt, delayReasonCode, remarks }).then((r) => r.data),
  attributeDelay: (executionId: string, delivery: boolean, reasonCode: string) =>
    http.post<ExecutionDto>(`${v1}/executions/${executionId}/delay`, { delivery, reasonCode }).then((r) => r.data),
  lanes: (id: string) => http.get<LaneDto[]>(`${v1}/transporters/${id}/lanes`).then((r) => r.data),
  createLane: (id: string, body: SaveLaneRequest) => http.post<LaneDto>(`${v1}/transporters/${id}/lanes`, body).then((r) => r.data),
  updateLane: (laneId: string, body: SaveLaneRequest) => http.put<LaneDto>(`${v1}/lanes/${laneId}`, body).then((r) => r.data),
  capabilityCatalog: () => http.get<CapabilityTypeDto[]>(`${v1}/transporters/capability-catalog`).then((r) => r.data),
  capabilities: (id: string) => http.get<CapabilityDto[]>(`${v1}/transporters/${id}/capabilities`).then((r) => r.data),
  addCapability: (id: string, code: string, effectiveFrom: string) =>
    http.post<CapabilityDto>(`${v1}/transporters/${id}/capabilities`, { code, effectiveFrom, effectiveTo: null }).then((r) => r.data),
  endCapability: (capabilityId: string) => http.post<CapabilityDto>(`${v1}/capabilities/${capabilityId}/end`).then((r) => r.data),
  planningRules: (id: string) => http.get<PlanningRuleDto[]>(`${v1}/transporters/${id}/planning-rules`).then((r) => r.data),
  addPlanningRule: (id: string, body: { ruleType: PlanningRuleType; laneId: string | null; reason: string; effectiveFrom: string; effectiveTo: string | null }) =>
    http.post<PlanningRuleDto>(`${v1}/transporters/${id}/planning-rules`, body).then((r) => r.data),
  endPlanningRule: (ruleId: string, reason: string) => http.post<PlanningRuleDto>(`${v1}/planning-rules/${ruleId}/end`, { reason }).then((r) => r.data),
  eligibility: (body: SelectionRequest) => http.post<CandidateEvaluationDto[]>(`${v1}/transporters/eligibility`, body).then((r) => r.data),
  recommend: (body: SelectionRequest) => http.post<RecommendationResultDto>(`${v1}/transporters/recommendation`, body).then((r) => r.data),
  contacts: (id: string) => http.get<ContactDto[]>(`${v1}/transporters/${id}/contacts`).then((r) => r.data),
  addContact: (id: string, body: SaveContactRequest) => http.post<ContactDto>(`${v1}/transporters/${id}/contacts`, body).then((r) => r.data),
  updateContact: (contactId: string, body: SaveContactRequest) => http.put<ContactDto>(`${v1}/contacts/${contactId}`, body).then((r) => r.data),
  branches: (id: string) => http.get<BranchDto[]>(`${v1}/transporters/${id}/branches`).then((r) => r.data),
  addBranch: (id: string, body: SaveBranchRequest) => http.post<BranchDto>(`${v1}/transporters/${id}/branches`, body).then((r) => r.data),
  updateBranch: (branchId: string, body: SaveBranchRequest) => http.put<BranchDto>(`${v1}/branches/${branchId}`, body).then((r) => r.data),
  placements: (params: { transporterId?: string; status?: PlacementStatus; page?: number; pageSize?: number }) =>
    http.get<PagedResult<PlacementDto>>(`${v1}/placements`, { params }).then((r) => r.data),
  reportPlacement: (id: string) => http.post<PlacementDto>(`${v1}/placements/${id}/report`).then((r) => r.data),
  placePlacement: (id: string) => http.post<PlacementDto>(`${v1}/placements/${id}/place`).then((r) => r.data),
  noShow: (id: string, reason: string) => http.post<PlacementDto>(`${v1}/placements/${id}/no-show`, { reason }).then((r) => r.data),
  cancelPlacement: (id: string, reason: string) => http.post<PlacementDto>(`${v1}/placements/${id}/cancel`, { reason }).then((r) => r.data),
  claims: (id: string, from: string, to: string) => http.get<ClaimDto[]>(`${v1}/transporters/${id}/claims`, { params: { from, to } }).then((r) => r.data),
  recordClaim: (id: string, body: { claimType: ClaimType; claimDate: string; claimValue: number; shipmentId: string | null; remarks: string | null }) =>
    http.post<ClaimDto>(`${v1}/transporters/${id}/claims`, body).then((r) => r.data),
  setClaimValue: (claimId: string, claimValue: number) => http.put<ClaimDto>(`${v1}/claims/${claimId}/value`, { claimValue }).then((r) => r.data),
  resolveClaim: (claimId: string) => http.post<ClaimDto>(`${v1}/claims/${claimId}/resolve`).then((r) => r.data),
  costs: (id: string, from: string, to: string) => http.get<LoadCostDto[]>(`${v1}/transporters/${id}/costs`, { params: { from, to } }).then((r) => r.data),
  recordCost: (id: string, shipmentId: string, invoicedAmount: number, agreedAmount?: number) =>
    http.post<LoadCostDto>(`${v1}/transporters/${id}/costs`, { shipmentId, invoicedAmount, agreedAmount }).then((r) => r.data),
  capacity: (id: string, from: string, to: string) => http.get<CapacityDayDto[]>(`${v1}/transporters/${id}/capacity`, { params: { from, to } }).then((r) => r.data),
  saveCapacity: (id: string, date: string, vehiclesCommitted: number, vehiclesAvailable: number) =>
    http.put<CapacityDayDto>(`${v1}/transporters/${id}/capacity`, { date, vehiclesCommitted, vehiclesAvailable }).then((r) => r.data),
  alerts: (params: { status?: AlertStatus; severity?: AlertSeverity; transporterId?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<AlertDto>>(`${v1}/transporter-alerts`, { params }).then((r) => r.data),
  acknowledgeAlert: (id: string) => http.post<AlertDto>(`${v1}/transporter-alerts/${id}/acknowledge`).then((r) => r.data),
  resolveAlert: (id: string, comments?: string) => http.post<AlertDto>(`${v1}/transporter-alerts/${id}/resolve`, { comments }).then((r) => r.data),
  settings: () => http.get<TransporterSettingDto[]>(`${v1}/transporter-settings`).then((r) => r.data),
  saveSetting: (key: string, value: unknown) => http.put<TransporterSettingDto>(`${v1}/transporter-settings/${key}`, { value }).then((r) => r.data),
}

export const milkRunsApi = {
  list: (params: { search?: string; active?: boolean; page?: number; pageSize?: number }) =>
    http.get<PagedResult<MilkRunDto>>(`${v1}/planning/milk-runs`, { params }).then((r) => r.data),
  create: (body: SaveMilkRunRequest) => http.post<MilkRunDto>(`${v1}/planning/milk-runs`, body).then((r) => r.data),
  update: (id: string, body: SaveMilkRunRequest) => http.put<MilkRunDto>(`${v1}/planning/milk-runs/${id}`, body).then((r) => r.data),
  preview: (milkRunId: string, date: string, keepTemplateOrder: boolean) =>
    http.post<MilkRunPlan>(`${v1}/planning/milk-runs/preview`, { milkRunId, date, keepTemplateOrder }).then((r) => r.data),
  /** Makes a draft shipment for each trip of the day's plan, from the orders that are still open. */
  commit: (milkRunId: string, date: string, keepTemplateOrder: boolean) =>
    http.post<CommitMilkRunResult>(`${v1}/planning/milk-runs/commit`, { milkRunId, date, keepTemplateOrder }).then((r) => r.data),
}

/** Fetches a file with the sign-in token attached and saves it, naming it from the server's header when present. */
export async function downloadFile(url: string, fallbackName: string, params?: Record<string, string | undefined>): Promise<void> {
  const response = await http.get<Blob>(url, { params, responseType: 'blob' })
  const header = String(response.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header)
  const link = document.createElement('a')
  link.href = URL.createObjectURL(response.data)
  link.download = match?.[1] ? decodeURIComponent(match[1]) : fallbackName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(link.href)
}

export const planningApi = {
  dashboard: (from?: string, to?: string) => http.get<DashboardDto>(`${v1}/planning/dashboard`, { params: { from, to } }).then((r) => r.data),
  exportDashboard: (format: 'csv' | 'xlsx' | 'pdf', from?: string, to?: string) =>
    downloadFile(`${v1}/planning/dashboard/export`, `planning-kpis.${format}`, { format, from, to }),
  exportRun: (id: string, format: 'csv' | 'xlsx' | 'pdf', number: string) => downloadFile(`${v1}/planning/runs/${id}/export`, `${number}.${format}`, { format }),
  preview: (planningDate: string, orderIds: string[], options: PlanOptions) =>
    http.post<PlanSnapshot>(`${v1}/planning/preview`, { planningDate, orderIds, options }).then((r) => r.data),
  /** `background` returns at once with a Running plan that finishes on the server; follow it with `run`. */
  createRun: (planningDate: string, orderIds: string[], options: PlanOptions, background = false, modeChoice?: ModeChoice) =>
    http.post<RunDto>(`${v1}/planning/runs`, { planningDate, orderIds, options, background, modeChoice }).then((r) => r.data),
  runs: (params: { status?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<RunSummaryDto>>(`${v1}/planning/runs`, { params }).then((r) => r.data),
  run: (id: string) => http.get<RunDto>(`${v1}/planning/runs/${id}`).then((r) => r.data),
  versions: (id: string) => http.get<RunVersionDto[]>(`${v1}/planning/runs/${id}/versions`).then((r) => r.data),
  reoptimize: (id: string, reason: string, options: PlanOptions | null) =>
    http.post<RunDto>(`${v1}/planning/runs/${id}/reoptimize`, { reason, options }).then((r) => r.data),
  edit: (id: string, body: EditPlanRequest) => http.post<RunDto>(`${v1}/planning/runs/${id}/edit`, body).then((r) => r.data),
  lockVehicle: (id: string, vehicleKey: string, locked: boolean, kind: LockKind = 'Vehicle', orderId?: string) =>
    http.post<RunDto>(`${v1}/planning/runs/${id}/lock`, { vehicleKey, locked, kind, orderId }).then((r) => r.data),
  approve: (id: string) => http.post<RunDto>(`${v1}/planning/runs/${id}/approve`).then((r) => r.data),
  commit: (id: string) => http.post<RunDto>(`${v1}/planning/runs/${id}/commit`).then((r) => r.data),
  cancelRun: (id: string, reason: string) => http.post<RunDto>(`${v1}/planning/runs/${id}/cancel`, { reason }).then((r) => r.data),
  compare: (orderIds: string[], options: PlanOptions | null) =>
    http.post<ComparisonDto>(`${v1}/planning/ftl-ptl/compare`, { orderIds, options }).then((r) => r.data),
  vehicleTypes: () => http.get<{ id: string; code: string; name: string; payloadKg: number; volumeCbm: number | null }[]>(`${v1}/planning/vehicle-types`).then((r) => r.data),
  compatibilityRules: () => http.get<CompatibilityRuleDto[]>(`${v1}/planning/compatibility-rules`).then((r) => r.data),
  addCompatibilityRule: (body: SaveCompatibilityRuleRequest) => http.post<CompatibilityRuleDto>(`${v1}/planning/compatibility-rules`, body).then((r) => r.data),
  deleteCompatibilityRule: (id: string) => http.delete(`${v1}/planning/compatibility-rules/${id}`).then(() => undefined),
  advice: (body: AdviceRequest) => http.post<AdviceDto>(`${v1}/planning/advice`, body).then((r) => r.data),
  suggestions: () => http.get<SuggestedLoadDto[]>(`${v1}/planning/suggestions`).then((r) => r.data),
  utilization: (params: { from?: string; to?: string; transporterId?: string }) =>
    http.get<UtilizationDto>(`${v1}/planning/utilization`, { params }).then((r) => r.data),
}

export const shipmentsApi = {
  list: (params: ListShipmentsParams) => http.get<PagedResult<ShipmentSummaryDto>>(`${v1}/shipments`, { params }).then((r) => r.data),
  get: (id: string) => http.get<ShipmentDto>(sh(id)).then((r) => r.data),
  create: (body: CreateShipmentRequest) => http.post<ShipmentDto>(`${v1}/shipments`, body).then((r) => r.data),
  updatePlan: (id: string, body: UpdateShipmentPlanRequest) => http.put<ShipmentDto>(`${sh(id)}/plan`, body).then((r) => r.data),
  addOrders: (id: string, orderIds: string[]) => http.post<ShipmentDto>(`${sh(id)}/orders`, { orderIds }).then((r) => r.data),
  removeOrder: (id: string, orderId: string) => http.delete<ShipmentDto>(`${sh(id)}/orders/${orderId}`).then((r) => r.data),
  sequence: (id: string, orderIds: string[]) => http.put<ShipmentDto>(`${sh(id)}/sequence`, { orderIds }).then((r) => r.data),
  quotes: (id: string) => http.get<ShipmentQuotesDto>(`${sh(id)}/quotes`).then((r) => r.data),
  tender: (id: string, contractId: string, overrideReason: string | null) =>
    http.post<ShipmentDto>(`${sh(id)}/tender`, { contractId, overrideReason }).then((r) => r.data),
  withdraw: (id: string) => http.post<ShipmentDto>(`${sh(id)}/withdraw`).then((r) => r.data),
  fleetOptions: (id: string) => http.get<FleetOptionsDto>(`${sh(id)}/fleet-options`).then((r) => r.data),
  accept: (id: string, vehicleId: string, driverId: string) => http.post<ShipmentDto>(`${sh(id)}/accept`, { vehicleId, driverId }).then((r) => r.data),
  reassign: (id: string, vehicleId: string, driverId: string) => http.post<ShipmentDto>(`${sh(id)}/reassign`, { vehicleId, driverId }).then((r) => r.data),
  reject: (id: string, reason: string) => http.post<ShipmentDto>(`${sh(id)}/reject`, { reason }).then((r) => r.data),
  dispatch: (id: string) => http.post<ShipmentDto>(`${sh(id)}/dispatch`).then((r) => r.data),
  deliver: (id: string) => http.post<ShipmentDto>(`${sh(id)}/deliver`).then((r) => r.data),
  cancel: (id: string, reason: string) => http.post<ShipmentDto>(`${sh(id)}/cancel`, { reason }).then((r) => r.data),
  tenders: (id: string) => http.get<TenderDto[]>(`${sh(id)}/tenders`).then((r) => r.data),
  startTender: (id: string, body: StartTenderRequest) => http.post<TenderDto>(`${sh(id)}/tenders`, body).then((r) => r.data),
  awardTender: (id: string, inviteeId: string) => http.post<TenderDto>(`${sh(id)}/tenders/award`, { inviteeId }).then((r) => r.data),
  decideCounter: (id: string, inviteeId: string, agree: boolean, comments: string | null) =>
    http.post<TenderDto>(`${sh(id)}/tenders/counter-decision`, { inviteeId, agree, comments }).then((r) => r.data),
  cancelTender: (id: string, reason: string) => http.post<TenderDto>(`${sh(id)}/tenders/cancel`, { reason }).then((r) => r.data),
  bid: (id: string, body: { vehicleId: string; driverId: string; counterRate: number | null; comments: string | null }) =>
    http.post<TenderDto>(`${sh(id)}/tenders/bid`, body).then((r) => r.data),
  counterOffer: (id: string, rate: number, comments: string | null) => http.post<TenderDto>(`${sh(id)}/tenders/counter`, { rate, comments }).then((r) => r.data),
  declineTender: (id: string, reason: string) => http.post<TenderDto>(`${sh(id)}/tenders/decline`, { reason }).then((r) => r.data),
}

export const locationsApi = {
  list: (params: ListLocationsParams) => http.get<PagedResult<LocationDto>>(`${v1}/locations`, { params }).then((r) => r.data),
  create: (body: SaveLocationRequest) => http.post<LocationDto>(`${v1}/locations`, body).then((r) => r.data),
  update: (id: string, body: SaveLocationRequest) => http.put<LocationDto>(`${v1}/locations/${id}`, body).then((r) => r.data),
  distance: (fromLocationId: string, toLocationId: string) =>
    http.post<DistanceDto>(`${v1}/locations/distance`, { fromLocationId, toLocationId }).then((r) => r.data),
}

const order = (shipmentId: string, orderId: string) => `${v1}/shipments/${shipmentId}/orders/${orderId}`

export const deliveryApi = {
  record: (shipmentId: string, orderId: string, body: RecordDeliveryRequest) => http.post<ShipmentDto>(`${order(shipmentId, orderId)}/delivery`, body).then((r) => r.data),
  documents: (shipmentId: string, orderId: string) => http.get<PodDocumentDto[]>(`${order(shipmentId, orderId)}/pod`).then((r) => r.data),
  upload: (shipmentId: string, orderId: string, file: File) => {
    const form = new FormData()
    form.set('file', file)
    return http.post<PodDocumentDto>(`${order(shipmentId, orderId)}/pod`, form).then((r) => r.data)
  },
  download: (doc: PodDocumentDto) => downloadFile(`${v1}/pod-documents/${doc.id}/file`, doc.fileName),
  remove: (documentId: string) => http.delete(`${v1}/pod-documents/${documentId}`),
  verify: (shipmentId: string, orderId: string) => http.post<ShipmentDto>(`${order(shipmentId, orderId)}/pod/verify`).then((r) => r.data),
  reject: (shipmentId: string, orderId: string, reason: string) => http.post<ShipmentDto>(`${order(shipmentId, orderId)}/pod/reject`, { reason }).then((r) => r.data),
  queue: (params: ListPodParams) => http.get<PagedResult<PodLineDto>>(`${v1}/pod`, { params }).then((r) => r.data),
  ageing: (overdueDays?: number) => http.get<AgeingDto>(`${v1}/pod/ageing`, { params: { overdueDays } }).then((r) => r.data),
}

export const proofPerformanceApi = {
  get: (transporterId: string, from?: string, to?: string) => http.get<ProofPerformanceDto>(`${v1}/transporters/${transporterId}/proof-performance`, { params: { from, to } }).then((r) => r.data),
}

/** Master lists (transporter types, capabilities) and the rules for compliance papers. */
export const masterDataApi = {
  types: () => http.get<MasterEntryDto[]>(`${v1}/transporters/types`).then((r) => r.data),
  saveType: (body: SaveMasterItemRequest) => http.put<MasterEntryDto>(`${v1}/transporters/types`, body).then((r) => r.data),
  capabilityTypes: () => http.get<MasterEntryDto[]>(`${v1}/transporters/capability-types`).then((r) => r.data),
  saveCapabilityType: (body: SaveMasterItemRequest) => http.put<MasterEntryDto>(`${v1}/transporters/capability-types`, body).then((r) => r.data),
  documentRules: () => http.get<DocumentRuleDto[]>(`${v1}/transporters/document-rules`).then((r) => r.data),
  saveDocumentRule: (kind: string, body: SaveDocumentRuleRequest) => http.put<DocumentRuleDto>(`${v1}/transporters/document-rules/${kind}`, body).then((r) => r.data),
}

const dl = (id: string) => `${v1}/deliveries/${id}`
const pd = (id: string) => `${v1}/pods/${id}`

/** Delivery execution, proof of delivery, exceptions and the mobile app's synchronisation. */
export const deliveriesApi = {
  list: (params: ListDeliveriesParams) => http.get<PagedResult<DeliverySummaryDto>>(`${v1}/deliveries`, { params }).then((r) => r.data),
  get: (id: string) => http.get<DeliveryDto>(dl(id)).then((r) => r.data),
  create: (body: SaveDeliveryRequest) => http.post<DeliveryDto>(`${v1}/deliveries`, body).then((r) => r.data),
  assign: (id: string, body: { transporterId: string; transporterReference: string | null; vehicleId: string | null; vehicleReference: string | null; driverName: string | null }) =>
    http.post<DeliveryDto>(`${dl(id)}/assign`, body).then((r) => r.data),
  start: (id: string, context: DeviceContext | null) => http.post<DeliveryDto>(`${dl(id)}/start`, context).then((r) => r.data),
  arrive: (id: string, context: DeviceContext | null) => http.post<DeliveryDto>(`${dl(id)}/arrive`, context).then((r) => r.data),
  verifyOtp: (id: string, code: string, context: DeviceContext | null) => http.post<DeliveryDto>(`${dl(id)}/otp/verify`, { code, context }).then((r) => r.data),
  issueOtp: (id: string, context: DeviceContext | null) => http.post<DeliveryDto>(`${dl(id)}/otp/issue`, context).then((r) => r.data),
  attempt: (id: string, body: AttemptRequest) => http.post<DeliveryDto>(`${dl(id)}/attempt`, body).then((r) => r.data),
  complete: (id: string, body: CompleteDeliveryRequest) => http.post<DeliveryDto>(`${dl(id)}/complete`, body).then((r) => r.data),
  fail: (id: string, body: FailDeliveryRequest) => http.post<DeliveryDto>(`${dl(id)}/fail`, body).then((r) => r.data),
  refuse: (id: string, body: RefuseDeliveryRequest) => http.post<DeliveryDto>(`${dl(id)}/refuse`, body).then((r) => r.data),
  reschedule: (id: string, plannedDeliveryAt: string) => http.post<DeliveryDto>(`${dl(id)}/reschedule`, { plannedDeliveryAt, windowStart: null, windowEnd: null }).then((r) => r.data),
  cancel: (id: string, reason: string) => http.post<DeliveryDto>(`${dl(id)}/cancel`, { reason }).then((r) => r.data),
  close: (id: string, reason: string) => http.post<DeliveryDto>(`${dl(id)}/close`, { reason }).then((r) => r.data),
  startPod: (id: string) => http.post<PodDto>(`${dl(id)}/pod`).then((r) => r.data),
  createClaims: (id: string, discrepancyIds: string[] | null) => http.post<ClaimResultDto[]>(`${dl(id)}/claims`, { discrepancyIds }).then((r) => r.data),
  billing: (id: string) => http.get<BillingStatusDto>(`${dl(id)}/billing`).then((r) => r.data),

  dashboard: (params: { from?: string; to?: string; transporterId?: string }) => http.get<DashboardSummaryDto>(`${v1}/pod-dashboard/summary`, { params }).then((r) => r.data),
  ageing: (transporterId?: string) => http.get<ProofAgeingDto>(`${v1}/pod-dashboard/ageing`, { params: { transporterId } }).then((r) => r.data),
  ageingItems: (params: { stage?: AgeingStage; bucket?: number; overdueOnly?: boolean; transporterId?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<AgeingItemDto>>(`${v1}/pod-dashboard/ageing/items`, { params }).then((r) => r.data),
  compliance: (params: { from?: string; to?: string; groupBy?: string; transporterId?: string; customer?: string; lane?: string; vehicle?: string; serviceType?: string }) => http.get<ProofComplianceDto>(`${v1}/pod-dashboard/compliance`, { params }).then((r) => r.data),
  downloadReport: (report: string, format: 'csv' | 'xlsx', params: { from?: string; to?: string; transporterId?: string; customer?: string; groupBy?: string; lane?: string; vehicle?: string; serviceType?: string }) =>
    downloadFile(`${v1}/delivery-reports/${report}`, `${report}.${format}`, { ...params, format }),
  notifications: (params: { unreadOnly?: boolean; page?: number; pageSize?: number }) => http.get<PagedResult<DeliveryNotificationDto>>(`${v1}/delivery-notifications`, { params }).then((r) => r.data),
  readNotification: (id: string) => http.post(`${v1}/delivery-notifications/${id}/read`),
  readAllNotifications: () => http.post(`${v1}/delivery-notifications/read-all`),

  pods: (params: ListPodsParams) => http.get<PagedResult<PodSummaryDto>>(`${v1}/pods`, { params }).then((r) => r.data),
  reviewQueue: (params: ListPodsParams) => http.get<PagedResult<PodSummaryDto>>(`${v1}/pods/review-queue`, { params }).then((r) => r.data),
  pod: (id: string) => http.get<PodDto>(pd(id)).then((r) => r.data),
  podForReview: (id: string) => http.get<PodReviewDto>(`${pd(id)}/review`).then((r) => r.data),
  updateProof: (id: string, proof: ProofRequest) => http.put<PodDto>(`${pd(id)}/proof`, { proof }).then((r) => r.data),
  submitPod: (id: string) => http.post<PodDto>(`${pd(id)}/submit`).then((r) => r.data),
  validatePod: (id: string) => http.post<PodDto>(`${pd(id)}/validate`).then((r) => r.data),
  reviewPod: (id: string, action: 'accept' | 'reject' | 'resubmission', reason: string | null) => http.post<PodDto>(`${pd(id)}/review`, { action, reason }).then((r) => r.data),
  requestCorrection: (id: string, reason: string) => http.post<PodDto>(`${pd(id)}/correction`, { reason }).then((r) => r.data),
  addEvidence: (id: string, file: Blob, type: EvidenceType, fix: { latitude?: number | null; longitude?: number | null; accuracyM?: number | null } | null, clientRecordId: string, device: string | null) => {
    const form = new FormData()
    form.set('file', file, 'capture')
    form.set('type', type)
    form.set('clientRecordId', clientRecordId)
    if (device) form.set('deviceReference', device)
    if (fix?.latitude != null && fix.longitude != null) {
      form.set('latitude', String(fix.latitude))
      form.set('longitude', String(fix.longitude))
      if (fix.accuracyM != null) form.set('accuracyM', String(fix.accuracyM))
    }
    return http.post<PodEvidenceDto>(`${pd(id)}/evidence`, form, { headers: { 'Idempotency-Key': clientRecordId } }).then((r) => r.data)
  },
  removeEvidence: (id: string, evidenceId: string, reason: string) => http.delete<PodDto>(`${pd(id)}/evidence/${evidenceId}`, { params: { reason } }).then((r) => r.data),
  addSignature: (id: string, file: Blob, signerName: string) => {
    const form = new FormData()
    form.set('file', file, 'signature.png')
    form.set('signerName', signerName)
    return http.post(`${pd(id)}/signature`, form).then((r) => r.data)
  },
  evidenceUrl: (evidenceId: string) => `${v1}/pod-evidence/${evidenceId}/file`,
  signatureUrl: (signatureId: string) => `${v1}/pod-signatures/${signatureId}/file`,
  fetchFile: (url: string) => http.get<Blob>(url, { responseType: 'blob' }).then((r) => r.data),
  queueOcr: (id: string) => http.post<OcrResultDto>(`${pd(id)}/ocr`).then((r) => r.data),
  reviewOcrField: (id: string, field: string, value: string, reason: string) => http.post<PodDto>(`${pd(id)}/ocr/review`, { field, value, reason }).then((r) => r.data),

  exceptions: (params: ListDeliveryExceptionsParams) => http.get<PagedResult<DeliveryExceptionSummaryDto>>(`${v1}/delivery-exceptions`, { params }).then((r) => r.data),
  exception: (id: string) => http.get<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}`).then((r) => r.data),
  raiseException: (body: { deliveryId: string; type: string; description: string; severity: string | null }) => http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions`, body).then((r) => r.data),
  acknowledgeException: (id: string) => http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/acknowledge`).then((r) => r.data),
  assignException: (id: string, body: { ownerUserId: string | null; department: string | null; dueAt: string | null; severity: string | null }) =>
    http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/assign`, body).then((r) => r.data),
  escalateException: (id: string, reason: string) => http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/escalate`, { reason }).then((r) => r.data),
  noteException: (id: string, text: string) => http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/notes`, { text }).then((r) => r.data),
  resolveException: (id: string, body: { resolution: string; rootCause: string | null; responsibleParty: string; actionTaken: string | null; financialImpact: number | null; claimReference: string | null }) =>
    http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/resolve`, body).then((r) => r.data),
  attachToException: (id: string, file: File, note?: string) => {
    const form = new FormData()
    form.append('file', file)
    if (note) form.append('note', note)
    return http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/attachments`, form).then((r) => r.data)
  },
  downloadExceptionAttachment: (attachmentId: string, fileName: string) => downloadFile(`${v1}/exception-attachments/${attachmentId}/file`, fileName),
  closeException: (id: string) => http.post<DeliveryExceptionDto>(`${v1}/delivery-exceptions/${id}/close`).then((r) => r.data),

  mobileDeliveries: (transporterId?: string) => http.get<MobileBundleDto>(`${v1}/mobile/deliveries`, { params: { transporterId } }).then((r) => r.data),
  sync: (deviceId: string, commands: SyncCommand[]) => http.post<{ results: SyncResultDto[] }>(`${v1}/mobile/sync`, { deviceId, commands }).then((r) => r.data),

  settings: () => http.get<DeliverySettingDto[]>(`${v1}/delivery-settings`).then((r) => r.data),
  saveSetting: (key: string, value: unknown) => http.put<DeliverySettingDto>(`${v1}/delivery-settings/${key}`, value).then((r) => r.data),
}

const tr = (id: string) => `${v1}/tracking/shipments/${id}`
const ex = (id: string) => `${v1}/tracking/exceptions/${id}`

export const trackingApi = {
  summary: (transporterId?: string) => http.get<ControlTowerSummaryDto>(`${v1}/control-tower/summary`, { params: { transporterId } }).then((r) => r.data),
  list: (params: ListTrackedParams) => http.get<PagedResult<TrackedSummaryDto>>(`${v1}/tracking/shipments`, { params }).then((r) => r.data),
  map: () => http.get<TrackedSummaryDto[]>(`${v1}/control-tower/map`).then((r) => r.data),
  get: (id: string) => http.get<TrackedDetailDto>(tr(id)).then((r) => r.data),
  current: (id: string) => http.get<CurrentLocationDto | null>(`${tr(id)}/current-location`).then((r) => r.data),
  timeline: (id: string) => http.get<TimelineEntryDto[]>(`${tr(id)}/timeline`).then((r) => r.data),
  eta: (id: string) => http.get<TrackEtaDto>(`${tr(id)}/eta`).then((r) => r.data),
  route: (id: string) => http.get<TrackRouteDto>(`${tr(id)}/route`).then((r) => r.data),
  replay: (id: string, params: { from?: string; to?: string; maxPoints?: number }) => http.get<ReplayDto>(`${tr(id)}/replay`, { params }).then((r) => r.data),
  compliance: (params: { from?: string; to?: string; transporterId?: string; groupBy?: string }) => http.get<TrackingComplianceDto>(`${v1}/tracking/compliance`, { params }).then((r) => r.data),
  locations: (id: string, params: { from?: string; to?: string; maxPoints?: number; includeSuspicious?: boolean; page?: number; pageSize?: number }) =>
    http.get<PagedResult<TrackLocationDto>>(`${tr(id)}/locations`, { params }).then((r) => r.data),
  health: (id: string) => http.get<TrackingHealthDto>(`${tr(id)}/health`).then((r) => r.data),
  analytics: (id: string) => http.get<JourneyAnalyticsDto>(`${tr(id)}/analytics`).then((r) => r.data),
  shipmentExceptions: (id: string) => http.get<TrackExceptionSummaryDto[]>(`${tr(id)}/exceptions`).then((r) => r.data),
  overrideEta: (id: string, eta: string, reason: string) => http.post<TrackedDetailDto>(`${tr(id)}/eta/override`, { eta, reason }).then((r) => r.data),
  clearEtaOverride: (id: string) => http.delete<TrackedDetailDto>(`${tr(id)}/eta/override`).then((r) => r.data),
  setDelayReason: (id: string, reason: DelayReason, note: string | null) => http.post<TrackedDetailDto>(`${tr(id)}/delay-reason`, { reason, note }).then((r) => r.data),
  deviationReason: (id: string, reason: DelayReason, note: string | null) => http.post(`${v1}/tracking/deviations/${id}/reason`, { reason, note }),
  recalculateEta: (tripReference: string) => http.post(`${v1}/tracking/eta/recalculate`, { shipmentId: null, tripReference }),

  vehicles: (params: { search?: string; health?: string; page?: number; pageSize?: number }) => http.get<PagedResult<VehicleTrackingDto>>(`${v1}/control-tower/vehicles`, { params }).then((r) => r.data),
  vehicleHistory: (vehicle: string, params: { from?: string; to?: string; maxPoints?: number }) =>
    http.get<TrackLocationDto[]>(`${v1}/tracking/vehicles/${encodeURIComponent(vehicle)}/history`, { params }).then((r) => r.data),

  alerts: (params: { status?: TrackAlertStatus; severity?: TrackSeverity; type?: TrackAlertType; shipmentId?: string; page?: number; pageSize?: number }) =>
    http.get<PagedResult<TrackAlertDto>>(`${v1}/tracking/alerts`, { params }).then((r) => r.data),
  acknowledgeAlert: (id: string) => http.post<TrackAlertDto>(`${v1}/tracking/alerts/${id}/acknowledge`).then((r) => r.data),
  resolveAlert: (id: string, note: string | null) => http.post<TrackAlertDto>(`${v1}/tracking/alerts/${id}/resolve`, { note }).then((r) => r.data),

  exceptions: (params: { status?: TrackExceptionStatus; severity?: TrackSeverity; type?: TrackAlertType; transporterId?: string; openOnly?: boolean; overdue?: boolean; page?: number; pageSize?: number }) =>
    http.get<PagedResult<TrackExceptionSummaryDto>>(`${v1}/tracking/exceptions`, { params }).then((r) => r.data),
  exception: (id: string) => http.get<TrackExceptionDto>(ex(id)).then((r) => r.data),
  acknowledgeException: (id: string) => http.post<TrackExceptionDto>(`${ex(id)}/acknowledge`).then((r) => r.data),
  assignException: (id: string, body: { ownerUserId: string | null; department: string | null; dueAt: string | null; severity: TrackSeverity | null }) => http.post<TrackExceptionDto>(`${ex(id)}/assign`, body).then((r) => r.data),
  escalateException: (id: string, reason: string) => http.post<TrackExceptionDto>(`${ex(id)}/escalate`, { reason, level: null }).then((r) => r.data),
  resolveException: (id: string, body: { rootCause: string; delayReason: DelayReason | null; actionTaken: string | null }) => http.post<TrackExceptionDto>(`${ex(id)}/resolve`, body).then((r) => r.data),
  closeException: (id: string) => http.post<TrackExceptionDto>(`${ex(id)}/close`).then((r) => r.data),
  noteException: (id: string, text: string) => http.post<TrackExceptionDto>(`${ex(id)}/notes`, { text }).then((r) => r.data),

  geofences: () => http.get<GeofenceDto[]>(`${v1}/tracking/geofences`).then((r) => r.data),
  createGeofence: (body: SaveGeofenceRequest) => http.post<GeofenceDto>(`${v1}/tracking/geofences`, body).then((r) => r.data),
  updateGeofence: (id: string, body: SaveGeofenceRequest) => http.put<GeofenceDto>(`${v1}/tracking/geofences/${id}`, body).then((r) => r.data),
  deleteGeofence: (id: string) => http.delete(`${v1}/tracking/geofences/${id}`),

  links: (shipmentId: string) => http.get<CustomerLinkDto[]>(`${tr(shipmentId)}/links`).then((r) => r.data),
  createLink: (body: { shipmentId: string; customerReference: string | null; customerName: string | null; validDays: number | null }) => http.post<CreatedLinkDto>(`${v1}/tracking/links`, body).then((r) => r.data),
  revokeLink: (id: string) => http.post<CustomerLinkDto>(`${v1}/tracking/links/${id}/revoke`).then((r) => r.data),

  settings: () => http.get<TrackingSettingDto[]>(`${v1}/tracking/settings`).then((r) => r.data),
  saveSetting: (key: string, value: unknown) => http.put<TrackingSettingDto>(`${v1}/tracking/settings/${key}`, value).then((r) => r.data),
  downloadReport: (report: string, format: 'csv' | 'xlsx', params: { from?: string; to?: string; transporterId?: string; vehicle?: string; customer?: string; groupBy?: string }) =>
    downloadFile(`${v1}/tracking/reports/${report}`, `${report}.${format}`, { ...params, format }),

  // the driver's phone
  trips: () => http.get<MobileTripDto[]>(`${v1}/mobile/tracking/trips`).then((r) => r.data),
  start: (tripReference: string, deviceId: string, clientKey: string) =>
    http.post<TrackingSessionDto>(`${v1}/mobile/tracking/start`, { tripReference, deviceId, clientKey }).then((r) => r.data),
  stop: (tripReference: string, deviceId: string, completed: boolean, clientKey: string) =>
    http.post<TrackingSessionDto>(`${v1}/mobile/tracking/stop`, { tripReference, deviceId, completed, clientKey }).then((r) => r.data),
  sendBatch: (body: { tripReference: string; deviceId: string; locations: QueuedFix[]; batteryPercentage?: number | null; networkType?: string | null; locationPermission?: string | null; sentAtUtc: string }) =>
    http.post<TrackBatchResult>(`${v1}/mobile/tracking/location/batch`, body).then((r) => r.data),

  /** The customer's page: anonymous, so it goes around the signed-in client. */
  customerView: (token: string) => fetch(`${import.meta.env.VITE_API_URL ?? ''}${v1}/public/tracking/${encodeURIComponent(token)}`, { headers: { Accept: 'application/json' } }).then(async (r) => {
    if (!r.ok) throw new Error(r.status === 404 ? 'not-found' : 'unavailable')
    return (await r.json()) as CustomerTrackingDto
  }),
}
