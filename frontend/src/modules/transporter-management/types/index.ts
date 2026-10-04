// Mirrors the Transporter Management API. Enums arrive as their names (the API serialises enums as strings).

export type TransporterStatus = 'Draft' | 'Submitted' | 'Approved' | 'Active' | 'Suspended' | 'Inactive' | 'Blacklisted';
export type RecordStatus = 'Active' | 'Inactive';
export type Severity = 'Low' | 'Medium' | 'High' | 'Critical';
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved';
export type TenderStatus = 'Draft' | 'Sent' | 'Viewed' | 'Accepted' | 'Rejected' | 'Expired' | 'Withdrawn' | 'Awarded' | 'Cancelled';
export type TenderType = 'Direct' | 'Sequential' | 'Broadcast';
export type PlacementStatus = 'Requested' | 'Confirmed' | 'VehicleAssigned' | 'Reported' | 'Placed' | 'LoadingStarted' | 'NoShow' | 'Replaced' | 'Cancelled';
export type PodStatus = 'Pending' | 'Submitted' | 'UnderReview' | 'Accepted' | 'Rejected' | 'ResubmissionRequired';
export type KpiType =
  | 'OnTimePickup' | 'OnTimeDelivery' | 'PlacementCompliance' | 'TenderAcceptance' | 'PodCompliance'
  | 'ClaimsRate' | 'CostPerformance' | 'Availability' | 'NoShowRate' | 'VehicleReplacementRate' | 'PodRejectionRate';
export type ClaimType = 'Damage' | 'Shortage' | 'LossTheft';
export type ComplianceOverallStatus = 'Compliant' | 'ExpiringSoon' | 'NonCompliant';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface TransporterListItem {
  id: number;
  transporterCode: string;
  legalName: string;
  tradeName: string | null;
  transporterType: string | null;
  status: TransporterStatus;
  city: string | null;
  state: string | null;
  primaryContactName: string | null;
  primaryContactPhone: string | null;
  activeVehicles: number;
  activeLanes: number;
}

export interface ContactDto {
  id: number;
  name: string;
  designation: string | null;
  email: string | null;
  phone: string | null;
  contactType: string;
  isPrimary: boolean;
  isActive: boolean;
}

export interface CapabilityDto {
  id: number;
  capabilityTypeId: number;
  code: string;
  name: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: RecordStatus;
}

export interface TransporterDetail {
  id: number;
  transporterCode: string;
  legalName: string;
  tradeName: string | null;
  transporterType: string | null;
  companyType: string | null;
  pan: string | null;
  gstin: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  country: string | null;
  primaryContactName: string | null;
  primaryContactEmail: string | null;
  primaryContactPhone: string | null;
  website: string | null;
  status: TransporterStatus;
  activeVehicles: number;
  activeLanes: number;
  contacts: ContactDto[];
  capabilities: CapabilityDto[];
  createdAt: string;
  updatedAt: string;
}

export interface VehicleDto {
  id: number;
  transporterId: number;
  registrationNumber: string;
  vehicleTypeReference: number;
  payloadCapacityKg: number;
  availabilityStatus: string;
  status: RecordStatus;
}

export interface LaneDto {
  id: number;
  transporterId: number;
  originLocationReference: number;
  destinationLocationReference: number;
  serviceType: string;
  vehicleTypeReference: number | null;
  transitSlaMinutes: number | null;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: RecordStatus;
}

export interface DriverDto {
  id: number;
  transporterId: number;
  fullName: string;
  mobile: string;
  licenceNumber: string;
  status: RecordStatus;
}

export interface DocumentDto {
  id: number;
  documentTypeCode: string;
  documentTypeName: string;
  documentNumber: string | null;
  expiryDate: string | null;
  verificationStatus: 'Pending' | 'Verified' | 'Rejected';
  driverId: number | null;
  vehicleId: number | null;
}

export interface ComplianceItemDto {
  scope: 'Transporter' | 'Vehicle' | 'Driver';
  vehicleId: number | null;
  driverId: number | null;
  documentTypeName: string;
  state: string;
  daysToExpiry: number | null;
  blocksApproval: boolean;
  blocksAllocation: boolean;
  message: string;
}

export interface ComplianceReport {
  transporterId: number;
  asOf: string;
  overall: ComplianceOverallStatus;
  approvalBlocked: boolean;
  allocationBlocked: boolean;
  items: ComplianceItemDto[];
}

export interface KpiRow {
  kpi: KpiType;
  numerator: number;
  denominator: number;
  value: number | null;
}

export interface OperationalPeriod {
  periodStart: string;
  periodEnd: string;
  kpis: KpiRow[];
  metrics: {
    pendingPod: number;
    averagePlacementDelayMinutes: number | null;
    latePickupsCarrier: number;
    lateDeliveriesCarrier: number;
  };
}

export interface ScorecardKpi {
  kpi: KpiType;
  kpiValue: number | null;
  weight: number;
  weightedScore: number | null;
  numerator: number;
  denominator: number;
}

export interface Scorecard {
  id: number;
  transporterId: number;
  periodStart: string;
  periodEnd: string;
  overallScore: number | null;
  status: string;
  generatedAt: string;
  calculationVersion: number;
  kpis: ScorecardKpi[];
}

export interface ClaimRecord {
  id: number;
  transporterId: number;
  loadReference: string | null;
  claimType: ClaimType;
  claimDate: string;
  claimValue: number;
  status: 'Open' | 'Resolved';
  remarks: string | null;
  resolvedAt: string | null;
}

export interface CapacityDay {
  id: number;
  transporterId: number;
  date: string;
  vehiclesCommitted: number;
  vehiclesAvailable: number;
  updatedAt: string;
}

export interface RankedKpi {
  kpi: KpiType;
  value: number | null;
  numerator: number;
  denominator: number;
}

export interface RankedTransporter {
  rank: number | null;
  transporterId: number;
  transporterCode: string;
  transporterName: string;
  region: string | null;
  status: TransporterStatus;
  metricValue: number | null;
  overallScore: number | null;
  ranked: boolean;
  note: string | null;
  kpis: RankedKpi[];
}

export interface RankingResult {
  metric: string;
  scopeLabel: string;
  from: string;
  to: string;
  rows: RankedTransporter[];
}

export interface BenchmarkRow {
  kpi: KpiType;
  transporter: number | null;
  laneAverage: number | null;
  regionAverage: number | null;
  categoryAverage: number | null;
  topPerformer: number | null;
  topPerformerTransporterId: number | null;
  gapToTop: number | null;
  gapToLaneAverage: number | null;
}

export interface Benchmark {
  transporterId: number;
  scopeLabel: string;
  from: string;
  to: string;
  rows: BenchmarkRow[];
}

export interface AlertDto {
  id: number;
  alertType: string;
  severity: Severity;
  transporterId: number;
  loadReference: string | null;
  message: string;
  createdAt: string;
  dueAt: string | null;
  status: AlertStatus;
}

export interface TenderInvitation {
  id: number;
  tenderNumber: string;
  tenderType: TenderType;
  sequenceNumber: number | null;
  transporterId: number;
  transporterName: string;
  status: TenderStatus;
  loadReference: string;
  serviceType: string;
  weightKg: number;
  offeredRate: number | null;
  currency: string;
  pickupDateTime: string;
  deliveryDateTime: string;
  responseDeadline: string;
  sentAt: string | null;
}

export interface TenderDetail {
  invitation: TenderInvitation;
  responses: { id: number; response: string; responseAt: string; quotedRate: number | null; reason: string | null; comments: string | null }[];
  events: { id: number; eventType: string; eventAt: string; performedBy: string; comments: string | null }[];
}

export interface PlacementDto {
  id: number;
  loadReference: string;
  transporterId: number;
  vehicleRegistration: string | null;
  requiredPlacementAt: string;
  placedAt: string | null;
  status: PlacementStatus;
  slaStatus: string;
  placementDelayMinutes: number | null;
  replacementCount: number;
}

export interface PodDto {
  id: number;
  loadReference: string;
  transporterId: number;
  deliveredAt: string;
  dueAt: string;
  submittedAt: string | null;
  submittedWithinSla: boolean | null;
  status: PodStatus;
  rejectionReason: string | null;
}

export interface VendorDashboard {
  newTenders: number;
  pendingAcceptance: number;
  acceptedLoads: number;
  upcomingPlacements: number;
  todaysPickups: number;
  todaysDeliveries: number;
  pendingPod: number | null;
  openExceptions: number;
}

export interface VendorLoad {
  invitationId: number;
  tenderNumber: string;
  loadReference: string;
  status: TenderStatus;
  loadStatus: string;
  serviceType: string;
  weightKg: number;
  pickupDateTime: string;
  deliveryDateTime: string;
  vehicleRegistration: string | null;
  driverName: string | null;
  placementStatus: string | null;
  executionStatus: string | null;
  podStatus: string | null;
}
