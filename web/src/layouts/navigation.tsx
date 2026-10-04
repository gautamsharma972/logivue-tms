import {
  AuditOutlined,
  BankOutlined,
  CheckSquareOutlined,
  CalculatorOutlined,
  CarOutlined,
  ClusterOutlined,
  ContainerOutlined,
  EnvironmentOutlined,
  FileDoneOutlined,
  InboxOutlined,
  SendOutlined,
  DatabaseOutlined,
  FileProtectOutlined,
  SafetyOutlined,
  DashboardOutlined,
  PartitionOutlined,
  SafetyCertificateOutlined,
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
      { path: '/transporters/rankings', label: 'Rankings', icon: <TrophyOutlined />, anyPermission: ['transporters.performance.read', 'transporters.performance.manage'], audience: 'internal' },
      { path: '/my-company', label: 'My company', icon: <BankOutlined />, anyPermission: ['transporters.self.manage'], audience: 'vendor' },
      { path: '/contracts', label: 'Contracts', icon: <FileProtectOutlined />, anyPermission: ['contracts.read', 'contracts.manage'], audience: 'internal' },
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
