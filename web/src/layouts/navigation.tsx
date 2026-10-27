import {
  TableOutlined,
  ThunderboltOutlined,
  PlusCircleOutlined,
  SyncOutlined,
  HistoryOutlined,
  RadarChartOutlined,
  AimOutlined,
  GlobalOutlined,
  AlertOutlined,
  AuditOutlined,
  BankOutlined,
  CheckSquareOutlined,
  CalculatorOutlined,
  CarOutlined,
  ClusterOutlined,
  ContainerOutlined,
  EnvironmentOutlined,
  FileDoneOutlined,
  MobileOutlined,
  ExceptionOutlined,
  SolutionOutlined,
  ControlOutlined,
  InboxOutlined,
  SendOutlined,
  DatabaseOutlined,
  FileProtectOutlined,
  SafetyOutlined,
  DashboardOutlined,
  BarChartOutlined,
  FundOutlined,
  PartitionOutlined,
  SafetyCertificateOutlined,
  ScheduleOutlined,
  DownloadOutlined,
  LineChartOutlined,
  SettingOutlined,
  SearchOutlined,
  TeamOutlined,
  TrophyOutlined,
} from '@ant-design/icons'
import type { ReactNode } from 'react'

export interface NavItem {
  path: string
  label: string
  icon: ReactNode
  /** Shown only to users holding at least one of these permissions. Omit for items everyone sees. */
  anyPermission?: string[]
  /** Shown only when the organisation has at least one of these modules (the platform operator's own organisation always sees it). Omit for items every organisation has. */
  anyModule?: string[]
  /** Restrict to staff (`internal`) or to vendor-portal users (`vendor`). Omit for both. */
  audience?: 'internal' | 'vendor'
}

/** The modules that have documents going through approval (kept in step with the document types the modules register on the server). Without one of them there is nothing to approve. */
export const approvalModules = ['transport-ops', 'transporters', 'contracts', 'deliveries']

export interface NavGroup {
  label: string
  /** Shown on the group's header. */
  icon: ReactNode
  items: NavItem[]
  /** The module's reports, listed under the module so a person finds them where they work. */
  reports?: NavItem[]
}

const rep = (code: string, label: string, permission: string): NavItem => ({
  path: `/reports/${code}`, label, icon: <BarChartOutlined />, anyPermission: [permission], audience: 'internal',
})

/**
 * One group per business module (the keys match TmsModules on the server), each with its own pages and its own reports; then Reports & Analytics, Workflow and Administration.
 * An organisation without a module never receives that module's permissions, so its group disappears without any extra check here.
 */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    icon: <DashboardOutlined />,
    items: [{ path: '/', label: 'Dashboard', icon: <DashboardOutlined /> }],
  },
  {
    label: 'Transporter Operations',
    icon: <CarOutlined />,
    items: [
      { path: '/ops', label: 'Operations dashboard', icon: <DashboardOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/lrs', label: 'Dispatch & LRs', icon: <FileDoneOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/trips', label: 'Trips & timeline', icon: <CarOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/pod', label: 'POD management', icon: <SolutionOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/billing', label: 'Freight & detention billing', icon: <CalculatorOutlined />, anyPermission: ['ops.billing.read'], audience: 'internal' },
      { path: '/ops/billing/arrears', label: 'Rate revision arrears', icon: <CalculatorOutlined />, anyPermission: ['ops.billing.read'], audience: 'internal' },
      { path: '/ops/invoices', label: 'Invoices', icon: <FileProtectOutlined />, anyPermission: ['ops.billing.read'], audience: 'internal' },
      { path: '/ops/agreements', label: 'Customer commercial terms', icon: <AuditOutlined />, anyPermission: ['ops.billing.read'], audience: 'internal' },
      { path: '/ops/customers', label: 'Customers', icon: <BankOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/locations', label: 'CFAs & distributors', icon: <EnvironmentOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/uom', label: 'Units of measure', icon: <ControlOutlined />, anyPermission: ['ops.read'], audience: 'internal' },
      { path: '/ops/settings', label: 'Operations settings', icon: <ControlOutlined />, anyPermission: ['ops.configure'], audience: 'internal' },
    ],
  },
  {
    label: 'Transporter management',
    icon: <TrophyOutlined />,
    items: [
      { path: '/transporters', label: 'Transporters', icon: <CarOutlined />, anyPermission: ['transporters.read', 'transporters.manage'], audience: 'internal' },
      { path: '/transporters/vehicle-types', label: 'Vehicle types', icon: <CarOutlined />, anyPermission: ['transporters.read', 'transporters.manage'], audience: 'internal' },
      { path: '/transporters/setup', label: 'Transporter setup', icon: <DatabaseOutlined />, anyPermission: ['transporters.manage'], audience: 'internal' },
      { path: '/transporters/rankings', label: 'Rankings', icon: <TrophyOutlined />, anyPermission: ['transporters.performance.read', 'transporters.performance.manage'], audience: 'internal' },
      { path: '/transporters/selection', label: 'Find a transporter', icon: <SearchOutlined />, anyPermission: ['transporters.select', 'transporters.performance.manage'], audience: 'internal' },
      { path: '/transporters/placements', label: 'Vehicle placements', icon: <CarOutlined />, anyPermission: ['transporters.performance.read', 'transporters.performance.manage'], audience: 'internal' },
      { path: '/transporters/placements', label: 'Placements', icon: <CarOutlined />, anyPermission: ['transporters.performance.self'], audience: 'vendor' },
      { path: '/transporters/alerts', label: 'Transporter alerts', icon: <AlertOutlined />, anyPermission: ['transporters.performance.read', 'transporters.performance.manage'], audience: 'internal' },
      { path: '/my-company', label: 'My company', icon: <BankOutlined />, anyPermission: ['transporters.self.manage'], audience: 'vendor' },
      { path: '/transporters/compliance', label: 'Compliance', icon: <SafetyOutlined />, anyPermission: ['transporters.read', 'transporters.manage', 'transporters.self.manage'] },
    ],
    reports: [
      rep('R09_TRANSPORTER_SCORECARD', 'Performance scorecard', 'reports.transporters'),
      rep('R10_TRANSPORTER_RANKING', 'Ranking', 'reports.transporters'),
      rep('R11_TRANSPORTER_BENCHMARK', 'Benchmark', 'reports.transporters'),
      rep('R12_TENDER_PERFORMANCE', 'Tender performance', 'reports.transporters'),
      rep('R13_PLACEMENT_COMPLIANCE', 'Placement compliance', 'reports.transporters'),
      rep('R14_OTP_OTD', 'OTP / OTD performance', 'reports.transporters'),
      rep('R15_POD_COMPLIANCE_BY_TRANSPORTER', 'POD compliance by transporter', 'reports.transporters'),
      rep('R16_TRANSPORTER_EXCEPTIONS', 'Transporter exceptions', 'reports.transporters'),
    ],
  },
  {
    label: 'Freight contracts',
    icon: <FileProtectOutlined />,
    items: [
      { path: '/contracts/dashboard', label: 'Contract overview', icon: <FundOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts', label: 'Contracts', icon: <FileProtectOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/rates', label: 'Rate management', icon: <TableOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/simulator', label: 'Rate simulator', icon: <CalculatorOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/ratings', label: 'Rating history', icon: <HistoryOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/dph', label: 'Diesel adjustment', icon: <ThunderboltOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/accessorials', label: 'Extra charges', icon: <PlusCircleOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/contracts/renewals', label: 'Renewals', icon: <SyncOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/rate-finder', label: 'Rate finder', icon: <CalculatorOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
      { path: '/rate-masters', label: 'Rate masters', icon: <DatabaseOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
    ],
    reports: [
      rep('R31_CONTRACT_EXPIRY', 'Contract status & expiry', 'reports.contracts'),
      rep('R32_RATE_COVERAGE', 'Rate coverage', 'reports.contracts'),
      rep('R33_RATE_SLAB', 'Rate / slab report', 'reports.contracts'),
      rep('R34_DPH_SURCHARGE', 'DPH & surcharge', 'reports.contracts'),
      rep('R35_FREIGHT_RATING_AUDIT', 'Freight rating audit', 'reports.contracts'),
    ],
  },
  {
    label: 'Orders & planning',
    icon: <ClusterOutlined />,
    items: [
      { path: '/locations', label: 'Locations', icon: <EnvironmentOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/orders', label: 'Orders', icon: <InboxOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/planning', label: 'Planning', icon: <ClusterOutlined />, anyPermission: ['shipments.plan'], audience: 'internal' },
      { path: '/shipments', label: 'Shipments', icon: <ContainerOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/deliveries', label: 'POD & deliveries', icon: <FileDoneOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/shipments', label: 'Tenders', icon: <SendOutlined />, anyPermission: ['shipments.respond'], audience: 'vendor' },
      { path: '/deliveries', label: 'Deliveries', icon: <FileDoneOutlined />, anyPermission: ['shipments.respond'], audience: 'vendor' },
    ],
    reports: [
      rep('R02_LOAD_PLANNING_SUMMARY', 'Load planning summary', 'reports.planning'),
      rep('R03_VEHICLE_ALLOCATION', 'Vehicle allocation', 'reports.planning'),
      rep('R04_VEHICLE_UTILISATION', 'Vehicle utilisation', 'reports.planning'),
      rep('R05_UNPLANNED', 'Unplanned shipments', 'reports.planning'),
      rep('R06_FTL_VS_PTL', 'FTL vs PTL analysis', 'reports.planning'),
      rep('R07_CONSOLIDATION_SAVINGS', 'Consolidation & savings', 'reports.planning'),
      rep('R08_PLANNED_VS_ACTUAL_TRIP', 'Planned vs actual trip', 'reports.planning'),
    ],
  },
  {
    label: 'Delivery & proof',
    icon: <SolutionOutlined />,
    items: [
      { path: '/delivery', label: 'Delivery & proof', icon: <SolutionOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery', label: 'My deliveries', icon: <SolutionOutlined />, anyPermission: ['deliveries.execute'], audience: 'vendor' },
      { path: '/driver', label: 'Driver app', icon: <MobileOutlined />, anyPermission: ['deliveries.execute'], audience: 'vendor' },
      { path: '/delivery/dashboard', label: 'Proof dashboard', icon: <FundOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery/reports', label: 'Delivery reports', icon: <BarChartOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery/pods', label: 'Proofs & review', icon: <FileProtectOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review', 'deliveries.execute'] },
      { path: '/delivery/exceptions', label: 'Delivery exceptions', icon: <ExceptionOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.exceptions.manage', 'deliveries.execute'] },
      { path: '/delivery/settings', label: 'Delivery rules', icon: <ControlOutlined />, anyPermission: ['deliveries.configure'], audience: 'internal' },
    ],
    reports: [
      rep('R17_DELIVERY_PERFORMANCE', 'Delivery performance', 'reports.delivery'),
      rep('R18_POD_COMPLIANCE', 'POD compliance', 'reports.delivery'),
      rep('R19_POD_AGEING', 'POD ageing', 'reports.delivery'),
      rep('R20_POD_REJECTIONS', 'POD rejections', 'reports.delivery'),
      rep('R23_FAILED_REFUSED', 'Failed / refused deliveries', 'reports.delivery'),
    ],
  },
  {
    label: 'Shipment tracking',
    icon: <RadarChartOutlined />,
    items: [
      { path: '/tracking', label: 'Control tower', icon: <RadarChartOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/dashboard', label: 'Tracking overview', icon: <FundOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/history', label: 'Trip replay', icon: <HistoryOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/vehicles', label: 'Vehicle tracking', icon: <CarOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/exceptions', label: 'Tracking exceptions', icon: <ExceptionOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/geofences', label: 'Geofences', icon: <GlobalOutlined />, anyPermission: ['tracking.read', 'tracking.geofences.manage'], audience: 'internal' },
      { path: '/tracking/reports', label: 'Tracking reports', icon: <BarChartOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/drive', label: 'Track my trip', icon: <AimOutlined />, anyPermission: ['tracking.execute'], audience: 'vendor' },
      { path: '/tracking/settings', label: 'Tracking rules', icon: <ControlOutlined />, anyPermission: ['tracking.configure'], audience: 'internal' },
    ],
    reports: [
      rep('R24_CONTROL_TOWER', 'Live control tower', 'reports.tracking'),
      rep('R25_ETA_DELAY', 'ETA / delay', 'reports.tracking'),
      rep('R26_ROUTE_DEVIATION', 'Route deviation', 'reports.tracking'),
      rep('R27_DWELL_TIME', 'Dwell time', 'reports.tracking'),
      rep('R28_TRACKING_HEALTH', 'Tracking health', 'reports.tracking'),
      rep('R29_TRACKING_GAPS', 'Tracking gaps', 'reports.tracking'),
      rep('R30_PLANNED_VS_ACTUAL_ROUTE', 'Planned vs actual route', 'reports.tracking'),
    ],
  },
  {
    label: 'Reports & Analytics',
    icon: <LineChartOutlined />,
    items: [
      { path: '/reports/executive', label: 'Executive dashboard', icon: <LineChartOutlined />, anyPermission: ['reports.executive'], audience: 'internal' },
      { path: '/reports', label: 'All reports', icon: <BarChartOutlined />, anyPermission: ['reports.read', 'reports.self'] },
      { path: '/reports/exports', label: 'My exports', icon: <DownloadOutlined />, anyPermission: ['reports.read', 'reports.self'] },
      { path: '/reports/schedules', label: 'Scheduled reports', icon: <ScheduleOutlined />, anyPermission: ['reports.schedule'], audience: 'internal' },
      { path: '/reports/settings', label: 'Report settings', icon: <SettingOutlined />, anyPermission: ['reports.manage'], audience: 'internal' },
      { path: '/reports/audit', label: 'Report audit', icon: <AuditOutlined />, anyPermission: ['reports.audit'], audience: 'internal' },
    ],
    reports: [
      rep('R36_SHIPMENT_360', 'Shipment 360', 'reports.cross'),
      rep('R37_LANE_PERFORMANCE', 'Lane performance', 'reports.cross'),
      rep('R38_COST_VS_PERFORMANCE', 'Transporter cost vs performance', 'reports.cross'),
      rep('R39_COST_VS_SERVICE', 'Cost vs service', 'reports.cross'),
    ],
  },
  {
    label: 'Workflow',
    icon: <CheckSquareOutlined />,
    items: [{ path: '/approvals', label: 'Approvals', icon: <CheckSquareOutlined />, audience: 'internal', anyModule: approvalModules }],
  },
  {
    label: 'Administration',
    icon: <TeamOutlined />,
    items: [
      { path: '/admin/users', label: 'Users', icon: <TeamOutlined />, anyPermission: ['users.read'] },
      { path: '/admin/roles', label: 'Roles & permissions', icon: <SafetyCertificateOutlined />, anyPermission: ['roles.read'] },
      { path: '/admin/approval-policies', label: 'Approval policies', icon: <PartitionOutlined />, anyPermission: ['approvals.policies.manage'], anyModule: approvalModules },
      { path: '/admin/audit', label: 'Audit trail', icon: <AuditOutlined />, anyPermission: ['audit.read'] },
    ],
  },
  {
    label: 'Platform',
    icon: <BankOutlined />,
    items: [{ path: '/platform/tenants', label: 'Organisations & modules', icon: <BankOutlined />, anyPermission: ['platform.tenants.manage'] }],
  },
]
