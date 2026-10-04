import type { KpiType, TransporterStatus } from '../types';

export const KPI_LABELS: Record<KpiType, string> = {
  OnTimePickup: 'On-time pickup (OTP)',
  OnTimeDelivery: 'On-time delivery (OTD)',
  PlacementCompliance: 'Placement compliance',
  TenderAcceptance: 'Tender acceptance',
  PodCompliance: 'POD compliance',
  ClaimsRate: 'Claims rate',
  CostPerformance: 'Cost performance',
  Availability: 'Availability',
  NoShowRate: 'No-show rate',
  VehicleReplacementRate: 'Vehicle replacement rate',
  PodRejectionRate: 'POD rejection rate',
};

/** KPIs that a scorecard and a benchmark show, in display order. */
export const SCORED_KPIS: KpiType[] = [
  'OnTimePickup', 'OnTimeDelivery', 'PlacementCompliance', 'TenderAcceptance',
  'PodCompliance', 'ClaimsRate', 'CostPerformance', 'Availability',
];

/** Claims rate is better when lower. Used to label the direction of a gap. */
export const LOWER_IS_BETTER: KpiType[] = ['ClaimsRate'];

export const RANKING_METRICS: { value: string; label: string }[] = [
  { value: 'OverallScore', label: 'Overall score' },
  { value: 'OnTimePickup', label: 'On-time pickup' },
  { value: 'OnTimeDelivery', label: 'On-time delivery' },
  { value: 'PlacementCompliance', label: 'Placement' },
  { value: 'PodCompliance', label: 'POD compliance' },
  { value: 'ClaimsRate', label: 'Claims (lower is better)' },
  { value: 'CostPerformance', label: 'Cost performance' },
  { value: 'TenderAcceptance', label: 'Tender acceptance' },
  { value: 'Availability', label: 'Availability' },
];

export const TRANSPORTER_STATUSES: TransporterStatus[] = ['Draft', 'Submitted', 'Approved', 'Active', 'Suspended', 'Inactive', 'Blacklisted'];

export const CLAIM_TYPES = ['Damage', 'Shortage', 'LossTheft'] as const;
