import type { DelayReasonSetting, ExecutionEventType, KpiType } from '@/lib/api/types'

export const kpiLabels: Record<KpiType, string> = {
  OnTimePickup: 'On-time pickup',
  OnTimeDelivery: 'On-time delivery',
  PlacementCompliance: 'Vehicle placement',
  TenderAcceptance: 'Tender acceptance',
  PodCompliance: 'POD on time',
  ClaimsRate: 'Claims rate',
  CostPerformance: 'Cost performance',
  Availability: 'Availability',
  NoShowRate: 'No-show rate',
  VehicleReplacementRate: 'Vehicle replacements',
  PodRejectionRate: 'POD rejections',
}

/** The KPIs that make up a score, in the order they are shown. */
export const scoredKpis: KpiType[] = [
  'OnTimePickup', 'OnTimeDelivery', 'PlacementCompliance', 'TenderAcceptance', 'PodCompliance', 'ClaimsRate', 'CostPerformance', 'Availability',
]

/** The KPIs where a lower number is better. */
export const lowerIsBetter: KpiType[] = ['ClaimsRate', 'NoShowRate', 'VehicleReplacementRate', 'PodRejectionRate']

export const eventLabels: Record<ExecutionEventType, string> = {
  PickupAppointment: 'Pickup appointment',
  VehicleArrival: 'Vehicle arrived',
  LoadingStart: 'Loading started',
  LoadingComplete: 'Loading complete',
  VehicleDeparture: 'Vehicle departed',
  DeliveryArrival: 'Arrived at delivery',
  UnloadingStart: 'Unloading started',
  DeliveryComplete: 'Delivered',
}

/** The server's default delay reasons; a tenant that edits them sees its own list where it is allowed to read settings. */
export const defaultDelayReasons: DelayReasonSetting[] = [
  { code: 'TRANSPORTER_DELAY', name: 'Transporter delay', attribution: 'Carrier' },
  { code: 'VEHICLE_BREAKDOWN', name: 'Vehicle breakdown', attribution: 'Carrier' },
  { code: 'DOCUMENTATION_ISSUE', name: 'Documentation issue', attribution: 'Carrier' },
  { code: 'CUSTOMER_DELAY', name: 'Customer delay', attribution: 'NonCarrier' },
  { code: 'WAREHOUSE_DELAY', name: 'Warehouse delay', attribution: 'NonCarrier' },
  { code: 'TRAFFIC', name: 'Traffic', attribution: 'NonCarrier' },
  { code: 'ROUTE_RESTRICTION', name: 'Route restriction', attribution: 'NonCarrier' },
  { code: 'WEATHER', name: 'Weather', attribution: 'NonCarrier' },
  { code: 'FORCE_MAJEURE', name: 'Force majeure', attribution: 'NonCarrier' },
  { code: 'OTHER', name: 'Other', attribution: 'Unattributed' },
]

export const attributionLabel = { None: 'On time', Carrier: 'Carrier', NonCarrier: 'Not the carrier', Unattributed: 'Needs a reason' } as const
