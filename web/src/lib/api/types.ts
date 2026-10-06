// Contracts mirrored from the TMS API (camelCase JSON). Replace with generated types once the API surface grows.

export type UserType = 'Internal' | 'Transporter' | 'Driver'

export interface UserProfile {
  id: string
  email: string
  fullName: string
  type: UserType
  transporterId: string | null
  tenantCode: string
  tenantName: string
  roles: string[]
  permissions: string[]
  mustChangePassword: boolean
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: UserProfile
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface RoleSummary {
  id: string
  name: string
}

export interface UserDto {
  id: string
  email: string
  fullName: string
  type: UserType
  transporterId: string | null
  isActive: boolean
  lastLoginAt: string | null
  createdAt: string
  version: number
  roles: RoleSummary[]
}

export interface CreateUserRequest {
  email: string
  fullName: string
  password: string
  type: UserType
  roleIds: string[]
  transporterId?: string | null
  requirePasswordChange?: boolean
}

export interface UpdateUserRequest {
  fullName: string
  isActive: boolean
  roleIds: string[]
  version: number
}

export interface ListUsersParams {
  search?: string
  isActive?: boolean
  page?: number
  pageSize?: number
}

export type RoleAudience = 'Internal' | 'External'

export interface RoleDto {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  audience: RoleAudience
  permissions: string[]
  userCount: number
  version: number
}

export interface SaveRoleRequest {
  name: string
  description: string | null
  permissions: string[]
  version: number | null
  audience?: RoleAudience
}

export interface PermissionDefinition {
  code: string
  module: string
  description: string
  externalAllowed: boolean
}

export type AuditAction = 'Created' | 'Updated' | 'Deleted'

export interface AuditLogDto {
  id: number
  occurredAt: string
  userId: string | null
  userName: string | null
  entityType: string
  entityId: string
  action: AuditAction
  changes: Record<string, { old?: unknown; new?: unknown }> | null
  traceId: string | null
  ipAddress: string | null
}

export interface ListAuditLogsParams {
  entityType?: string
  entityId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

// ---- Approvals ----

export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'
export type StepStatus = 'Pending' | 'Approved' | 'Rejected'

export interface PolicyStepDto {
  name: string
  requiredPermission: string
  minAmount: number | null
}

export interface PolicyDto {
  id: string | null
  documentType: string
  documentTypeName: string
  isConfigured: boolean
  isActive: boolean
  steps: PolicyStepDto[]
  version: number | null
}

export interface SavePolicyRequest {
  isActive: boolean
  steps: PolicyStepDto[]
  version: number | null
}

export interface RequestStepDto {
  order: number
  name: string
  requiredPermission: string
  status: StepStatus
  decidedBy: string | null
  decidedByName: string | null
  onBehalfOf: string | null
  onBehalfOfName: string | null
  decidedAt: string | null
  comment: string | null
}

export interface RequestDto {
  id: string
  documentType: string
  documentTypeName: string
  documentId: string
  title: string
  amount: number | null
  requesterId: string
  requesterName: string
  status: ApprovalStatus
  createdAt: string
  completedAt: string | null
  currentStepIndex: number | null
  canDecide: boolean
  canCancel: boolean
  steps: RequestStepDto[]
  version: number
}

export interface RequestSummaryDto {
  id: string
  documentType: string
  documentTypeName: string
  title: string
  amount: number | null
  requesterName: string
  status: ApprovalStatus
  currentStepName: string | null
  createdAt: string
  canDecide: boolean
}

export type RequestScope = 'inbox' | 'mine' | 'all'

export interface ListRequestsParams {
  scope: RequestScope
  status?: ApprovalStatus
  page?: number
  pageSize?: number
}

export interface DelegationDto {
  id: string
  delegatorId: string
  delegatorName: string
  delegateId: string
  delegateName: string
  validFrom: string
  validTo: string
  reason: string | null
  isActive: boolean
  version: number
}

export interface DelegationsDto {
  given: DelegationDto[]
  received: DelegationDto[]
}

export interface CreateDelegationRequest {
  delegateId: string
  validFrom: string
  validTo: string
  reason: string | null
}

export interface UserLookupDto {
  id: string
  fullName: string
  email: string
}

// ---- Transporters ----

export type TransporterStatus = 'Draft' | 'PendingApproval' | 'Active' | 'Rejected' | 'Suspended'
export type ServiceMode = 'Ftl' | 'Ptl' | 'Dedicated'
export type OwnerKind = 'Transporter' | 'Vehicle' | 'Driver'
export type VehicleOwnership = 'Owned' | 'Attached' | 'Market'
export type ComplianceStatus = 'Compliant' | 'ExpiringSoon' | 'NonCompliant'
export type ExpiryStatus = 'NoExpiry' | 'Valid' | 'ExpiringSoon' | 'Expired'
export type DocumentKind =
  | 'PanCard'
  | 'GstCertificate'
  | 'CancelledCheque'
  | 'MsmeCertificate'
  | 'TransportLicense'
  | 'RegistrationCertificate'
  | 'Insurance'
  | 'Fitness'
  | 'Permit'
  | 'Puc'
  | 'DrivingLicense'

export interface TransporterSummaryDto {
  id: string
  code: string
  legalName: string
  tradeName: string | null
  city: string
  state: string
  status: TransporterStatus
  serviceModes: ServiceMode[]
  phone: string
}

export interface AddressDto {
  line1: string
  line2: string | null
  city: string
  state: string
  pincode: string
}

export interface BankDto {
  accountHolder: string
  accountNumberMasked: string
  ifsc: string
  bankName: string
}

export interface TransporterDto {
  id: string
  code: string
  status: TransporterStatus
  legalName: string
  tradeName: string | null
  pan: string
  gstin: string | null
  contactPerson: string
  phone: string
  email: string
  address: AddressDto
  serviceModes: ServiceMode[]
  bank: BankDto | null
  approvalRequestId: string | null
  suspensionReason: string | null
  activatedAt: string | null
  createdAt: string
  version: number
  missingForSubmission: string[]
  typeCode?: string | null
}

export interface SaveTransporterRequest {
  legalName: string
  tradeName: string | null
  pan: string
  gstin: string | null
  contactPerson: string
  phone: string
  email: string
  addressLine1: string
  addressLine2: string | null
  city: string
  state: string
  pincode: string
  serviceModes: ServiceMode[]
  version: number | null
  typeCode?: string | null
}

export interface MasterEntryDto {
  code: string
  name: string
  isActive: boolean
  isBuiltIn: boolean
}

export interface SaveMasterItemRequest {
  code: string
  name: string
  isActive: boolean
}

export interface DocumentRuleDto {
  kind: DocumentKind
  label: string
  owner: 'Transporter' | 'Vehicle' | 'Driver'
  isMandatory: boolean
  expiryRequired: boolean
  renewalReminderDays: number
  blockWhenExpired: boolean
  isActive: boolean
  isCustomised: boolean
}

export interface SaveDocumentRuleRequest {
  isMandatory: boolean
  expiryRequired: boolean
  renewalReminderDays: number
  blockWhenExpired: boolean
  isActive: boolean
}

export interface SaveBankRequest {
  accountHolder: string
  accountNumber: string
  ifsc: string
  bankName: string
  version: number
}

export interface ListTransportersParams {
  search?: string
  status?: TransporterStatus
  page?: number
  pageSize?: number
}

export interface TransporterLookupDto {
  id: string
  code: string
  legalName: string
}

export interface VehicleTypeDto {
  id: string
  code: string
  name: string
  payloadKg: number
  volumeCbm: number | null
  isActive: boolean
  version: number
  lengthM?: number | null
  widthM?: number | null
  heightM?: number | null
  allowsHazardous?: boolean
  supportsTemperatureControl?: boolean
}

export interface SaveVehicleTypeRequest {
  code: string
  name: string
  payloadKg: number
  volumeCbm: number | null
  isActive: boolean
  version: number | null
  lengthM: number | null
  widthM: number | null
  heightM: number | null
  allowsHazardous: boolean
  supportsTemperatureControl: boolean
}

export interface ComplianceDto {
  status: ComplianceStatus
  issues: string[]
}

export interface VehicleDto {
  id: string
  transporterId: string
  registrationNumber: string
  vehicleTypeId: string
  vehicleTypeName: string
  payloadKg: number
  ownership: VehicleOwnership
  make: string | null
  yearOfManufacture: number | null
  isActive: boolean
  compliance: ComplianceDto
  version: number
  availability?: FleetAvailability
  availableFrom?: string | null
  availableTo?: string | null
  availabilityNote?: string | null
}

export type FleetAvailability = 'Available' | 'InMaintenance' | 'OffRoad'

export interface SaveVehicleRequest {
  registrationNumber: string
  vehicleTypeId: string
  ownership: VehicleOwnership
  make: string | null
  yearOfManufacture: number | null
  isActive: boolean
  version: number | null
  availability?: FleetAvailability
  availableFrom?: string | null
  availableTo?: string | null
  availabilityNote?: string | null
}

export interface DriverDto {
  id: string
  transporterId: string
  fullName: string
  phone: string
  licenseNumber: string | null
  isActive: boolean
  compliance: ComplianceDto
  version: number
}

export interface SaveDriverRequest {
  fullName: string
  phone: string
  licenseNumber: string | null
  isActive: boolean
  version: number | null
}

export interface DocumentDto {
  id: string
  transporterId: string
  ownerKind: OwnerKind
  ownerId: string
  kind: DocumentKind
  kindLabel: string
  number: string | null
  issuedOn: string | null
  expiresOn: string | null
  fileName: string
  contentType: string
  sizeBytes: number
  expiryStatus: ExpiryStatus
  isCurrent: boolean
  uploadedAt: string
}

export interface ComplianceItemDto {
  document: DocumentDto
  ownerLabel: string
  transporterName: string
}

export interface UploadDocumentInput {
  ownerKind: OwnerKind
  ownerId: string
  kind: DocumentKind
  number?: string
  issuedOn?: string
  expiresOn?: string
  file: File
}

// ---- Contracts ----

export type ContractType = 'Ftl' | 'Ptl' | 'Dedicated'
export type ContractStatus = 'Draft' | 'PendingApproval' | 'Active' | 'Rejected' | 'Terminated' | 'Superseded' | 'Expired'
export type PlaceKind = 'Any' | 'State' | 'Zone' | 'City'
export type SlabMode = 'Whole' | 'Incremental'
export type FuelStepUnit = 'Percent' | 'Rupees'
export type FuelDirection = 'Both' | 'EscalationOnly'
export type ContractDocumentKind = 'SignedContract' | 'Annexure' | 'Amendment' | 'Correspondence' | 'Other'

export interface PlaceDto {
  kind: PlaceKind
  state: string | null
  city: string | null
  zoneCode: string | null
}

export interface WeightSlab {
  fromKg: number
  toKg: number | null
  ratePerKg: number
}

/** Discriminated by `kind` (must stay the first property when sent). */
export type Pricing =
  | { kind: 'flatTrip'; amountPerTrip: number }
  | { kind: 'perKm'; ratePerKm: number; minKm: number; minCharge: number }
  | { kind: 'weightSlabs'; mode: SlabMode; slabs: WeightSlab[]; minCharge: number; minChargeableKg: number }
  | { kind: 'dedicated'; monthlyRental: number; includedKmPerMonth: number; extraKmRate: number; includedHoursPerMonth: number; extraHourRate: number }

export interface RateInputDto {
  origin: PlaceDto
  destination: PlaceDto
  bothWays: boolean
  vehicleTypeId: string | null
  minDistanceKm: number | null
  maxDistanceKm: number | null
  pricing: Pricing
}

export interface RateCardDto extends RateInputDto {
  id: string
  lane: string
  vehicleTypeName: string | null
}

export interface ContractTerms {
  volumetricKgPerCbm: number
  detentionFreeHours: number
  detentionRatePerHour: number
  loadingCharge: number
  unloadingCharge: number
  multiDropChargePerPoint: number
  minChargePerConsignment: number
  notes: string | null
}

export interface FuelClause {
  region: string
  basePricePerLitre: number
  unit: FuelStepUnit
  stepSize: number
  impactPercentPerStep: number
  deadBand: number
  capPercent: number | null
  direction: FuelDirection
}

export interface ContractSummaryDto {
  id: string
  number: string
  revision: number
  reference: string
  transporterId: string
  transporterName: string
  type: ContractType
  title: string
  status: ContractStatus
  effectiveFrom: string
  effectiveTo: string
  daysUntilExpiry: number | null
  rateCount: number
  estimatedAnnualSpend: number | null
}

export interface ContractDto {
  summary: ContractSummaryDto
  paymentTermsDays: number
  terms: ContractTerms
  fuel: FuelClause | null
  ownerUserId: string | null
  ownerName: string | null
  approvalRequestId: string | null
  revisionOfId: string | null
  terminationReason: string | null
  ratesRevision: number
  activatedAt: string | null
  createdAt: string
  version: number
  missingForSubmission: string[]
}

export interface SaveContractRequest {
  transporterId: string
  type: ContractType
  title: string
  effectiveFrom: string
  effectiveTo: string
  paymentTermsDays: number
  estimatedAnnualSpend: number | null
  ownerUserId: string | null
  terms: ContractTerms
  fuel: FuelClause | null
  version: number | null
}

export interface ListContractsParams {
  search?: string
  status?: ContractStatus
  type?: ContractType
  transporterId?: string
  page?: number
  pageSize?: number
}

export interface ZoneMember {
  state: string
  city: string | null
}

export interface ZoneDto {
  id: string
  code: string
  name: string
  members: ZoneMember[]
  version: number
}

export interface DieselPriceDto {
  id: string
  region: string
  effectiveFrom: string
  pricePerLitre: number
}

export interface QuoteRequest {
  date?: string
  origin: { state: string; city?: string }
  destination: { state: string; city?: string }
  vehicleTypeId?: string
  type?: ContractType
  weightKg?: number
  volumeCbm?: number
  distanceKm?: number
  drops?: number
  contractId?: string
  preview?: boolean
}

export interface QuoteLine {
  code: string
  description: string
  amount: number
}

export interface QuoteDto {
  contractId: string
  contractReference: string
  transporterId: string
  transporterName: string
  type: ContractType
  lane: string
  chargeableWeightKg: number | null
  lines: QuoteLine[]
  notes: string[]
  total: number
}

export interface QuoteResultDto {
  date: string
  quotes: QuoteDto[]
  message: string | null
}

export interface ContractDocumentDto {
  id: string
  contractId: string
  kind: ContractDocumentKind
  title: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedAt: string
}

// ---- Shipments (orders, planning, tendering, dispatch)

export type FreightMode = 'Ftl' | 'Ptl'
export type OrderDirection = 'Forward' | 'Reverse'
export type OrderPriority = 'Low' | 'Normal' | 'High' | 'Urgent'
export type HandlingType = 'Standard' | 'Fragile' | 'TemperatureControlled'
export type ReturnType = 'CustomerReturn' | 'DamagedMaterial' | 'RejectedMaterial' | 'EmptyPackaging' | 'SupplierReturn' | 'ReplacementPickup'
export type OrderStatus = 'Open' | 'Planned' | 'Dispatched' | 'Delivered' | 'Cancelled'
export type ShipmentStatus = 'Draft' | 'Tendered' | 'Accepted' | 'Dispatched' | 'Delivered' | 'Cancelled' | 'Bidding'

export type TenderMode = 'Sequential' | 'Broadcast'
export type TenderStatus = 'Open' | 'Awarded' | 'Cancelled' | 'Exhausted'
export type InviteeStatus = 'Waiting' | 'Sent' | 'Bid' | 'Accepted' | 'Rejected' | 'Expired' | 'Superseded' | 'Cancelled'
export type CounterStatus = 'None' | 'Pending' | 'Agreed' | 'Declined'

export interface StartTenderRequest {
  mode: TenderMode
  contractIds: string[]
  responseMinutes: number | null
  notes: string | null
}

/** `contractReference` and `quotedTotal` are null for vendors, who see only their own invitation. */
export interface TenderInviteeDto {
  id: string
  transporterId: string
  transporterName: string
  sequence: number
  status: InviteeStatus
  sentAt: string | null
  deadline: string | null
  respondedAt: string | null
  reason: string | null
  contractReference: string | null
  quotedTotal: number | null
  counterRate: number | null
  counterComment: string | null
  counterStatus: CounterStatus
  agreedRate: number | null
  bidVehicleId: string | null
  bidVehicleRegistration: string | null
  bidDriverId: string | null
  bidDriverName: string | null
}

export interface TenderEventDto {
  at: string
  type: string
  inviteeId: string | null
  transporterName: string | null
  comments: string | null
}

export interface TenderDto {
  id: string
  number: string
  shipmentId: string
  shipmentNumber: string
  mode: TenderMode
  status: TenderStatus
  responseMinutes: number
  notes: string | null
  awardedTransporterId: string | null
  closedAt: string | null
  closeReason: string | null
  createdAt: string
  invitees: TenderInviteeDto[]
  events: TenderEventDto[]
}

export interface PartyDto {
  name: string
  line1: string
  city: string
  state: string
  pincode: string
  contactName: string | null
  contactPhone: string | null
}

export interface SaveOrderRequest {
  direction: OrderDirection
  reference: string | null
  pickup: PartyDto
  drop: PartyDto
  weightKg: number
  volumeCbm: number | null
  packages: number | null
  description: string
  readyDate: string
  deliverByDate: string | null
  notes: string | null
  /** Optional master locations; the server then takes the address from them. */
  pickupLocationId?: string | null
  dropLocationId?: string | null
  /** Daily window the consignee accepts deliveries, as HH:mm:ss (India time). Both or neither. */
  deliveryWindowFrom?: string | null
  deliveryWindowTo?: string | null
  priority?: OrderPriority
  /** Free-text category (e.g. FOOD); compatibility rules refer to it. */
  productCategory?: string | null
  handling?: HandlingType
  isHazardous?: boolean
  isStackable?: boolean
  /** Longest single item in metres; a vehicle body must be at least this long. */
  longestItemM?: number | null
  /** Return orders only. */
  returnType?: ReturnType | null
  returnReason?: string | null
  /** Window in which the goods can be collected, as HH:mm:ss (return orders). Both or neither. */
  pickupWindowFrom?: string | null
  pickupWindowTo?: string | null
}

export interface CompatibilityRuleDto {
  id: string
  categoryA: string
  categoryB: string
  reason: string | null
}

export interface SaveCompatibilityRuleRequest {
  categoryA: string
  categoryB: string
  reason: string | null
}

export interface OrderDto extends SaveOrderRequest {
  id: string
  number: string
  status: OrderStatus
  shipmentId: string | null
  shipmentNumber: string | null
  cancelReason: string | null
  version: number
}

export interface ListOrdersParams {
  status?: OrderStatus
  direction?: OrderDirection
  search?: string
  pickupState?: string
  page?: number
  pageSize?: number
}

export interface QuoteLineDto {
  code: string
  description: string
  amount: number
}

export interface ShipmentOrderDto {
  orderId: string
  orderNumber: string
  dropSequence: number
  lrNumber: string | null
  pickup: PartyDto
  drop: PartyDto
  weightKg: number
  volumeCbm: number | null
  description: string
  isReturn?: boolean
  deliveredAt?: string | null
  receiverName?: string | null
  packagesShipped?: number | null
  deliveredPackages?: number | null
  damagedPackages?: number | null
  shortagePackages?: number | null
  deliveryRemarks?: string | null
  podStatus?: PodStatus
  podRejectionReason?: string | null
  podDocuments?: number
}

export interface ShipmentSummaryDto {
  id: string
  number: string
  status: ShipmentStatus
  mode: FreightMode
  lane: string
  plannedPickupDate: string
  orderCount: number
  totalWeightKg: number
  transporterId: string | null
  transporterName: string | null
  vehicleRegistration: string | null
  utilization: number | null
  /** Null for vendor-portal users. */
  freightEstimate: number | null
}

export interface ShipmentDto {
  summary: ShipmentSummaryDto
  vehicleTypeId: string | null
  vehicleTypeName: string | null
  distanceKm: number | null
  totalVolumeCbm: number | null
  contractId: string | null
  contractReference: string | null
  /** The next three are null for vendor-portal users. */
  estimateLines: QuoteLineDto[] | null
  overrideReason: string | null
  tenderedAt: string | null
  rejectionCount: number
  lastRejectionReason: string | null
  vehicleId: string | null
  driverId: string | null
  driverName: string | null
  driverPhone: string | null
  acceptedAt: string | null
  dispatchedAt: string | null
  deliveredAt: string | null
  cancelReason: string | null
  orders: ShipmentOrderDto[]
  version: number
}

export interface ListShipmentsParams {
  status?: ShipmentStatus
  transporterId?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface CreateShipmentRequest {
  orderIds: string[]
  mode: FreightMode
  vehicleTypeId: string | null
  plannedPickupDate: string
  distanceKm: number | null
}

export interface UpdateShipmentPlanRequest {
  mode: FreightMode
  vehicleTypeId: string | null
  plannedPickupDate: string
  distanceKm: number | null
}

export interface ShipmentQuoteDto {
  contractId: string
  contractReference: string
  transporterId: string
  transporterName: string
  mode: FreightMode
  lane: string
  total: number
  isCheapest: boolean
  lines: QuoteLineDto[]
  notes: string[]
}

export interface ShipmentQuotesDto {
  quotes: ShipmentQuoteDto[]
  message: string | null
}

export interface FleetOptionDto {
  id: string
  label: string
  isOk: boolean
  warn: boolean
  issues: string[]
}

export interface FleetOptionsDto {
  vehicles: FleetOptionDto[]
  drivers: FleetOptionDto[]
}

export interface AdviceRequest {
  weightKg: number
  volumeCbm: number | null
  originState: string
  originCity: string | null
  destinationState: string
  destinationCity: string | null
  date: string | null
  distanceKm: number | null
}

export interface VehicleOptionDto {
  vehicleTypeId: string
  name: string
  payloadKg: number
  weightUtilization: number
}

export interface ModeOptionDto {
  mode: FreightMode
  feasible: boolean
  total: number | null
  transporter: string | null
  detail: string
}

export interface AdviceDto {
  fitting: VehicleOptionDto[]
  recommended: VehicleOptionDto | null
  vehiclesNeeded: number | null
  sizingWarning: string | null
  modes: ModeOptionDto[]
  recommendedMode: FreightMode | null
  modeReason: string
}

export interface SuggestedLoadDto {
  orderIds: string[]
  orderNumbers: string[]
  pickupCity: string
  pickupState: string
  drops: string[]
  totalWeightKg: number
  totalVolumeCbm: number | null
  vehicle: VehicleOptionDto | null
  utilization: number
  suggested: FreightMode
  earliestReady: string
  earliestDeadline: string | null
  backhaulOrderIds: string[]
  warning: string | null
}

export interface UtilizationRowDto {
  shipmentId: string
  number: string
  plannedPickupDate: string
  transporterName: string
  vehicleRegistration: string | null
  loadKg: number
  payloadKg: number | null
  utilization: number | null
}

export interface UtilizationDto {
  shipments: number
  averageUtilization: number | null
  underUtilised: number
  rows: UtilizationRowDto[]
}

// ---- Planning runs (decision engine)

export type PlanObjective = 'MinimizeTotalCost' | 'MinimizeVehicles' | 'MaximizeUtilisation' | 'MinimizeDistance' | 'BalanceCostAndUtilisation'
export type SolverStatus = 'Feasible' | 'Optimized' | 'TimeLimitReached' | 'Infeasible' | 'Failed'
export interface ModeChoice {
  mode: FreightMode
  override: boolean
  reason?: string | null
}

export interface CommittedTrip {
  tripNumber: number
  shipmentId: string
  shipmentNumber: string
  orders: number
  plannedCost: number | null
  isCollectionRun: boolean
}

export interface CommitMilkRunResult {
  code: string
  date: string
  shipments: CommittedTrip[]
}

export type LockKind = 'Vehicle' | 'Sequence' | 'Assignment' | 'Order'

export type PlanStatus = 'Running' | 'Completed' | 'PartiallyPlanned' | 'Infeasible' | 'Approved' | 'Committed' | 'Cancelled'

export interface PlanLogEntry {
  at: string
  message: string
}

export interface PlanOptions {
  objective: PlanObjective
  allowFtl: boolean
  allowPtl: boolean
  allowConsolidation: boolean
  maxStops: number
  timeBudgetSeconds: number
  enforceDeadlines: boolean
  ptlExtraTransitHours: number
  stopServiceMinutes: number
  departureHour: number
  allowBackhaul: boolean
  backhaulChargePercent: number
  maxBackhaulExtraKm: number
  returnsAfterDeliveries: boolean
}

export interface PlanQuoteLine {
  code: string
  description: string
  amount: number
}

export interface AssignedTransporter {
  id: string
  code: string
  name: string
  contactPerson: string | null
  phone: string | null
  email: string | null
  city: string | null
}

export interface AssignedVehicle {
  id: string
  registration: string
  typeName: string | null
  payloadKg: number | null
  compliance: string
  issues: string[]
}

export interface AssignedDriver {
  id: string
  name: string
  phone: string | null
  licenseNumber: string | null
  compliance: string
  issues: string[]
}

export interface PlanAlternative {
  mode: FreightMode
  vehicleTypeId: string | null
  vehicleTypeName: string | null
  contractId: string | null
  contractReference: string | null
  transporterId: string | null
  transporterName: string | null
  total: number | null
  lines: PlanQuoteLine[]
  chosen: boolean
  verdict: string
  transitHours: number | null
  meetsDeadline: boolean | null
  transporter: AssignedTransporter | null
  vehicle: AssignedVehicle | null
  driver: AssignedDriver | null
}

export interface PlannedStop {
  sequence: number
  kind: 'Pickup' | 'Drop' | 'ReturnPickup' | 'Return'
  label: string
  latitude: number | null
  longitude: number | null
  plannedArrival: string | null
  plannedDeparture: string | null
  waitMinutes: number | null
}

export type RouteSource = 'Estimate' | 'Osrm'

export interface PlannedOrder {
  orderId: string
  number: string
  sequence: number
  dropCity: string
  dropState: string
  weightKg: number
  volumeCbm: number | null
  kind: 'Delivery' | 'ReturnPickup'
  priority?: OrderPriority
  isLocked?: boolean
}

export interface PlannedVehicle {
  key: string
  pickupCity: string
  pickupState: string
  mode: FreightMode
  vehicleTypeId: string | null
  vehicleTypeName: string | null
  payloadKg: number | null
  volumeCapacityCbm: number | null
  contractId: string | null
  contractReference: string | null
  transporterId: string | null
  transporterName: string | null
  estimatedCost: number
  costLines: PlanQuoteLine[]
  weightKg: number
  volumeCbm: number | null
  weightUtilisation: number | null
  volumeUtilisation: number | null
  orders: PlannedOrder[]
  alternatives: PlanAlternative[]
  reason: string
  isLocked: boolean
  consolidationSaving: number | null
  shipmentId: string | null
  shipmentNumber: string | null
  distanceKm: number | null
  durationMinutes: number | null
  routeSource: RouteSource | null
  transitHours: number | null
  costPerTonneKm: number | null
  stops: PlannedStop[] | null
  plannedDeparture: string | null
  sequenceMethod: 'Exact' | 'Heuristic' | null
  additionalKm: number | null
  additionalMinutes: number | null
  separateCost: number | null
  savingPercent: number | null
  backhaulSaving: number | null
  routeNote: string | null
  transporter: AssignedTransporter | null
  assignedVehicle: AssignedVehicle | null
  assignedDriver: AssignedDriver | null
  lengthUtilisation: number | null
  loadedKm: number | null
  emptyKm: number | null
  sequenceLocked: boolean
  assignmentLocked: boolean
}

export interface UnplannedOrder {
  orderId: string
  number: string
  code: string
  reason: string
  suggestions: string[]
}

export interface PlanSummary {
  ordersPlanned: number
  ordersUnplanned: number
  vehiclesUsed: number
  totalCost: number
  averageWeightUtilisation: number | null
  averageVolumeUtilisation: number | null
  consolidationSaving: number
  ftlCount: number
  ptlCount: number
  totalDistanceKm: number | null
  costPerTonneKm: number | null
  backhaulSaving: number
  returnPickups: number
  totalLoadedKm?: number | null
  totalEmptyKm?: number | null
  emptyKmPercent?: number | null
  costPerTonne?: number | null
  costPerShipment?: number | null
}

export interface PlanSnapshot {
  solverStatus: SolverStatus
  solverMessage: string | null
  vehicles: PlannedVehicle[]
  unplanned: UnplannedOrder[]
  summary: PlanSummary
}

export interface RunDto {
  id: string
  runGroupId: string
  number: string
  planVersion: number
  isLatest: boolean
  planningDate: string
  status: PlanStatus
  options: PlanOptions
  orderIds: string[]
  reason: string | null
  plan: PlanSnapshot
  createdAt: string
  approvedAt: string | null
  committedAt: string | null
  cancelReason: string | null
  version: number
  startedAt?: string | null
  completedAt?: string | null
  log?: PlanLogEntry[] | null
}

export interface RunSummaryDto {
  id: string
  number: string
  planVersion: number
  planningDate: string
  status: PlanStatus
  solverStatus: SolverStatus
  summary: PlanSummary
  createdAt: string
}

export interface RunVersionDto {
  id: string
  planVersion: number
  status: PlanStatus
  reason: string | null
  createdAt: string
  summary: PlanSummary
}

export interface VehicleTypeEvaluation {
  vehicleTypeId: string
  name: string
  payloadKg: number
  volumeCbm: number | null
  accepted: boolean
  reason: string | null
  weightUtilisation: number
  volumeUtilisation: number | null
}

export interface ComparisonDto {
  alternatives: PlanAlternative[]
  recommended: PlanAlternative | null
  reason: string
  sizing: VehicleTypeEvaluation[]
  ftlTotal: number | null
  ptlTotal: number | null
  saving: number | null
  recommendedMode: FreightMode | null
}

// ---- Locations master

export type LocationType = 'Depot' | 'Plant' | 'Warehouse' | 'Customer' | 'Supplier' | 'Other'

export interface SaveLocationRequest {
  code: string
  name: string
  type: LocationType
  line1: string
  city: string
  state: string
  pincode: string
  latitude: number
  longitude: number
  isActive: boolean
  version: number | null
}

export interface LocationDto extends Omit<SaveLocationRequest, 'version'> {
  id: string
  version: number
}

export interface ListLocationsParams {
  search?: string
  type?: LocationType
  active?: boolean
  page?: number
  pageSize?: number
}

export interface DistanceDto {
  distanceKm: number
  durationMinutes: number
  source: RouteSource
}

export type PlanEditKind = 'MoveOrder' | 'RemoveOrder' | 'AddOrder' | 'ReorderStops' | 'ChangeVehicleType'

export interface EditPlanRequest {
  kind: PlanEditKind
  orderId?: string
  vehicleKey?: string
  toVehicleKey?: string | null
  orderIds?: string[]
  vehicleTypeId?: string
  comment?: string
}

// ---- Milk runs

export type MilkRunStopType = 'Pickup' | 'Delivery'
export type DayName = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday'

export interface MilkRunStopRequest {
  locationId: string
  type: MilkRunStopType
  serviceMinutes: number
  windowFrom?: string | null
  windowTo?: string | null
}

export interface SaveMilkRunRequest {
  code: string
  name: string
  depotLocationId: string
  vehicleTypeId: string | null
  maxStops: number
  maxDurationMinutes: number
  departureTime: string
  days: DayName[]
  stops: MilkRunStopRequest[]
  isActive: boolean
  version: number | null
}

export interface MilkRunStopDto {
  sequence: number
  locationId: string
  locationName: string
  city: string
  state: string
  type: MilkRunStopType
  serviceMinutes: number
  windowFrom: string | null
  windowTo: string | null
}

export interface MilkRunDto {
  id: string
  code: string
  name: string
  depotLocationId: string
  depotName: string
  vehicleTypeId: string | null
  maxStops: number
  maxDurationMinutes: number
  departureTime: string
  days: DayName[]
  isActive: boolean
  stops: MilkRunStopDto[]
  version: number
}

export interface MilkRunOrderLine {
  orderId: string
  number: string
  weightKg: number
  volumeCbm: number | null
}

export interface MilkRunStopPlan {
  sequence: number
  kind: 'Pickup' | 'Delivery' | 'Depot'
  label: string
  orders: MilkRunOrderLine[]
  weightKg: number
  volumeCbm: number | null
  arrival: string | null
  departure: string | null
  waitMinutes: number | null
  onboardKg: number
}

export interface MilkRunTrip {
  number: number
  vehicleTypeId: string | null
  vehicleTypeName: string | null
  payloadKg: number | null
  volumeCapacityCbm: number | null
  contractReference: string | null
  transporterName: string | null
  cost: number | null
  distanceKm: number
  drivingMinutes: number
  totalMinutes: number
  departure: string
  returnAt: string
  peakWeightKg: number
  peakVolumeCbm: number | null
  weightUtilisation: number | null
  volumeUtilisation: number | null
  costPerTonneKm: number | null
  stops: MilkRunStopPlan[]
  sequenceMethod: string
  templateOrderKm: number | null
  shortestOrderKm: number | null
  alternatives: PlanAlternative[]
  warnings: string[]
  reason: string
  source: RouteSource
  transporter: AssignedTransporter | null
  vehicle: AssignedVehicle | null
  driver: AssignedDriver | null
}

export interface MilkRunSkippedStop {
  templateSequence: number
  label: string
  reason: string
}

export interface MilkRunTotals {
  orders: number
  stopsServed: number
  stopsSkipped: number
  trips: number
  inboundKg: number
  outboundKg: number
  distanceKm: number
  cost: number | null
  costPerTonneKm: number | null
}

export interface MilkRunPlan {
  code: string
  name: string
  date: string
  trips: MilkRunTrip[]
  skipped: MilkRunSkippedStop[]
  unplanned: UnplannedOrder[]
  totals: MilkRunTotals
  source: RouteSource | null
  warnings: string[]
}

// ---- Planning KPIs

export interface DailyKpi {
  date: string
  plans: number
  ordersPlanned: number
  ordersUnplanned: number
  vehicles: number
  cost: number
  averageWeightUtilisation: number | null
}

export interface PlanningKpis {
  plans: number
  ordersTotal: number
  ordersPlanned: number
  ordersUnplanned: number
  vehiclesUsed: number
  totalFreightCost: number
  consolidationSaving: number
  backhaulSaving: number
  totalSavings: number
  averageWeightUtilisation: number | null
  averageVolumeUtilisation: number | null
  totalDistanceKm: number
  costPerTonneKm: number | null
  averageStopsPerVehicle: number | null
  ftlPercent: number
  ptlPercent: number
  consolidatedPercent: number
  returnPickupPercent: number
  daily: DailyKpi[]
  unplannedReasons: { code: string; orders: number }[]
  transporters: { name: string; vehicles: number; cost: number }[]
  vehicleTypes: { name: string; vehicles: number }[]
  totalLoadedKm?: number
  totalEmptyKm?: number
  emptyKmPercent?: number | null
  costPerTonne?: number | null
  costPerShipment?: number | null
}

export interface DashboardDto {
  from: string
  to: string
  kpis: PlanningKpis
}

// ---- Delivery and proof of delivery

export type PodStatus = 'Awaiting' | 'Uploaded' | 'Verified' | 'Rejected'
export type PodStage = 'DeliveryPending' | 'AwaitingProof' | 'ProofUploaded' | 'ProofRejected' | 'ProofVerified'

export interface RecordDeliveryRequest {
  deliveredAt: string | null
  receiverName: string
  deliveredPackages: number | null
  damagedPackages: number | null
  remarks: string | null
}

export interface PodDocumentDto {
  id: string
  shipmentId: string
  orderId: string
  fileName: string
  contentType: string
  sizeBytes: number
  uploadedAt: string
}

export interface PodLineDto {
  shipmentId: string
  shipmentNumber: string
  orderId: string
  orderNumber: string
  lrNumber: string | null
  consignee: string
  consigneeCity: string
  transporterId: string | null
  transporterName: string | null
  stage: PodStage
  deliveredAt: string | null
  receiverName: string | null
  packagesShipped: number | null
  deliveredPackages: number | null
  damagedPackages: number | null
  shortagePackages: number | null
  hasException: boolean
  ageDays: number | null
  overdue: boolean
  rejectionReason: string | null
  documents: number
}

export interface ListPodParams {
  stage?: PodStage
  search?: string
  overdueOnly?: boolean
  transporterId?: string
  page?: number
  pageSize?: number
}

export interface AgeingDto {
  overdueDays: number
  outstanding: number
  overdue: number
  withExceptions: number
  buckets: { label: string; count: number }[]
  transporters: { transporterId: string | null; name: string; outstanding: number; overdue: number; oldestDays: number }[]
}

// ---- Transporter performance ----

export type KpiType =
  | 'OnTimePickup' | 'OnTimeDelivery' | 'PlacementCompliance' | 'TenderAcceptance' | 'PodCompliance' | 'ClaimsRate'
  | 'CostPerformance' | 'Availability' | 'NoShowRate' | 'VehicleReplacementRate' | 'PodRejectionRate'

export type DelayAttribution = 'None' | 'Carrier' | 'NonCarrier' | 'Unattributed'
export type ExecutionEventType =
  | 'PickupAppointment' | 'VehicleArrival' | 'LoadingStart' | 'LoadingComplete' | 'VehicleDeparture' | 'DeliveryArrival' | 'UnloadingStart' | 'DeliveryComplete'
export type ExecutionStatus = 'NotStarted' | 'AtPickup' | 'PickedUp' | 'Delivered'
export type RankingMetric = 'OverallScore' | 'OnTimePickup' | 'OnTimeDelivery' | 'PlacementCompliance' | 'TenderAcceptance' | 'PodCompliance' | 'ClaimsRate' | 'CostPerformance' | 'Availability'

export interface KpiDto {
  kpi: KpiType
  numerator: number
  denominator: number
  value: number | null
}

export interface MonthKpisDto {
  month: string
  kpis: KpiDto[]
}

export interface OperationalMetricsDto {
  duePlacements: number
  placedOnTime: number
  placed: number
  noShows: number
  replacements: number
  averagePlacementDelayMinutes: number | null
  measuredPickups: number
  notMeasurablePickups: number
  latePickupsCarrier: number
  latePickupsNonCarrier: number
  latePickupsUnattributed: number
  averagePickupDelayMinutes: number | null
  measuredDeliveries: number
  notMeasurableDeliveries: number
  lateDeliveriesCarrier: number
  lateDeliveriesNonCarrier: number
  lateDeliveriesUnattributed: number
  averageDeliveryDelayMinutes: number | null
  pendingPod: number
  averagePodSubmissionHours: number | null
}

export interface PerformanceDto {
  transporterId: string
  from: string
  to: string
  kpis: KpiDto[]
  metrics: OperationalMetricsDto
  months: MonthKpisDto[]
}

export interface ScorecardKpiDto {
  kpi: KpiType
  value: number | null
  weight: number
  weightedScore: number | null
  numerator: number
  denominator: number
}

export interface ScorecardDto {
  id: string
  transporterId: string
  periodStart: string
  periodEnd: string
  overallScore: number | null
  generatedAt: string
  calculationVersion: number
  kpis: ScorecardKpiDto[]
}

export interface RankedKpiDto {
  kpi: KpiType
  value: number | null
  numerator: number
  denominator: number
}

export interface RankedTransporterDto {
  rank: number | null
  transporterId: string
  transporterCode: string
  transporterName: string
  region: string | null
  status: string
  metricValue: number | null
  overallScore: number | null
  ranked: boolean
  note: string | null
  kpis: RankedKpiDto[]
}

export interface RankingResultDto {
  metric: RankingMetric
  scopeLabel: string
  from: string
  to: string
  rows: RankedTransporterDto[]
}

export interface RankingParams {
  from: string
  to: string
  metric?: RankingMetric
  laneId?: string
  vehicleTypeId?: string
  region?: string
}

export interface BenchmarkRowDto {
  kpi: KpiType
  transporter: number | null
  laneAverage: number | null
  regionAverage: number | null
  modeAverage: number | null
  topPerformer: number | null
  topPerformerTransporterId: string | null
  gapToTop: number | null
  gapToLaneAverage: number | null
}

export interface BenchmarkDto {
  transporterId: string
  scopeLabel: string
  from: string
  to: string
  rows: BenchmarkRowDto[]
}

export interface ExecutionEventDto {
  eventType: ExecutionEventType
  eventAt: string
  delayReasonCode: string | null
  remarks: string | null
}

export interface ExecutionDto {
  id: string
  shipmentId: string
  shipmentNumber: string
  transporterId: string
  plannedPickupAt: string | null
  actualPickupAt: string | null
  pickupDelayMinutes: number | null
  pickupDelayReasonCode: string | null
  pickupAttribution: DelayAttribution
  plannedDeliveryAt: string | null
  actualDeliveryAt: string | null
  deliveryDelayMinutes: number | null
  deliveryDelayReasonCode: string | null
  deliveryAttribution: DelayAttribution
  status: ExecutionStatus
  events: ExecutionEventDto[]
}

export interface LaneDto {
  id: string
  transporterId: string
  originState: string
  originCity: string | null
  destinationState: string
  destinationCity: string | null
  mode: FreightMode | null
  transitSlaMinutes: number | null
  effectiveFrom: string
  effectiveTo: string | null
  isActive: boolean
  version: number
}

export interface SaveLaneRequest {
  originState: string
  originCity: string | null
  destinationState: string
  destinationCity: string | null
  mode: FreightMode | null
  transitSlaMinutes: number | null
  effectiveFrom: string
  effectiveTo: string | null
  isActive: boolean
  version: number | null
}

export interface DelayReasonSetting {
  code: string
  name: string
  attribution: 'Carrier' | 'NonCarrier' | 'Unattributed'
}

export interface TransporterSettingDto {
  key: string
  value: unknown
  isDefault: boolean
}

// ---- Transporter selection, coverage and planning rules ----

export type PlanningRuleType = 'PreferredCarrier' | 'PreferredLane' | 'AvoidForUrgent' | 'Restricted' | 'DoNotAllocate'

export interface CapabilityTypeDto {
  code: string
  name: string
}

export interface CapabilityDto {
  id: string
  transporterId: string
  code: string
  name: string
  effectiveFrom: string
  effectiveTo: string | null
  isActive: boolean
}

export interface PlanningRuleDto {
  id: string
  transporterId: string
  ruleType: PlanningRuleType
  laneId: string | null
  reason: string
  effectiveFrom: string
  effectiveTo: string | null
  isActive: boolean
  endedBecause: string | null
}

export interface SelectionRequest {
  originState: string
  originCity: string | null
  destinationState: string
  destinationCity: string | null
  mode: FreightMode
  vehicleTypeId: string | null
  weightKg: number
  volumeCbm: number | null
  date: string
  requiredCapabilities: string[]
  isUrgent: boolean
  distanceKm: number | null
}

export interface KpiPointDto {
  value: number | null
  denominator: number
  sufficient: boolean
  scope: string
}

export interface CandidateEvaluationDto {
  transporterId: string
  code: string
  name: string
  status: string
  eligible: boolean
  reasons: string[]
  warnings: string[]
  laneId: string | null
  preferred: boolean
  restrictedForPlanning: boolean
  rate: { contractId: string; contractReference: string; total: number; note: string | null } | null
  availableVehicles: number
  kpis: Record<string, KpiPointDto>
}

export interface ScoreComponentDto {
  factor: string
  score: number
  weight: number
  contribution: number
  basis: string
  sufficient: boolean
}

export interface RankedCandidateDto {
  rank: number
  candidate: CandidateEvaluationDto
  recommendationScore: number
  components: ScoreComponentDto[]
  explanations: string[]
  comparisons: string[]
}

export interface RecommendationResultDto {
  recommended: RankedCandidateDto | null
  ranked: RankedCandidateDto[]
  candidates: CandidateEvaluationDto[]
}

// ---- Placements, claims, costs, capacity and alerts ----

export type PlacementStatus = 'VehicleAssigned' | 'Reported' | 'Placed' | 'LoadingStarted' | 'NoShow' | 'Cancelled'
export type ClaimType = 'Damage' | 'Shortage' | 'LossTheft'
export type ClaimStatus = 'Open' | 'Resolved'
export type AlertSeverity = 'Low' | 'Medium' | 'High' | 'Critical'
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved'

export interface PlacementDto {
  id: string
  shipmentId: string
  shipmentNumber: string
  transporterId: string
  vehicleId: string | null
  vehicleRegistration: string | null
  requiredAt: string
  reportedAt: string | null
  placedAt: string | null
  loadingStartedAt: string | null
  status: PlacementStatus
  slaStatus: 'OnTime' | 'Late' | 'Pending' | 'Overdue' | 'NoShow' | 'Cancelled'
  delayMinutes: number | null
  replacementCount: number
  reason: string | null
  events: { eventType: string; eventAt: string; remarks: string | null }[]
}

export interface ClaimDto {
  id: string
  transporterId: string
  shipmentId: string | null
  shipmentNumber: string | null
  claimType: ClaimType
  claimDate: string
  claimValue: number
  status: ClaimStatus
  remarks: string | null
  resolvedAt: string | null
}

export interface LoadCostDto {
  id: string
  transporterId: string
  shipmentId: string
  shipmentNumber: string
  serviceDate: string
  agreedAmount: number
  invoicedAmount: number
  onBudget: boolean
}

export interface CapacityDayDto {
  id: string
  transporterId: string
  date: string
  vehiclesCommitted: number
  vehiclesAvailable: number
}

export interface AlertDto {
  id: string
  alertType: string
  severity: AlertSeverity
  transporterId: string
  transporterName: string | null
  shipmentId: string | null
  shipmentNumber: string | null
  message: string
  status: AlertStatus
  createdAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
  resolution: string | null
}

// ---- Contacts and branches ----

export interface ContactDto {
  id: string
  transporterId: string
  name: string
  designation: string | null
  email: string | null
  phone: string | null
  contactType: string
  isPrimary: boolean
  isActive: boolean
  version: number
}

export interface SaveContactRequest {
  name: string
  designation: string | null
  email: string | null
  phone: string | null
  contactType: string
  isPrimary: boolean
  isActive: boolean
  version: number | null
}

export interface BranchDto {
  id: string
  transporterId: string
  code: string
  name: string
  address: string | null
  city: string | null
  state: string | null
  latitude: number | null
  longitude: number | null
  contactName: string | null
  contactPhone: string | null
  isActive: boolean
  version: number
}

export interface SaveBranchRequest {
  code: string
  name: string
  address: string | null
  city: string | null
  state: string | null
  latitude: number | null
  longitude: number | null
  contactName: string | null
  contactPhone: string | null
  isActive: boolean
  version: number | null
}

// ---- Deliveries and proof of delivery (module 'pd')

export type DeliveryStatus = 'Planned' | 'Assigned' | 'EnRoute' | 'Arrived' | 'Attempted' | 'Delivered' | 'PartiallyDelivered' | 'Refused' | 'Failed' | 'Closed' | 'Cancelled'
export type DeliveryOutcome = 'Full' | 'Partial' | 'Shortage' | 'Damaged' | 'Refused' | 'Failed'
export type RemainingDisposition = 'Backorder' | 'Reschedule' | 'Return' | 'Cancel' | 'Exception'
export type ProofStatus = 'Pending' | 'Draft' | 'Captured' | 'Submitted' | 'UnderReview' | 'Accepted' | 'Rejected' | 'ResubmissionRequired' | 'Cancelled'
export type ProofMethod = 'Signature' | 'Otp' | 'Photo' | 'Contactless' | 'Qr'
export type EvidenceType = 'PackagePhoto' | 'DamagePhoto' | 'LocationPhoto' | 'SitePhoto' | 'SealPhoto' | 'VehiclePhoto' | 'PodDocument'
export type GeofenceStatus = 'NotApplicable' | 'Inside' | 'Outside' | 'GpsUnavailable' | 'AccuracyInsufficient'
export type ValidationOutcome = 'Valid' | 'Warning' | 'RequiresReview' | 'Invalid'
export type OcrStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed'
export type OcrFieldStatus = 'NotChecked' | 'Matched' | 'Mismatch' | 'LowConfidence'
export type DiscrepancyType = 'Shortage' | 'Damage' | 'Rejection'
export type DeliveryEventType =
  | 'Created' | 'Assigned' | 'Started' | 'Arrived' | 'AttemptFailed' | 'Delivered' | 'PartiallyDelivered' | 'Failed' | 'Refused' | 'Rescheduled' | 'Cancelled' | 'Closed' | 'OtpIssued' | 'OtpVerified' | 'SiteReached'
export type DeliveryExceptionType =
  | 'Shortage' | 'Damage' | 'CustomerRefusal' | 'DeliveryFailed' | 'LateDelivery' | 'AddressIssue' | 'PodMissing' | 'PodRejected' | 'QuantityMismatch' | 'GpsException'
  | 'SignatureMissing' | 'OcrValidationFailed' | 'DuplicatePod' | 'PartialDelivery'
export type DeliveryExceptionStatus = 'Open' | 'Acknowledged' | 'UnderInvestigation' | 'ActionRequired' | 'Resolved' | 'Closed' | 'Escalated'
export type DeliveryExceptionSeverity = 'Low' | 'Medium' | 'High' | 'Critical'
export type ResponsibleParty = 'Unknown' | 'Transporter' | 'Warehouse' | 'Customer' | 'Supplier'
export type SyncStatus = 'Pending' | 'Synced' | 'Failed' | 'Conflict'

export interface GeoDto {
  latitude: number | null
  longitude: number | null
  accuracyM: number | null
}

/** Sent with every action from a device: where it was, which device, and when it really happened (it may have been offline). */
export interface DeviceContext {
  fix: GeoDto | null
  deviceReference: string | null
  at: string | null
}

export interface DeliveryItemDto {
  id: string
  sku: string
  description: string
  orderedQuantity: number
  dispatchedQuantity: number
  deliveredQuantity: number | null
  shortQuantity: number
  damagedQuantity: number
  rejectedQuantity: number
  unitOfMeasure: string
  remarks: string | null
  shortageReasonCode: string | null
  damageType: string | null
  damageReason: string | null
  damageDescription: string | null
  unaccounted: number | null
}

export interface DeliverySummaryDto {
  id: string
  number: string
  shipmentReference: string | null
  customerName: string
  destinationReference: string | null
  transporterId: string | null
  transporterReference: string | null
  vehicleReference: string | null
  plannedDeliveryAt: string
  actualDeliveryAt: string | null
  status: DeliveryStatus
  outcome: DeliveryOutcome | null
  podStatus: ProofStatus
  podId: string | null
  hasDiscrepancy: boolean
  openExceptions: number
  serviceType?: string | null
}

export interface DeliveryDto {
  summary: DeliverySummaryDto
  orderReference: string | null
  loadReference: string | null
  tripReference: string | null
  lrNumber: string | null
  sequence: number
  driverName: string | null
  customerReference: string | null
  customerPhone: string | null
  customerEmail: string | null
  originReference: string | null
  destinationAddress: string | null
  customerLatitude: number | null
  customerLongitude: number | null
  geofenceRadiusM: number | null
  windowStart: string | null
  windowEnd: string | null
  actualArrivalAt: string | null
  remainingDisposition: RemainingDisposition | null
  hasQuantityMismatch: boolean
  otpIssued: boolean
  otpVerified: boolean
  items: DeliveryItemDto[]
  attempts: { attemptNumber: number; attemptedAt: string; result: 'Failed' | 'Delivered'; reasonCode: string | null; recipientName: string | null; driverRemarks: string | null; customerRemarks: string | null; latitude: number | null; longitude: number | null }[]
  events: { at: string; type: DeliveryEventType; latitude: number | null; longitude: number | null; deviceReference: string | null; remarks: string | null }[]
  discrepancies: { id: string; itemId: string; sku: string; type: DiscrepancyType; quantity: number; reasonCode: string | null; description: string | null; customerAcknowledged: boolean; claimReference: string | null }[]
  reconciliation: { itemId: string; sku: string; dispatched: number; accounted: number; unaccounted: number; reconciled: boolean; problems: string[] }[]
  version: number
}

export interface ListDeliveriesParams {
  status?: DeliveryStatus
  podStatus?: ProofStatus
  search?: string
  transporterId?: string
  customer?: string
  from?: string
  to?: string
  hasException?: boolean
  hasDiscrepancy?: boolean
  vehicle?: string
  lane?: string
  serviceType?: string
  page?: number
  pageSize?: number
}

export interface SaveDeliveryRequest {
  shipmentReference: string | null
  orderReference: string | null
  loadReference: string | null
  tripReference: string | null
  lrNumber: string | null
  sequence: number
  transporterId: string | null
  transporterReference: string | null
  vehicleId: string | null
  vehicleReference: string | null
  driverName: string | null
  customerReference: string | null
  customerName: string
  customerPhone: string | null
  customerEmail: string | null
  originReference: string | null
  destinationReference: string | null
  destinationAddress: string | null
  customerLatitude: number | null
  customerLongitude: number | null
  geofenceRadiusM: number | null
  plannedDeliveryAt: string
  windowStart: string | null
  windowEnd: string | null
  items: { sku: string; description: string; orderedQuantity: number; dispatchedQuantity: number | null; unitOfMeasure: string | null }[]
  version?: number | null
}

export interface ItemQuantityRequest {
  itemId: string
  deliveredQuantity: number
  shortQuantity: number
  damagedQuantity: number
  rejectedQuantity: number
  shortageReasonCode: string | null
  damageType: string | null
  damageReason: string | null
  damageDescription: string | null
  remarks: string | null
}

export interface ProofRequest {
  method: ProofMethod
  recipientName: string | null
  recipientDesignation: string | null
  recipientPhone: string | null
  recipientRemarks: string | null
  driverConfirmed: boolean
  customerAcknowledged: boolean
}

export interface CompleteDeliveryRequest {
  outcome: DeliveryOutcome
  items: ItemQuantityRequest[]
  remainingDisposition: RemainingDisposition | null
  driverRemarks: string | null
  proof: ProofRequest
  context: DeviceContext | null
}

export interface AttemptRequest {
  reasonCode: string
  driverRemarks: string | null
  customerRemarks: string | null
  recipientName: string | null
  context: DeviceContext | null
}

export interface FailDeliveryRequest {
  reasonCode: string
  remarks: string | null
  context: DeviceContext | null
}

export interface RefuseDeliveryRequest {
  reasonCode: string
  recipientName: string | null
  remarks: string | null
  customerAcknowledged: boolean
  context: DeviceContext | null
}

export interface PodEvidenceDto {
  id: string
  type: EvidenceType
  fileName: string
  contentType: string
  sizeBytes: number
  fileHash: string
  capturedAt: string
  latitude: number | null
  longitude: number | null
  deviceReference: string | null
  width: number | null
  height: number | null
  warnings: string | null
  removed: boolean
  removedReason: string | null
}

export interface PodSummaryDto {
  id: string
  podNumber: string
  version: number
  isCurrent: boolean
  deliveryId: string
  deliveryNumber: string
  customerName: string
  transporterReference: string | null
  status: ProofStatus
  deliveredAt: string | null
  submittedAt: string | null
  approvedAt: string | null
  hoursSinceSubmitted: number | null
  validation: ValidationOutcome
  hasDiscrepancy: boolean
  ocr: OcrStatus | null
}

export interface OcrFieldDto {
  name: string
  rawValue: string | null
  normalizedValue: string | null
  confidence: number
  status: OcrFieldStatus
  message: string | null
  reviewedValue: string | null
  effectiveValue: string | null
  expected: string | null
  threshold: number
}

export interface OcrResultDto {
  id: string
  evidenceId: string
  provider: string
  status: OcrStatus
  overallConfidence: number | null
  queuedAt: string
  processedAt: string | null
  error: string | null
  fields: OcrFieldDto[]
}

export interface PodDto {
  summary: PodSummaryDto
  method: ProofMethod | null
  recipientName: string | null
  recipientDesignation: string | null
  recipientPhone: string | null
  arrivalAt: string | null
  capturedAt: string
  reviewedAt: string | null
  latitude: number | null
  longitude: number | null
  gpsAccuracy: number | null
  geofence: GeofenceStatus
  driverRemarks: string | null
  recipientRemarks: string | null
  driverConfirmed: boolean
  otpVerified: boolean
  customerAcknowledged: boolean
  rejectionReason: string | null
  rejectionCount: number
  autoAccepted: boolean
  items: { deliveryItemId: string; sku: string; orderedQuantity: number; dispatchedQuantity: number; deliveredQuantity: number; shortQuantity: number; damagedQuantity: number; rejectedQuantity: number; remarks: string | null }[]
  evidence: PodEvidenceDto[]
  signatures: { id: string; signerName: string; signerDesignation: string | null; capturedAt: string; latitude: number | null; longitude: number | null; verificationMethod: string }[]
  validations: { type: string; check: string; status: ValidationOutcome; message: string; validatedAt: string }[]
  reviews: { at: string; action: string; fieldName: string | null; oldValue: string | null; newValue: string | null; reason: string | null; by: string | null }[]
  ocr: OcrResultDto[]
  missing: string[]
  rowVersion: number
}

export interface PodReviewDto {
  pod: PodDto
  delivery: DeliveryDto
  documentEvidenceId: string | null
  ocr: OcrResultDto | null
}

export interface ListPodsParams {
  status?: ProofStatus
  search?: string
  transporterId?: string
  overdue?: boolean
  currentOnly?: boolean
  page?: number
  pageSize?: number
}

export interface DeliveryExceptionSummaryDto {
  id: string
  number: string
  deliveryId: string
  deliveryNumber: string
  customerName: string | null
  transporterReference: string | null
  vehicleReference: string | null
  podId: string | null
  type: DeliveryExceptionType
  severity: DeliveryExceptionSeverity
  status: DeliveryExceptionStatus
  ownerUserId: string | null
  department: string | null
  raisedAt: string
  dueAt: string
  overdue: boolean
  ageHours: number
  claimReference: string | null
}

export interface DeliveryExceptionDto {
  summary: DeliveryExceptionSummaryDto
  description: string
  rootCause: string | null
  responsibleParty: ResponsibleParty
  actionTaken: string | null
  resolution: string | null
  financialImpact: number | null
  resolvedAt: string | null
  escalatedAt: string | null
  notes: { at: string; text: string; by: string | null }[]
  version: number
  attachments?: ExceptionAttachmentDto[] | null
}

export interface ExceptionAttachmentDto {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  note: string | null
  at: string
  by: string | null
}

export interface ListDeliveryExceptionsParams {
  type?: DeliveryExceptionType
  status?: DeliveryExceptionStatus
  severity?: DeliveryExceptionSeverity
  transporterId?: string
  deliveryId?: string
  openOnly?: boolean
  overdue?: boolean
  page?: number
  pageSize?: number
}

export interface ReasonSetting {
  code: string
  name: string
  evidenceRequired: boolean
}

export interface PodRulesSetting {
  signatureRequired: boolean
  otpRequired: boolean
  gpsRequired: boolean
  photoRequired: boolean
  minPhotos: number
  geofenceRequired: boolean
  contactlessAllowed: boolean
  galleryAllowed: boolean
  maxGpsAccuracyM: number
  otpValidityMinutes: number
  otpMaxAttempts: number
}

export interface MobileConfigDto {
  pod: PodRulesSetting
  attemptReasons: ReasonSetting[]
  shortageReasons: ReasonSetting[]
  damageTypes: ReasonSetting[]
  refusalReasons: ReasonSetting[]
  quantity: { overDeliveryPct: number; blockUnreconciledCompletion: boolean }
  discrepancy: { shortageAcknowledgementRequired: boolean; damageAcknowledgementRequired: boolean; refusalAcknowledgementRequired: boolean; autoCreateClaim: boolean }
  images: { maxBytes: number; minWidth: number; minHeight: number; rejectLowResolution: boolean }
}

export interface MobileBundleDto {
  deliveries: { delivery: DeliveryDto; pod: PodDto | null }[]
  config: MobileConfigDto
  downloadedAt: string
}

export type SyncOperation = 'start' | 'arrive' | 'attempt' | 'complete' | 'fail' | 'refuse' | 'otp-verify' | 'otp-issue'

export interface SyncCommand {
  clientRecordId: string
  type: SyncOperation
  deliveryId: string
  clientCreatedAt: string
  clientUpdatedAt: string
  payload: unknown
}

export interface SyncResultDto {
  clientRecordId: string
  status: SyncStatus
  duplicate: boolean
  attempt: number
  error: string | null
  errorCode: string | null
  deliveryStatus: DeliveryStatus | null
  podId: string | null
}

export interface DeliverySettingDto {
  key: string
  value: unknown
  isCustomised: boolean
}

// ---- Dashboard, ageing, compliance, notifications, claims and billing

export type AgeingStage = 'PendingSubmission' | 'PendingReview' | 'Rejected' | 'ResubmissionRequired'

export interface DashboardSummaryDto {
  from: string
  to: string
  deliveriesToday: number
  delivered: number
  partiallyDelivered: number
  failed: number
  refused: number
  closed: number
  podPending: number
  podInPreparation: number
  podSubmitted: number
  podUnderReview: number
  podRejected: number
  podResubmissionRequired: number
  podAccepted: number
  shortageCases: number
  damageCases: number
  openExceptions: number
  overdueExceptions: number
  openClaims: number
  unreadNotifications: number
}

/** A null rate means there was nothing to measure, which is different from zero. */
export interface ComplianceMetricsDto {
  delivered: number
  podSubmitted: number
  podPending: number
  podRejected: number
  podAccepted: number
  submissionCompliance: number | null
  acceptanceRate: number | null
  rejectionRate: number | null
  averageSubmissionHours: number | null
  averageReviewHours: number | null
  averageResubmissionHours: number | null
  onTimeRate: number | null
}

export interface ProofComplianceDto {
  from: string
  to: string
  groupBy: 'transporter' | 'customer' | 'lane'
  overall: ComplianceMetricsDto
  rows: { key: string; name: string; metrics: ComplianceMetricsDto }[]
}

export interface ProofAgeingDto {
  bucketLabels: string[]
  stages: { stage: AgeingStage; label: string; count: number; overdue: number; targetHours: number; buckets: number[] }[]
  bucketTotals: number[]
  topTransporters: { name: string; overdue: number; total: number }[]
  topCustomers: { name: string; overdue: number; total: number }[]
  topLocations: { name: string; overdue: number; total: number }[]
}

export interface AgeingItemDto {
  stage: AgeingStage
  deliveryId: string
  deliveryNumber: string
  podId: string | null
  customerName: string
  transporterReference: string | null
  destination: string | null
  ageHours: number
  bucket: string
  bucketIndex: number
  overdue: boolean
  targetHours: number
}

export interface DeliveryNotificationDto {
  id: string
  kind: string
  title: string
  body: string
  deliveryId: string | null
  podId: string | null
  exceptionId: string | null
  createdAt: string
  read: boolean
}

export interface ClaimResultDto {
  discrepancyId: string
  sku: string
  type: DiscrepancyType
  quantity: number
  reference: string
  system: string
}

export interface BillingStatusDto {
  status: string
  billingEligible: boolean
  invoiceHold: boolean
  at: string | null
  explanation: string
}

export interface ProofPerformanceDto {
  from: string
  to: string
  deliveries: number
  delivered: number
  onTimeRate: number | null
  proofInTimeRate: number | null
  firstTimeAcceptanceRate: number | null
  rejectionRate: number | null
  shortageRate: number | null
  damageRate: number | null
  refusals: number
  failures: number
}

// ---- Shipment tracking & visibility (module 4)

export type TrackingHealth = 'NotStarted' | 'Healthy' | 'Stale' | 'Lost' | 'Completed'
export type TrackingExecution =
  | 'Planned' | 'EnRouteToOrigin' | 'ArrivedOrigin' | 'Loading' | 'Departed' | 'InTransit' | 'ApproachingDestination' | 'ArrivedDestination' | 'Delivered' | 'Cancelled' | 'Completed'
export type RiskStatus = 'Unknown' | 'OnTime' | 'AtRisk' | 'Delayed' | 'SeverelyDelayed'
export type RiskLevel = 'Low' | 'Medium' | 'High' | 'Critical'
export type TrackSeverity = 'Informational' | 'Warning' | 'High' | 'Critical'
export type TrackStopStatus = 'Pending' | 'Approaching' | 'Arrived' | 'Departed' | 'Skipped'
export type TrackAlertType =
  | 'TrackingStale' | 'TrackingLost' | 'RouteDeviation' | 'ExcessiveDwell' | 'UnplannedStop' | 'EtaAtRisk' | 'EtaDelayed' | 'DeliverySlaRisk' | 'GeofenceException' | 'GpsAnomaly' | 'VehicleStationary'
  | 'GpsUnavailable'
export type TrackAlertStatus = 'Open' | 'Acknowledged' | 'Resolved'
export type TrackExceptionStatus = 'Open' | 'Acknowledged' | 'InProgress' | 'Escalated' | 'Resolved' | 'Closed'
export type DelayReason =
  | 'Unknown' | 'Traffic' | 'VehicleBreakdown' | 'WarehouseDelay' | 'CustomerDelay' | 'LoadingDelay' | 'UnloadingDelay' | 'Weather' | 'RoadClosure' | 'RouteDeviation' | 'Documentation'
  | 'BorderCheckpost' | 'Accident' | 'Other'
export type GeofenceType = 'Origin' | 'Destination' | 'Customer' | 'Warehouse' | 'Hub' | 'CrossDock' | 'Depot' | 'Toll' | 'RestrictedArea' | 'HighRiskZone' | 'Custom'

export interface TrackStopDto {
  id: string
  sequence: number
  kind: 'Pickup' | 'Drop'
  name: string
  city: string | null
  latitude: number | null
  longitude: number | null
  radiusM: number
  placeType: GeofenceType
  reference: string | null
  customerName: string | null
  plannedArrival: string | null
  windowStart: string | null
  windowEnd: string | null
  expectedDwellMinutes: number | null
  status: TrackStopStatus
  arrivedAt: string | null
  departedAt: string | null
  etaAt: string | null
  etaConfidence: number | null
  delayMinutes: number
  risk: RiskStatus
  alongKm: number | null
}

export interface TrackedSummaryDto {
  id: string
  shipmentId: string
  shipmentReference: string
  tripReference: string
  transporterId: string | null
  transporterReference: string | null
  vehicleReference: string | null
  driverName: string | null
  driverPhone: string | null
  customerName: string | null
  origin: string | null
  destination: string | null
  execution: TrackingExecution
  tracking: TrackingHealth
  risk: RiskStatus
  delivery: 'Pending' | 'PartlyDelivered' | 'Delivered'
  plannedArrivalAt: string | null
  etaAt: string | null
  systemEtaAt: string | null
  etaOverridden: boolean
  etaConfidence: number | null
  delayMinutes: number
  progressPct: number | null
  remainingKm: number | null
  latitude: number | null
  longitude: number | null
  lastCapturedAt: string | null
  speedKph: number | null
  heading: number | null
  minutesSinceLastLocation: number | null
  openExceptions: number
  onRoute: boolean
  moving: boolean
  startedAt: string | null
  completedAt: string | null
}

export interface TrackedDetailDto {
  summary: TrackedSummaryDto
  stops: TrackStopDto[]
  plannedDistanceKm: number | null
  plannedDurationMinutes: number | null
  travelledKm: number
  offRouteKm: number | null
  routeSource: string
  plannedStartAt: string | null
  etaOverrideAt: string | null
  etaOverrideReason: string | null
  delayReason: DelayReason | null
  delayNote: string | null
  currentSessionId: string | null
  sessionStatus: string | null
  deviceId: string | null
  batteryPercentage: number | null
  networkType: string | null
  locationPermission: string | null
  version: number
}

export interface TimelineEntryDto {
  at: string
  kind: 'Planned' | 'Estimated' | 'Actual' | string
  label: string
  detail: string | null
  source: string | null
  type: string | null
  latitude: number | null
  longitude: number | null
  stopId: string | null
  reason: string | null
}

export interface EtaStopDto {
  stopId: string
  name: string
  kind: 'Pickup' | 'Drop'
  plannedAt: string | null
  etaAt: string | null
  confidence: number | null
  delayMinutes: number
  risk: RiskStatus
  level: RiskLevel
  status: TrackStopStatus
}

export interface EtaHistoryDto {
  predictedAt: string
  eta: string
  remainingKm: number
  confidence: number
  riskLevel: RiskLevel
  delayMinutes: number
  isFinalDestination: boolean
  stopId: string | null
}

export interface TrackEtaDto {
  plannedAt: string | null
  systemEtaAt: string | null
  etaAt: string | null
  overridden: boolean
  overrideAt: string | null
  overrideReason: string | null
  confidence: number | null
  risk: RiskStatus
  level: RiskLevel
  delayMinutes: number
  calculationVersion: string
  stops: EtaStopDto[]
  history: EtaHistoryDto[]
}

export interface RouteDeviationDto {
  id: string
  detectedAt: string
  latitude: number
  longitude: number
  distanceFromRouteKm: number
  durationMinutes: number
  severity: TrackSeverity
  status: 'Open' | 'Resolved'
  reason: DelayReason | null
  reasonNote: string | null
  resolvedAt: string | null
}

export interface TrackRouteDto {
  points: number[][]
  lengthKm: number
  source: string
  plannedDistanceKm: number | null
  plannedDurationMinutes: number | null
  travelledKm: number
  remainingKm: number | null
  progressPct: number | null
  stops: TrackStopDto[]
  deviations: RouteDeviationDto[]
}

export interface TrackLocationDto {
  id: string
  latitude: number
  longitude: number
  accuracyMeters: number | null
  speedKph: number | null
  heading: number | null
  capturedAt: string
  receivedAt: string
  validation: 'Valid' | 'Suspicious' | 'Rejected'
  anomalies: string
  reasons: string | null
  isLate: boolean
  deviceId: string
  source: string
}

export interface CurrentLocationDto {
  vehicleReference: string | null
  tripReference: string | null
  shipmentReference: string | null
  latitude: number
  longitude: number
  accuracyMeters: number | null
  speedKph: number | null
  heading: number | null
  lastCapturedAt: string
  lastReceivedAt: string
  health: TrackingHealth
  moving: boolean
  ageMinutes: number
  batteryPercentage: number | null
  networkType: string | null
  driverName: string | null
  transporterReference: string | null
}

export interface VehicleTrackingDto {
  position: CurrentLocationDto
  shipmentId: string | null
  shipmentReference: string | null
  tripReference: string | null
  execution: TrackingExecution | null
  risk: RiskStatus | null
  etaAt: string | null
  origin: string | null
  destination: string | null
  todayKm: number
  driverPhone: string | null
}

export interface TrackAlertDto {
  id: string
  type: TrackAlertType
  severity: TrackSeverity
  status: TrackAlertStatus
  shipmentId: string
  shipmentReference: string
  tripReference: string
  vehicleReference: string | null
  message: string
  raisedAt: string
  dueAt: string
  overdue: boolean
  acknowledgedAt: string | null
  resolvedAt: string | null
  resolutionNote: string | null
  exceptionId: string | null
}

export interface TrackExceptionSummaryDto {
  id: string
  number: string
  type: TrackAlertType
  severity: TrackSeverity
  status: TrackExceptionStatus
  shipmentId: string
  shipmentReference: string
  tripReference: string
  vehicleReference: string | null
  transporterId: string | null
  transporterReference: string | null
  description: string
  raisedAt: string
  dueAt: string
  overdue: boolean
  ownerUserId: string | null
  department: string | null
  escalationLevel: number
  escalatedTo: string | null
  conditionCleared: boolean
  ageMinutes: number
}

export interface TrackExceptionDto {
  summary: TrackExceptionSummaryDto
  rootCause: string | null
  delayReason: DelayReason | null
  actionTaken: string | null
  resolvedAt: string | null
  closedAt: string | null
  escalatedAt: string | null
  driverName: string | null
  driverPhone: string | null
  lastLatitude: number | null
  lastLongitude: number | null
  lastCapturedAt: string | null
  notes: { at: string; text: string; by: string | null }[]
  version: number
}

export interface ControlTowerSummaryDto {
  active: number
  onTime: number
  atRisk: number
  delayed: number
  trackingStale: number
  trackingLost: number
  routeDeviations: number
  excessDwell: number
  openExceptions: number
  completedToday: number
  notStarted: number
  openAlerts: number
  asOf: string
}

export interface GeofenceDto {
  id: string
  code: string
  name: string
  type: GeofenceType
  centerLatitude: number
  centerLongitude: number
  radiusMeters: number
  polygon: number[][] | null
  status: 'Active' | 'Inactive'
  effectiveFrom: string | null
  effectiveTo: string | null
  version: number
}

export interface SaveGeofenceRequest {
  code: string
  name: string
  type: GeofenceType
  centerLatitude: number
  centerLongitude: number
  radiusMeters: number
  polygon: number[][] | null
  effectiveFrom: string | null
  effectiveTo: string | null
  status: 'Active' | 'Inactive' | null
  version: number | null
}

export interface TrackingHealthDto {
  health: TrackingHealth
  ageMinutes: number | null
  sessionStatus: string | null
  deviceId: string | null
  driverReference: string | null
  batteryPercentage: number | null
  networkType: string | null
  locationPermission: string | null
  appVersion: string | null
  lastSeenAt: string | null
  gaps: { id: string; gapStart: string; gapEnd: string | null; durationMinutes: number; lastKnownLatitude: number; lastKnownLongitude: number; severity: TrackSeverity }[]
  locationCount: number
  suspiciousCount: number
  lateCount: number
}

export interface JourneyAnalyticsDto {
  plannedKm: number | null
  actualKm: number
  kmVariance: number | null
  plannedMinutes: number | null
  actualMinutes: number | null
  minutesVariance: number | null
  plannedStops: number
  stopsReached: number
  unplannedStops: number
  totalDwellMinutes: number
  deviationMinutes: number
  deviationCount: number
  dwells: { id: string; place: string | null; kind: 'PlannedStop' | 'UnplannedStop'; startAt: string; endAt: string | null; durationMinutes: number; expectedDurationMinutes: number; excessDurationMinutes: number; status: string }[]
}

export interface CustomerLinkDto {
  id: string
  shipmentId: string
  shipmentReference: string
  customerReference: string | null
  customerName: string | null
  createdAt: string
  expiresAt: string
  status: 'Active' | 'Revoked' | 'Expired'
  revokedAt: string | null
  viewCount: number
  lastViewedAt: string | null
}

export interface CreatedLinkDto {
  link: CustomerLinkDto
  token: string
  path: string
}

export interface CustomerTrackingDto {
  shipmentReference: string
  origin: string | null
  destination: string | null
  statusLabel: string
  steps: { label: string; state: string; at: string | null }[]
  latitude: number | null
  longitude: number | null
  locationAsOf: string | null
  locationLabel: string | null
  etaAt: string | null
  deliveryWindowStart: string | null
  deliveryWindowEnd: string | null
  riskLabel: string
  delivered: boolean
  deliveredAt: string | null
  route: number[][]
  asOf: string
}

export interface TrackingSettingDto {
  key: string
  value: Record<string, unknown>
  isCustomised: boolean
}

export interface ListTrackedParams {
  search?: string
  transporterId?: string
  vehicle?: string
  driver?: string
  customer?: string
  origin?: string
  destination?: string
  execution?: TrackingExecution
  tracking?: TrackingHealth
  risk?: RiskStatus
  hasException?: boolean
  activeOnly?: boolean
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export interface MobileTripDto {
  shipmentId: string
  tripReference: string
  shipmentReference: string
  vehicleReference: string | null
  driverName: string | null
  origin: string | null
  destination: string | null
  execution: TrackingExecution
  risk: RiskStatus
  etaAt: string | null
  plannedStartAt: string | null
  stops: { sequence: number; kind: 'Pickup' | 'Drop'; name: string; city: string | null; plannedArrival: string | null; status: TrackStopStatus; latitude: number | null; longitude: number | null }[]
  session: TrackingSessionDto | null
  canStart: boolean
}

export interface TrackingSessionDto {
  sessionId: string
  reference: string
  tripReference: string
  status: string
  startedAt: string
  stoppedAt: string | null
  lastLocationAt: string | null
  locationCount: number
  intervalSeconds: number
  stationaryIntervalSeconds: number
  approachingIntervalSeconds: number
  approachingKm: number
  adaptive: boolean
  staleAfterMinutes: number
  lostAfterMinutes: number
}

export interface QueuedFix {
  clientLocationId: string
  latitude: number
  longitude: number
  accuracyMeters: number | null
  speedKph: number | null
  heading: number | null
  capturedAtUtc: string
  mockLocation: boolean
}

export interface TrackBatchResult {
  accepted: number
  duplicates: number
  suspicious: number
  late: number
  rejected: { clientLocationId: string; reason: string }[]
}
