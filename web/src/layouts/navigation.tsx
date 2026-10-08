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
  /** Restrict to staff (`internal`) or to vendor-portal users (`vendor`). Omit for both. */
  audience?: 'internal' | 'vendor'
}

export interface NavGroup {
  label: string
  items: NavItem[]
}

/** Add each new module's pages here; the sidebar, permission filtering and active state follow automatically. */
export const navigation: NavGroup[] = [
  {
    label: 'Overview',
    items: [{ path: '/', label: 'Dashboard', icon: <DashboardOutlined /> }],
  },
  {
    label: 'Freight network',
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
      { path: '/transporters/compliance', label: 'Compliance', icon: <SafetyOutlined />, anyPermission: ['transporters.read', 'transporters.manage', 'transporters.self.manage'] },
    ],
  },
  {
    label: 'Logistics',
    items: [
      { path: '/locations', label: 'Locations', icon: <EnvironmentOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/orders', label: 'Orders', icon: <InboxOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/planning', label: 'Planning', icon: <ClusterOutlined />, anyPermission: ['shipments.plan'], audience: 'internal' },
      { path: '/shipments', label: 'Shipments', icon: <ContainerOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/deliveries', label: 'POD & deliveries', icon: <FileDoneOutlined />, anyPermission: ['shipments.read', 'shipments.plan'], audience: 'internal' },
      { path: '/shipments', label: 'Tenders', icon: <SendOutlined />, anyPermission: ['shipments.respond'], audience: 'vendor' },
      { path: '/deliveries', label: 'Deliveries', icon: <FileDoneOutlined />, anyPermission: ['shipments.respond'], audience: 'vendor' },
      { path: '/delivery', label: 'Delivery & proof', icon: <SolutionOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery', label: 'My deliveries', icon: <SolutionOutlined />, anyPermission: ['deliveries.execute'], audience: 'vendor' },
      { path: '/driver', label: 'Driver app', icon: <MobileOutlined />, anyPermission: ['deliveries.execute'], audience: 'vendor' },
      { path: '/delivery/dashboard', label: 'Proof dashboard', icon: <FundOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery/reports', label: 'Delivery reports', icon: <BarChartOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review'], audience: 'internal' },
      { path: '/delivery/pods', label: 'Proofs & review', icon: <FileProtectOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.pod.review', 'deliveries.execute'] },
      { path: '/delivery/exceptions', label: 'Delivery exceptions', icon: <ExceptionOutlined />, anyPermission: ['deliveries.read', 'deliveries.manage', 'deliveries.exceptions.manage', 'deliveries.execute'] },
      { path: '/tracking', label: 'Control tower', icon: <RadarChartOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/dashboard', label: 'Tracking overview', icon: <FundOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/history', label: 'Trip replay', icon: <HistoryOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/vehicles', label: 'Vehicle tracking', icon: <CarOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/exceptions', label: 'Tracking exceptions', icon: <ExceptionOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/geofences', label: 'Geofences', icon: <GlobalOutlined />, anyPermission: ['tracking.read', 'tracking.geofences.manage'], audience: 'internal' },
      { path: '/tracking/reports', label: 'Tracking reports', icon: <BarChartOutlined />, anyPermission: ['tracking.read', 'tracking.manage'], audience: 'internal' },
      { path: '/tracking/settings', label: 'Tracking rules', icon: <ControlOutlined />, anyPermission: ['tracking.configure'], audience: 'internal' },
      { path: '/tracking/drive', label: 'Track my trip', icon: <AimOutlined />, anyPermission: ['tracking.execute'], audience: 'vendor' },
      { path: '/delivery/settings', label: 'Delivery rules', icon: <ControlOutlined />, anyPermission: ['deliveries.configure'], audience: 'internal' },
    ],
  },
  {
    label: 'Reports & Analytics',
    items: [
      { path: '/reports/executive', label: 'Executive dashboard', icon: <LineChartOutlined />, anyPermission: ['reports.executive'], audience: 'internal' },
      { path: '/reports', label: 'All reports', icon: <BarChartOutlined />, anyPermission: ['reports.read', 'reports.self'] },
      { path: '/reports/R24_CONTROL_TOWER', label: 'Live control tower', icon: <RadarChartOutlined />, anyPermission: ['reports.tracking'], audience: 'internal' },
      { path: '/reports/exports', label: 'My exports', icon: <DownloadOutlined />, anyPermission: ['reports.read', 'reports.self'] },
      { path: '/reports/schedules', label: 'Scheduled reports', icon: <ScheduleOutlined />, anyPermission: ['reports.schedule'], audience: 'internal' },
      { path: '/reports/settings', label: 'Report settings', icon: <SettingOutlined />, anyPermission: ['reports.manage'], audience: 'internal' },
      { path: '/reports/audit', label: 'Report audit', icon: <AuditOutlined />, anyPermission: ['reports.audit'], audience: 'internal' },
    ],
  },
  {
    label: 'Workflow',
    items: [{ path: '/approvals', label: 'Approvals', icon: <CheckSquareOutlined />, audience: 'internal' }],
  },
  {
    label: 'Administration',
    items: [
      { path: '/admin/users', label: 'Users', icon: <TeamOutlined />, anyPermission: ['users.read'] },
      { path: '/admin/roles', label: 'Roles & permissions', icon: <SafetyCertificateOutlined />, anyPermission: ['roles.read'] },
      { path: '/admin/approval-policies', label: 'Approval policies', icon: <PartitionOutlined />, anyPermission: ['approvals.policies.manage'] },
      { path: '/admin/audit', label: 'Audit trail', icon: <AuditOutlined />, anyPermission: ['audit.read'] },
    ],
  },
]
