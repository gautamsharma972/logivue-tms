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
