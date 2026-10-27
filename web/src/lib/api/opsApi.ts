// Transporter operations: delivery requests, LRs and trips, the driver's QR page, POD, freight and detention billing, and invoices.
// Types live beside the calls (one self-contained file) so this module can be merged without touching the shared type lists.
import { http } from './client'
import type { PagedResult } from './types'

const ops = '/api/v1/ops'
const base = () => import.meta.env.VITE_API_URL ?? ''

export type ActiveStatus = 'Active' | 'Inactive'
export type LocationKind = 'Cfa' | 'Distributor'
export type RequestStatus = 'Received' | 'UnderReview' | 'Incomplete' | 'Validated' | 'ReadyForDispatch' | 'Converted' | 'Cancelled'
export type DeliveryStatus = 'Created' | 'Assigned' | 'AtCfa' | 'Loading' | 'Loaded' | 'InTransit' | 'Arrived' | 'UnloadingCompleted' | 'Cancelled'
export type TripStatus = 'Planned' | 'AtCfa' | 'Loading' | 'Loaded' | 'InTransit' | 'Completed' | 'Cancelled'
export type EventType = 'CfaReached' | 'LoadingStarted' | 'LoadingCompleted' | 'CfaDeparted' | 'DestinationArrival' | 'UnloadingCompleted' | 'PodUploaded' | 'PodPending' | 'ArrivalRejected' | 'ArrivalOverridden' | 'Correction'
export type PodStatus = 'NotYetDue' | 'Pending' | 'Received' | 'UnderReview' | 'Accepted' | 'Rejected'
export type CalculationStatus = 'Calculated' | 'OnHold' | 'Reviewed' | 'Invoiced' | 'Superseded'
export type InvoiceStatus = 'Draft' | 'PendingApproval' | 'Issued' | 'Cancelled'
export type SubmissionStatus = 'NotSubmitted' | 'Submitted' | 'Acknowledged' | 'Rejected' | 'ResubmissionRequired'
export type PaymentState = 'Unpaid' | 'PartiallyPaid' | 'Paid'
export type AgreementStatus = 'Draft' | 'Active' | 'Superseded' | 'Cancelled'
export type WeightBasis = 'Actual' | 'Volumetric' | 'HigherOfActualAndVolumetric'
export type RateMethod = 'PerKg' | 'PerTonne' | 'FlatPerConsignment' | 'WeightSlabs'
export type FreeTimeKind = 'Duration' | 'EndOfDay'
export type DetentionUnit = 'Hour' | 'Day'
export type PartUnitRounding = 'Exact' | 'RoundUp' | 'RoundDown' | 'Nearest'
export type TaxMode = 'None' | 'Forward' | 'ReverseCharge'

export interface CustomerDto {
  id: string; code: string; name: string; gstin: string | null; billingLine1: string | null; billingCity: string | null; billingState: string | null; billingPincode: string | null
  billingContactName: string | null; billingContactEmail: string | null; billingContactPhone: string | null; deliveryContactName: string | null; deliveryContactPhone: string | null
  commercialContactName: string | null; commercialContactEmail: string | null; dueDays: number; billingRequirements: string | null; portalMethod: string | null; portalReference: string | null
  requiredRequestFields: string | null; status: ActiveStatus; version: number
}
export type SaveCustomerRequest = Omit<CustomerDto, 'id'>

export interface LocationDto {
  id: string; code: string; name: string; kind: LocationKind; customerId: string | null; customerName: string | null; line1: string; city: string; state: string; pincode: string | null
  latitude: number | null; longitude: number | null; geofenceRadiusM: number; accessNotes: string | null; status: ActiveStatus; version: number; sacCode?: string | null
}
export type SaveLocationRequest = Omit<LocationDto, 'id' | 'customerName'>

export interface WeightSlab { fromKg: number; toKg: number | null; ratePerKg: number }
export interface ExtraCharge { code: string; name: string; amount: number }
export interface DetentionPolicy {
  applicable: boolean; freeTime: FreeTimeKind; freeAmount: number; freeUnit: DetentionUnit; chargeUnit: DetentionUnit; rate: number; rounding: PartUnitRounding; maxCharge: number | null; note: string | null
}
export interface TaxSettings { mode: TaxMode; ratePercent: number; sac: string | null; note: string | null }
export interface BillingRules { requireAcceptedPod: boolean; allowBillingWithoutPod: boolean; requiredDocuments: string | null }
export interface RateDto {
  id?: string | null; originLocationId: string | null; originCity: string | null; originState: string | null; destinationLocationId: string | null; destinationCity: string | null
  destinationState: string | null; material: string | null; method: RateMethod; rate: number; slabs: WeightSlab[]; minimumCharge: number | null
}
export interface AgreementSummaryDto { id: string; number: string; revision: number; customerId: string; customerName: string; title: string; effectiveFrom: string; effectiveTo: string | null; status: AgreementStatus; rateCount: number }
export interface AgreementDto {
  id: string; number: string; revision: number; customerId: string; customerName: string; title: string; effectiveFrom: string; effectiveTo: string | null; status: AgreementStatus
  weightBasis: WeightBasis; volumetricKgPerCbm: number | null; minimumFreight: number | null; detention: DetentionPolicy; tax: TaxSettings; billing: BillingRules; extraCharges: ExtraCharge[]
  rates: RateDto[]; activatedAt: string | null; version: number
}
export interface SaveAgreementRequest {
  customerId: string; title: string; effectiveFrom: string; effectiveTo: string | null; weightBasis: WeightBasis; volumetricKgPerCbm: number | null; minimumFreight: number | null
  detention: DetentionPolicy; tax: TaxSettings; billing: BillingRules; extraCharges: ExtraCharge[]; rates: RateDto[]; version?: number
}

export interface MaterialLineDto { materialCode: string | null; materialName: string; quantity: number; unit: string; weightKg: number; volumeCbm: number | null; dimensions: string | null; packages: number; remarks?: string | null; volumeCuFt?: number | null; netWeightKg?: number | null; invoiceNo?: string | null }
export interface FileDto { id: string; fileName: string; contentType: string; sizeBytes: number; uploadedAt: string }
export interface RequestSummaryDto { id: string; number: string; customerId: string; customerName: string; customerReference: string | null; receivedAt: string; cfaName: string; destinationName: string; requestedDate: string | null; priority: string; status: RequestStatus; totalWeightKg: number; lrId: string | null }
export interface RequestDto {
  id: string; number: string; customerId: string; customerName: string; customerReference: string | null; receivedAt: string; emailReference: string | null; cfaLocationId: string | null; cfaName: string | null
  destinationLocationId: string | null; destinationName: string | null; requestedDate: string | null; priority: string; vehicleTypeRequested: string | null; instructions: string | null; remarks: string | null
  status: RequestStatus; issues: string[]; lrId: string | null; lines: MaterialLineDto[]; files: FileDto[]; version: number
}
export interface SaveRequestRequest {
  customerId: string; customerReference: string | null; receivedAt: string | null; emailReference: string | null; cfaLocationId: string | null; destinationLocationId: string | null; requestedDate: string | null
  priority: string | null; vehicleTypeRequested: string | null; instructions: string | null; remarks: string | null; lines: MaterialLineDto[]; version?: number
}

export interface EventDto { id: string; type: EventType; actualAt: string; recordedAt: string; source: 'Office' | 'QrWeb' | 'System'; userId: string | null; remarks: string | null; verification: string | null; distanceM: number | null; accuracyM: number | null; radiusM: number | null; correctsEventId: string | null; tripId: string | null }
export type MovementType = 'Supply' | 'Return' | 'CrossBilling'
export type CrossBillingMode = 'Lumpsum' | 'PerKg'
export interface CrossBillingTerms { mode: CrossBillingMode; amount?: number | null; kg?: number | null; ratePerKg?: number | null }
export interface LrSummaryDto {
  id: string; number: string; customerId: string; customerName: string; customerReference: string | null; cfaName: string; destinationName: string; destinationCity: string; tripId: string | null; tripNumber: string | null
  vehicleRegistration: string | null; status: DeliveryStatus; podStatus: PodStatus; deliveryDate: string | null; totalWeightKg: number; totalPackages: number; cfaReachedAt: string | null; loadingStartedAt: string | null
  loadingCompletedAt: string | null; departedAt: string | null; arrivedAt: string | null; unloadedAt: string | null; qrActive: boolean; createdAt: string; movement?: MovementType
}
export interface LrDetailDto { lr: LrSummaryDto; instructions: string | null; qrExpiresAt: string | null; printCount: number; lines: MaterialLineDto[]; events: EventDto[]; requestId: string | null; version: number; indentNo: string | null; files: FileDto[]; crossBilling?: (CrossBillingTerms & { freight: number | null }) | null }
export interface CreatedLrDto { lr: LrDetailDto; qrPath: string }
export interface CreateLrRequest { requestId?: string | null; customerId?: string | null; customerReference?: string | null; cfaLocationId?: string | null; destinationLocationId?: string | null; deliveryDate?: string | null; instructions?: string | null; indentNo?: string | null; lines?: LrLineInput[] | null; movement?: MovementType; crossBilling?: CrossBillingTerms | null }
/** One LR line as the office enters it. The material is optional; volume is in cubic feet. */
export interface LrLineInput { materialCode?: string | null; materialName?: string | null; quantity: number; unit: string; weightKg: number; invoiceNo?: string | null; volumeCbm?: number | null; dimensions?: string | null; packages: number }
export interface UomDto { id: string | null; code: string; name: string; isActive: boolean; isDefault: boolean }
export interface LrPrintDto {
  id: string; number: string; customerName: string; customerReference: string | null; cfaName: string; cfaAddress: string; destinationName: string; destinationAddress: string; vehicleRegistration: string | null
  driverName: string | null; driverPhone: string | null; deliveryDate: string | null; instructions: string | null; lines: MaterialLineDto[]; totalWeightKg: number; totalPackages: number; qrPath: string; printCount: number; tripNumber: string | null; indentNo: string | null; createdAt?: string | null
}

export interface TripSummaryDto {
  id: string; number: string; cfaName: string; vehicleRegistration: string; driverName: string | null; driverPhone: string | null; plannedDate: string | null; status: TripStatus; stopCount: number; lrCount: number
  cfaReachedAt: string | null; loadingStartedAt: string | null; loadingCompletedAt: string | null; departedAt: string | null; loadingMinutes: number | null; cfaTurnaroundMinutes: number | null
}
export interface TripStopDto { id: string; sequence: number; destinationLocationId: string; destinationName: string; destinationCity: string; arrivedAt: string | null; unloadedAt: string | null; lrs: LrSummaryDto[] }
export interface TripDetailDto { trip: TripSummaryDto; cfaLocationId: string; vehicleId: string | null; transporterId: string | null; remarks: string | null; stops: TripStopDto[]; events: EventDto[]; version: number }
export interface CreateTripRequest { cfaLocationId: string; vehicleId: string | null; vehicleRegistration: string | null; transporterId: string | null; driverName: string | null; driverPhone: string | null; plannedDate: string | null; remarks: string | null; lrIds: string[] }
export interface VehicleOptionDto { id: string; transporterId: string; registrationNumber: string; vehicleTypeName: string; payloadKg: number; available: boolean; note: string | null }

export interface PodDocumentDto { id: string; submission: number; fileName: string; contentType: string; sizeBytes: number; source: 'QrWeb' | 'Office'; uploadedAt: string; uploadedBy: string | null; remarks: string | null }
export interface PodReviewDto { action: string; reviewerId: string | null; at: string; reason: string | null }
export interface PodDto {
  id: string; lrId: string; lrNumber: string; status: PodStatus; submission: number; pendingAt: string | null; receivedAt: string | null; reviewerId: string | null; reviewedAt: string | null; rejectionReason: string | null
  documents: PodDocumentDto[]; reviews: PodReviewDto[]; deliveryStatus: DeliveryStatus; arrivedAt: string | null; unloadedAt: string | null; version: number
}
export interface PodQueueItemDto { podId: string; lrId: string; lrNumber: string; customerName: string; destinationName: string; vehicleRegistration: string | null; status: PodStatus; arrivedAt: string | null; unloadedAt: string | null; receivedAt: string | null; ageDays: number; submission: number; files: number }

export interface CalcLineDto { code: string; description: string; amount: number; basis: string }
export interface DetentionDto {
  status: 'NotApplicable' | 'Calculated' | 'OnHold'; holdReason: string | null; arrival: string | null; unloading: string | null; elapsedHours: number; freeUntil: string | null; chargeableHours: number; units: number
  chargeUnit: string | null; rate: number; rounding: string | null; cap: number | null; capApplied: boolean; amount: number
}
export interface CalculationDto {
  id: string; lrId: string; lrNumber: string; customerId: string; version: number; status: CalculationStatus; agreementId: string | null; agreementRevision: number | null; billableWeightKg: number; weightBasisUsed: string | null
  basicFreight: number; otherCharges: number; detentionAmount: number; detentionOverride: number | null; detentionOverrideReason: string | null; totalPreTax: number; holds: string[]; lines: CalcLineDto[]; detention: DetentionDto | null
  trace: string[]; podAccepted: boolean; billedWithoutPod: boolean; calculatedAt: string; reviewedBy: string | null; reviewedAt: string | null; reviewNote: string | null; invoiceId: string | null
}
export interface BillingRowDto {
  lrId: string; lrNumber: string; customerId: string; customerName: string; vehicleRegistration: string | null; destinationName: string; arrivedAt: string | null; unloadedAt: string | null; podStatus: PodStatus
  billableWeightKg: number | null; basicFreight: number | null; detention: number | null; otherCharges: number | null; totalPreTax: number | null; calcId: string | null; calcStatus: CalculationStatus | null; calcVersion: number | null
  holds: string[]; eligible: boolean; invoiceId: string | null; invoiceNumber: string | null
}
export interface CalculateResultDto { calculations: CalculationDto[]; failures: { lrId: string; code: string; message: string }[] }

export interface InvoiceSummaryDto { id: string; number: string; customerId: string; customerName: string; invoiceDate: string; dueDate: string; status: InvoiceStatus; submission: SubmissionStatus; paymentState: PaymentState; subtotal: number; tax: number; grandTotal: number; amountPaid: number; outstanding: number; lineCount: number; daysOverdue: number; tdsDeducted?: number }
export interface InvoiceLineDto { id: string; lineNumber: number; lrId: string; lrNumber: string; freightCalculationId: string; calculationVersion: number; agreementRevision: number | null; description: string; basicFreight: number; detention: number; otherCharges: number; taxable: number; cgst: number; sgst: number; igst: number }
export interface SubmissionDto { id: string; attempt: number; status: SubmissionStatus; submittedAt: string; submittedBy: string | null; portalReference: string | null; documentReference: string | null; decidedAt: string | null; rejectionReason: string | null; notes: string | null }
export interface PaymentDto { id: string; amount: number; receivedOn: string; mode: string | null; reference: string | null; remarks: string | null; recordedAt: string; tdsAmount?: number }
export interface InvoiceDto {
  summary: InvoiceSummaryDto; customerGstin: string | null; billingAddress: string | null; customerReference: string | null; taxMode: TaxMode; taxRatePercent: number; taxNote: string | null; sac: string | null
  cgst: number; sgst: number; igst: number; notes: string | null; cancelReason: string | null; approvalRequestId: string | null; issuedAt: string | null; supplierGstin: string | null
  lines: InvoiceLineDto[]; submissions: SubmissionDto[]; payments: PaymentDto[]; version: number
}
export interface ReceivableRowDto { customerId: string; customerName: string; notDue: number; days1To30: number; days31To60: number; days61To90: number; over90: number; total: number; invoices: number }

export interface DashboardDto {
  openRequests: number; readyForDispatch: number; lrsNotOnTrip: number; tripsActive: number; lrsInTransit: number; arrivedAwaitingUnloading: number; podPending: number; podToReview: number; podRejected: number
  oldestPendingPodDays: number
  /** The billing figures are null for someone without ops.billing.read. */
  billingOnHold: number | null; readyToInvoice: number | null; invoicesToSubmit: number | null; submissionsRejected: number | null; outstanding: number | null; overdue: number | null; detentionThisMonth: number | null
}
export interface OpsSettingsDto {
  arrival: { maxAccuracyM: number; tokenValidityDays: number }
  numbering: { requestPrefix: string; lrPrefix: string; tripPrefix: string; digits: number }
  pod: { maxFiles: number; maxBytes: number }
  invoice: { seriesPrefix: string; supplierGstin: string | null; supplierStateCode: string | null; requireApproval: boolean; supplierName?: string | null; supplierAddress?: string | null; supplierState?: string | null; declaration?: string | null; logoKey?: string | null; signatureKey?: string | null }
  fleet: { operatingTransporterId: string | null }
}

// ---- the driver's QR page
export type QrAction = 'None' | 'ConfirmArrival' | 'ConfirmUnloading' | 'UploadPod'
export interface QrViewDto {
  lrNumber: string; customerName: string; vehicleRegistration: string | null; cfaName: string; destinationName: string; destinationCity: string; material: string; status: DeliveryStatus; statusText: string
  podStatus: PodStatus; arrivedAt: string | null; unloadedAt: string | null; nextAction: QrAction; message: string; podRejectionReason: string | null; maxFiles: number; maxFileMb: number; canMarkPodPending: boolean; serverTime: string
}
export interface ArrivalPayload { latitude: number | null; longitude: number | null; accuracyM: number | null; capturedAt: string | null; locationError: string | null }

export class QrError extends Error {
  status: number
  code: string
  constructor(status: number, code: string, message: string) { super(message); this.status = status; this.code = code }
}

async function qrCall(path: string, init?: RequestInit): Promise<QrViewDto> {
  const r = await fetch(`${base()}/api/v1/public/ops/lr/${path}`, { ...init, headers: { Accept: 'application/json', ...(init?.headers ?? {}) } })
  if (r.ok) return (await r.json()) as QrViewDto
  let body: { code?: string; detail?: string } = {}
  try { body = await r.json() } catch { /* not JSON */ }
  throw new QrError(r.status, body.code ?? 'error', r.status === 404 ? 'This code is not valid. Please ask the office for a new LR.' : (body.detail ?? 'Something went wrong. Please try again.'))
}

const json = (body: unknown): RequestInit => ({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
const key = () => (typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : String(Date.now()) + Math.random())

/** The driver's page. Anonymous, so it goes around the signed-in client: possession of the code is the permission. */
export const qrApi = {
  view: (token: string) => qrCall(encodeURIComponent(token)),
  /** The only call that carries a location. */
  arrive: (token: string, p: ArrivalPayload, clientKey = key()) => qrCall(`${encodeURIComponent(token)}/arrival`, { ...json({ ...p, clientKey }), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': clientKey } }),
  unload: (token: string, clientKey = key()) => qrCall(`${encodeURIComponent(token)}/unloading`, { ...json({ clientKey }), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': clientKey } }),
  podPending: (token: string, clientKey = key()) => qrCall(`${encodeURIComponent(token)}/pod-pending`, { ...json({ clientKey }), headers: { 'Content-Type': 'application/json', 'Idempotency-Key': clientKey } }),
  uploadPod: (token: string, files: File[], clientKey = key()) => {
    const form = new FormData()
    files.forEach((f) => form.append('files', f, f.name))
    return qrCall(`${encodeURIComponent(token)}/pod`, { method: 'POST', body: form, headers: { 'Idempotency-Key': clientKey } })
  },
}

export interface InvoicePrintDto {
  supplierName: string; supplierAddress: string | null; supplierGstin: string | null; supplierState: string | null; hasLogo: boolean; hasSignature: boolean; declaration: string
  customerName: string; customerAddress: string | null; customerGstin: string | null; customerState: string | null
  invoiceNumber: string; invoiceDate: string; reference: string; description: string; lrNumbers: string[]; hsnSac: string | null
  taxable: number; cgst: number; sgst: number; igst: number; taxMode: TaxMode; taxRatePercent: number; taxNote: string | null; grandTotal: number; amountInWords: string; status: InvoiceStatus
}
export type BrandingKind = 'logo' | 'signature'

export interface ArrearRowDto {
  invoiceId: string; invoiceNumber: string; invoiceDate: string; customerId: string; customerName: string; paymentState: PaymentState; lrNumber: string; deliveryDate: string
  invoicedRevision: number | null; currentRevision: number; invoicedFreight: number; revisedFreight: number | null; freightDifference: number | null; taxDifference: number | null; totalDifference: number | null; note: string | null
}
export interface ArrearCustomerDto { customerId: string; customerName: string; invoices: number; lines: number; freightDifference: number; taxDifference: number; totalDifference: number }
export interface ArrearsReportDto { rows: ArrearRowDto[]; customers: ArrearCustomerDto[]; freightDifference: number; taxDifference: number; totalDifference: number; notPriced: number }

const page = (p: object) => ({ params: p })

export const opsApi = {
  dashboard: () => http.get<DashboardDto>(`${ops}/dashboard`).then((r) => r.data),

  customers: (params: { search?: string; status?: ActiveStatus; page?: number; pageSize?: number }) => http.get<PagedResult<CustomerDto>>(`${ops}/customers`, page(params)).then((r) => r.data),
  createCustomer: (b: SaveCustomerRequest) => http.post<CustomerDto>(`${ops}/customers`, b).then((r) => r.data),
  updateCustomer: (id: string, b: SaveCustomerRequest) => http.put<CustomerDto>(`${ops}/customers/${id}`, b).then((r) => r.data),

  locations: (params: { search?: string; kind?: LocationKind; customerId?: string; status?: ActiveStatus; page?: number; pageSize?: number }) => http.get<PagedResult<LocationDto>>(`${ops}/locations`, page(params)).then((r) => r.data),
  createLocation: (b: SaveLocationRequest) => http.post<LocationDto>(`${ops}/locations`, b).then((r) => r.data),
  updateLocation: (id: string, b: SaveLocationRequest) => http.put<LocationDto>(`${ops}/locations/${id}`, b).then((r) => r.data),

  agreements: (params: { customerId?: string; status?: AgreementStatus }) => http.get<AgreementSummaryDto[]>(`${ops}/agreements`, page(params)).then((r) => r.data),
  agreement: (id: string) => http.get<AgreementDto>(`${ops}/agreements/${id}`).then((r) => r.data),
  createAgreement: (b: SaveAgreementRequest) => http.post<AgreementDto>(`${ops}/agreements`, b).then((r) => r.data),
  updateAgreement: (id: string, b: SaveAgreementRequest) => http.put<AgreementDto>(`${ops}/agreements/${id}`, b).then((r) => r.data),
  activateAgreement: (id: string) => http.post<AgreementDto>(`${ops}/agreements/${id}/activate`).then((r) => r.data),
  reviseAgreement: (id: string, effectiveFrom: string) => http.post<AgreementDto>(`${ops}/agreements/${id}/revise`, { effectiveFrom }).then((r) => r.data),
  cancelAgreement: (id: string) => http.delete(`${ops}/agreements/${id}`).then(() => undefined),

  requests: (params: { search?: string; status?: RequestStatus; customerId?: string; page?: number; pageSize?: number }) => http.get<PagedResult<RequestSummaryDto>>(`${ops}/requests`, page(params)).then((r) => r.data),
  request: (id: string) => http.get<RequestDto>(`${ops}/requests/${id}`).then((r) => r.data),
  createRequest: (b: SaveRequestRequest) => http.post<RequestDto>(`${ops}/requests`, b).then((r) => r.data),
  updateRequest: (id: string, b: SaveRequestRequest) => http.put<RequestDto>(`${ops}/requests/${id}`, b).then((r) => r.data),
  requestAction: (id: string, action: 'review' | 'validate' | 'ready' | 'cancel') => http.post<RequestDto>(`${ops}/requests/${id}/${action}`).then((r) => r.data),
  attachRequestFile: (id: string, file: File) => { const f = new FormData(); f.append('files', file, file.name); return http.post<FileDto>(`${ops}/requests/${id}/files`, f).then((r) => r.data) },
  requestFile: (fileId: string) => http.get<Blob>(`${ops}/request-files/${fileId}`, { responseType: 'blob' }).then((r) => r.data),

  lrs: (params: { search?: string; status?: DeliveryStatus; podStatus?: PodStatus; customerId?: string; tripId?: string; from?: string; to?: string; page?: number; pageSize?: number }) => http.get<PagedResult<LrSummaryDto>>(`${ops}/lrs`, page(params)).then((r) => r.data),
  lr: (id: string) => http.get<LrDetailDto>(`${ops}/lrs/${id}`).then((r) => r.data),
  attachLrFile: (id: string, file: File) => { const f = new FormData(); f.append('files', file, file.name); return http.post<FileDto>(`${ops}/lrs/${id}/files`, f).then((r) => r.data) },
  uoms: (activeOnly = true) => http.get<UomDto[]>(`${ops}/uoms`, { params: { activeOnly } }).then((r) => r.data),
  createUom: (b: { code: string; name: string; isActive: boolean }) => http.post<UomDto>(`${ops}/uoms`, b).then((r) => r.data),
  updateUom: (id: string, b: { code: string; name: string; isActive: boolean }) => http.put<UomDto>(`${ops}/uoms/${id}`, b).then((r) => r.data),
  createLr: (b: CreateLrRequest) => http.post<CreatedLrDto>(`${ops}/lrs`, b).then((r) => r.data),
  printLr: (id: string) => http.get<LrPrintDto>(`${ops}/lrs/${id}/print`).then((r) => r.data),
  reissueQr: (id: string) => http.post<CreatedLrDto>(`${ops}/lrs/${id}/qr`).then((r) => r.data),
  revokeQr: (id: string) => http.delete<LrDetailDto>(`${ops}/lrs/${id}/qr`).then((r) => r.data),
  deleteLr: (id: string) => http.delete(`${ops}/lrs/${id}`).then(() => undefined),
  cancelLr: (id: string) => http.post<LrDetailDto>(`${ops}/lrs/${id}/cancel`).then((r) => r.data),
  destinationEvent: (id: string, b: { type: EventType; actualAt: string | null; reason: string }) => http.post<LrDetailDto>(`${ops}/lrs/${id}/destination-events`, b).then((r) => r.data),
  correctDestinationEvent: (id: string, b: { type: EventType; newActualAt: string; reason: string }) => http.post<LrDetailDto>(`${ops}/lrs/${id}/destination-events/correct`, b).then((r) => r.data),

  trips: (params: { search?: string; status?: TripStatus; page?: number; pageSize?: number }) => http.get<PagedResult<TripSummaryDto>>(`${ops}/trips`, page(params)).then((r) => r.data),
  trip: (id: string) => http.get<TripDetailDto>(`${ops}/trips/${id}`).then((r) => r.data),
  createTrip: (b: CreateTripRequest) => http.post<TripDetailDto>(`${ops}/trips`, b).then((r) => r.data),
  tripEvent: (id: string, b: { type: EventType; actualAt: string | null; remarks: string | null }) => http.post<TripDetailDto>(`${ops}/trips/${id}/events`, { ...b, clientKey: key() }).then((r) => r.data),
  correctTripEvent: (id: string, b: { type: EventType; newActualAt: string; reason: string }) => http.post<TripDetailDto>(`${ops}/trips/${id}/events/correct`, b).then((r) => r.data),
  cancelTrip: (id: string) => http.post<TripDetailDto>(`${ops}/trips/${id}/cancel`).then((r) => r.data),
  vehicles: () => http.get<VehicleOptionDto[]>(`${ops}/vehicles`).then((r) => r.data),

  pods: (params: { view?: string; search?: string; customerId?: string; page?: number; pageSize?: number }) => http.get<PagedResult<PodQueueItemDto>>(`${ops}/pods`, page(params)).then((r) => r.data),
  pod: (lrId: string) => http.get<PodDto>(`${ops}/lrs/${lrId}/pod`).then((r) => r.data),
  uploadPod: (lrId: string, files: File[], remarks?: string) => { const f = new FormData(); files.forEach((x) => f.append('files', x, x.name)); if (remarks) f.append('remarks', remarks); return http.post<PodDto>(`${ops}/lrs/${lrId}/pod`, f, { headers: { 'Idempotency-Key': key() } }).then((r) => r.data) },
  podAction: (lrId: string, action: 'start-review' | 'accept' | 'reject' | 'reopen', reason?: string) => http.post<PodDto>(`${ops}/lrs/${lrId}/pod/${action}`, { reason: reason ?? null }).then((r) => r.data),
  podFile: (documentId: string) => http.get<Blob>(`${ops}/pod-documents/${documentId}/file`, { responseType: 'blob' }).then((r) => r.data),

  invoicePrint: (id: string) => http.get<InvoicePrintDto>(`${ops}/invoices/${id}/print`).then((r) => r.data),
  brandingImage: (kind: BrandingKind) => http.get<Blob>(`${ops}/invoice-branding/${kind}`, { responseType: 'blob' }).then((r) => r.data),
  uploadBranding: (kind: BrandingKind, file: File) => { const f = new FormData(); f.append('files', file, file.name); return http.post(`${ops}/invoice-branding/${kind}`, f).then(() => undefined) },
  removeBranding: (kind: BrandingKind) => http.delete(`${ops}/invoice-branding/${kind}`).then(() => undefined),
  arrears: (params: { customerId?: string; from?: string; to?: string; includeUnchanged?: boolean }) => http.get<ArrearsReportDto>(`${ops}/billing/arrears`, page(params)).then((r) => r.data),
  billing: (params: { customerId?: string; from?: string; to?: string; search?: string; podStatus?: PodStatus; calcStatus?: CalculationStatus; invoiced?: boolean; page?: number; pageSize?: number }) => http.get<PagedResult<BillingRowDto>>(`${ops}/billing`, page(params)).then((r) => r.data),
  calculate: (lrIds: string[]) => http.post<CalculateResultDto>(`${ops}/billing/calculate`, { lrIds }).then((r) => r.data),
  calculation: (id: string) => http.get<CalculationDto>(`${ops}/billing/calculations/${id}`).then((r) => r.data),
  lrCalculations: (lrId: string) => http.get<CalculationDto[]>(`${ops}/lrs/${lrId}/calculations`).then((r) => r.data),
  reviewCalculation: (id: string, note?: string) => http.post<CalculationDto>(`${ops}/billing/calculations/${id}/review`, { note: note ?? null }).then((r) => r.data),
  resolveHold: (id: string, detentionAmount: number, reason: string) => http.post<CalculationDto>(`${ops}/billing/calculations/${id}/resolve-hold`, { detentionAmount, reason }).then((r) => r.data),

  invoices: (params: { search?: string; customerId?: string; status?: InvoiceStatus; submission?: SubmissionStatus; payment?: PaymentState; from?: string; to?: string; page?: number; pageSize?: number }) => http.get<PagedResult<InvoiceSummaryDto>>(`${ops}/invoices`, page(params)).then((r) => r.data),
  invoice: (id: string) => http.get<InvoiceDto>(`${ops}/invoices/${id}`).then((r) => r.data),
  createInvoice: (b: { calculationIds: string[]; invoiceDate?: string | null; customerReference?: string | null; notes?: string | null }) => http.post<InvoiceDto>(`${ops}/invoices`, b).then((r) => r.data),
  cancelInvoice: (id: string, reason: string) => http.post<InvoiceDto>(`${ops}/invoices/${id}/cancel`, { reason }).then((r) => r.data),
  submitInvoice: (id: string, b: { portalReference: string | null; documentReference: string | null; notes: string | null }) => http.post<InvoiceDto>(`${ops}/invoices/${id}/submit`, b).then((r) => r.data),
  acknowledgeInvoice: (id: string, portalReference: string | null) => http.post<InvoiceDto>(`${ops}/invoices/${id}/acknowledge`, { portalReference }).then((r) => r.data),
  rejectSubmission: (id: string, reason: string) => http.post<InvoiceDto>(`${ops}/invoices/${id}/reject-submission`, { reason }).then((r) => r.data),
  recordPayment: (id: string, b: { amount: number; receivedOn: string; mode: string | null; reference: string | null; remarks: string | null; tdsAmount?: number }) => http.post<InvoiceDto>(`${ops}/invoices/${id}/payments`, b).then((r) => r.data),
  receivables: () => http.get<ReceivableRowDto[]>(`${ops}/receivables`).then((r) => r.data),

  settings: () => http.get<OpsSettingsDto>(`${ops}/settings`).then((r) => r.data),
  saveSettings: (b: OpsSettingsDto) => http.put<OpsSettingsDto>(`${ops}/settings`, b).then((r) => r.data),
}
