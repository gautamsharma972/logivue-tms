import { Button, Result } from 'antd'
import { lazy } from 'react'
import { Link, Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'
import { LoginPage } from '@/features/auth/LoginPage'
import { ChangePasswordPage, ForgotPasswordPage, ResetPasswordPage } from '@/features/auth/PasswordPages'
import { RequireAnyPermission, RequireAuth, RequirePermission } from '@/features/auth/guards'
import { AppLayout } from '@/layouts/AppLayout'

// Each page is its own chunk, so a user only downloads the screens they actually open.
const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage').then((m) => ({ default: m.DashboardPage })))
const UsersPage = lazy(() => import('@/features/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const RolesPage = lazy(() => import('@/features/roles/RolesPage').then((m) => ({ default: m.RolesPage })))
const ApprovalsPage = lazy(() => import('@/features/approvals/ApprovalsPage').then((m) => ({ default: m.ApprovalsPage })))
const PoliciesPage = lazy(() => import('@/features/approvals/PoliciesPage').then((m) => ({ default: m.PoliciesPage })))
const TransportersPage = lazy(() => import('@/features/transporters/TransportersPage').then((m) => ({ default: m.TransportersPage })))
const TransporterDetailPage = lazy(() => import('@/features/transporters/TransporterDetailPage').then((m) => ({ default: m.TransporterDetailPage })))
const PlacementsPage = lazy(() => import('@/features/transporters/PlacementsPage').then((m) => ({ default: m.PlacementsPage })))
const AlertsPage = lazy(() => import('@/features/transporters/AlertsPage').then((m) => ({ default: m.AlertsPage })))
const SelectionPage = lazy(() => import('@/features/transporters/SelectionPage').then((m) => ({ default: m.SelectionPage })))
const RankingsPage = lazy(() => import('@/features/transporters/RankingsPage').then((m) => ({ default: m.RankingsPage })))
const TransporterSetupPage = lazy(() => import('@/features/transporters/TransporterSetupPage').then((m) => ({ default: m.TransporterSetupPage })))
const VehicleTypesPage = lazy(() => import('@/features/transporters/VehicleTypesPage').then((m) => ({ default: m.VehicleTypesPage })))
const CompliancePage = lazy(() => import('@/features/transporters/CompliancePage').then((m) => ({ default: m.CompliancePage })))
const ContractsPage = lazy(() => import('@/features/contracts/ContractsPage').then((m) => ({ default: m.ContractsPage })))
const ContractDetailPage = lazy(() => import('@/features/contracts/ContractDetailPage').then((m) => ({ default: m.ContractDetailPage })))
const RateFinderPage = lazy(() => import('@/features/contracts/RateFinderPage').then((m) => ({ default: m.RateFinderPage })))
const OrdersPage = lazy(() => import('@/features/shipments/OrdersPage').then((m) => ({ default: m.OrdersPage })))
const LocationsPage = lazy(() => import('@/features/shipments/LocationsPage').then((m) => ({ default: m.LocationsPage })))
const PlanningPage = lazy(() => import('@/features/shipments/PlanningPage').then((m) => ({ default: m.PlanningPage })))
const PlanRunPage = lazy(() => import('@/features/shipments/PlanRunPage').then((m) => ({ default: m.PlanRunPage })))
const DeliveriesPage = lazy(() => import('@/features/shipments/DeliveriesPage').then((m) => ({ default: m.DeliveriesPage })))
const ShipmentsPage = lazy(() => import('@/features/shipments/ShipmentsPage').then((m) => ({ default: m.ShipmentsPage })))
const ShipmentDetailPage = lazy(() => import('@/features/shipments/ShipmentDetailPage').then((m) => ({ default: m.ShipmentDetailPage })))
const RateMastersPage = lazy(() => import('@/features/contracts/RateMastersPage').then((m) => ({ default: m.RateMastersPage })))
const DeliveriesListPage = lazy(() => import('@/features/deliveries/DeliveriesListPage').then((m) => ({ default: m.DeliveriesListPage })))
const DeliveryDetailPage = lazy(() => import('@/features/deliveries/DeliveryDetailPage').then((m) => ({ default: m.DeliveryDetailPage })))
const PodsListPage = lazy(() => import('@/features/deliveries/PodsListPage').then((m) => ({ default: m.PodsListPage })))
const PodDetailPage = lazy(() => import('@/features/deliveries/PodDetailPage').then((m) => ({ default: m.PodDetailPage })))
const ExceptionsPage = lazy(() => import('@/features/deliveries/ExceptionsPage').then((m) => ({ default: m.ExceptionsPage })))
const DeliveryDashboardPage = lazy(() => import('@/features/deliveries/DeliveryDashboardPage').then((m) => ({ default: m.DeliveryDashboardPage })))
const DeliveryReportsPage = lazy(() => import('@/features/deliveries/DeliveryReportsPage').then((m) => ({ default: m.DeliveryReportsPage })))
const NotificationsPage = lazy(() => import('@/features/deliveries/NotificationsPage').then((m) => ({ default: m.NotificationsPage })))
const ControlTowerPage = lazy(() => import('@/features/tracking/ControlTowerPage').then((m) => ({ default: m.ControlTowerPage })))
const ShipmentTrackingPage = lazy(() => import('@/features/tracking/ShipmentTrackingPage').then((m) => ({ default: m.ShipmentTrackingPage })))
const VehicleTrackingPage = lazy(() => import('@/features/tracking/VehiclesPage').then((m) => ({ default: m.VehicleTrackingPage })))
const TrackingExceptionsPage = lazy(() => import('@/features/tracking/ExceptionsPage').then((m) => ({ default: m.TrackingExceptionsPage })))
const GeofencesPage = lazy(() => import('@/features/tracking/GeofencesPage').then((m) => ({ default: m.GeofencesPage })))
const TrackingSettingsPage = lazy(() => import('@/features/tracking/SettingsPage').then((m) => ({ default: m.TrackingSettingsPage })))
const TrackingReportsPage = lazy(() => import('@/features/tracking/ReportsPage').then((m) => ({ default: m.TrackingReportsPage })))
const DriverTrackingPage = lazy(() => import('@/features/tracking/DriverTrackingPage').then((m) => ({ default: m.DriverTrackingPage })))
const CustomerTrackingPage = lazy(() => import('@/features/tracking/CustomerTrackingPage').then((m) => ({ default: m.CustomerTrackingPage })))
const DeliverySettingsPage = lazy(() => import('@/features/deliveries/DeliverySettingsPage').then((m) => ({ default: m.DeliverySettingsPage })))
const DriverArea = lazy(() => import('@/features/deliveries/DriverArea').then((m) => ({ default: m.DriverArea })))
const MobileDeliveriesPage = lazy(() => import('@/features/deliveries/MobileDeliveriesPage').then((m) => ({ default: m.MobileDeliveriesPage })))
const MobileDeliveryPage = lazy(() => import('@/features/deliveries/MobileDeliveryPage').then((m) => ({ default: m.MobileDeliveryPage })))
const AuditLogPage = lazy(() => import('@/features/audit/AuditLogPage').then((m) => ({ default: m.AuditLogPage })))

/** Vendor-portal users land on their own company record. */
function MyCompany() {
  const { user } = useAuth()
  return user?.transporterId ? <Navigate to={`/transporters/${user.transporterId}`} replace /> : <Navigate to="/" replace />
}

function NotFound() {
  return (
    <Result
      status="404"
      title="Page not found"
      subTitle="The page you're looking for doesn't exist or has moved."
      extra={
        <Link to="/">
          <Button type="primary">Back to dashboard</Button>
        </Link>
      }
    />
  )
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/track/:token" element={<CustomerTrackingPage />} />
      <Route element={<RequireAuth />}>
        <Route path="/change-password" element={<ChangePasswordPage />} />
        <Route element={<AppLayout />}>
          <Route index element={<DashboardPage />} />
          <Route path="approvals" element={<ApprovalsPage />} />
          <Route path="my-company" element={<MyCompany />} />
          <Route path="transporters">
            <Route element={<RequireAnyPermission permissions={['transporters.read', 'transporters.manage']} />}>
              <Route index element={<TransportersPage />} />
            </Route>
            <Route path="compliance" element={<CompliancePage />} />
            <Route element={<RequireAnyPermission permissions={['transporters.read', 'transporters.manage']} />}>
              <Route path="vehicle-types" element={<VehicleTypesPage />} />
              <Route path="setup" element={<TransporterSetupPage />} />
            </Route>
            <Route element={<RequireAnyPermission permissions={['transporters.performance.read', 'transporters.performance.manage']} />}>
              <Route path="rankings" element={<RankingsPage />} />
            </Route>
            <Route element={<RequireAnyPermission permissions={['transporters.performance.read', 'transporters.performance.manage']} />}>
              <Route path="alerts" element={<AlertsPage />} />
            </Route>
            <Route element={<RequireAnyPermission permissions={['transporters.performance.read', 'transporters.performance.manage', 'transporters.performance.self']} />}>
              <Route path="placements" element={<PlacementsPage />} />
            </Route>
            <Route element={<RequireAnyPermission permissions={['transporters.select', 'transporters.performance.manage']} />}>
              <Route path="selection" element={<SelectionPage />} />
            </Route>
            <Route path=":id" element={<TransporterDetailPage />} />
          </Route>
          <Route element={<RequireAnyPermission permissions={['contracts.read', 'contracts.manage']} />}>
            <Route path="contracts" element={<ContractsPage />} />
            <Route path="contracts/:id" element={<ContractDetailPage />} />
            <Route path="rate-finder" element={<RateFinderPage />} />
            <Route path="rate-masters" element={<RateMastersPage />} />
          </Route>
          <Route element={<RequireAnyPermission permissions={['shipments.read', 'shipments.plan']} />}>
            <Route path="orders" element={<OrdersPage />} />
            <Route path="locations" element={<LocationsPage />} />
          </Route>
          <Route element={<RequirePermission permission="shipments.plan" />}>
            <Route path="planning" element={<PlanningPage />} />
            <Route path="planning/runs/:id" element={<PlanRunPage />} />
          </Route>
          <Route element={<RequireAnyPermission permissions={['shipments.read', 'shipments.plan', 'shipments.respond']} />}>
            <Route path="shipments" element={<ShipmentsPage />} />
            <Route path="deliveries" element={<DeliveriesPage />} />
            <Route path="shipments/:id" element={<ShipmentDetailPage />} />
          </Route>
          <Route path="delivery">
            <Route element={<RequireAnyPermission permissions={['deliveries.read', 'deliveries.manage', 'deliveries.pod.review', 'deliveries.execute']} />}>
              <Route index element={<DeliveriesListPage />} />
              <Route path="pods" element={<PodsListPage />} />
              <Route path="pods/:id" element={<PodDetailPage />} />
              <Route path="exceptions" element={<ExceptionsPage />} />
              <Route path="dashboard" element={<DeliveryDashboardPage />} />
              <Route path="reports" element={<DeliveryReportsPage />} />
              <Route path="notifications" element={<NotificationsPage />} />
              <Route path=":id" element={<DeliveryDetailPage />} />
            </Route>
            <Route element={<RequirePermission permission="deliveries.configure" />}>
              <Route path="settings" element={<DeliverySettingsPage />} />
            </Route>
          </Route>
          <Route element={<RequirePermission permission="deliveries.execute" />}>
            <Route path="driver" element={<DriverArea />}>
              <Route index element={<MobileDeliveriesPage />} />
              <Route path=":id" element={<MobileDeliveryPage />} />
            </Route>
          </Route>
          <Route path="tracking">
            <Route element={<RequireAnyPermission permissions={['tracking.read', 'tracking.manage']} />}>
              <Route index element={<ControlTowerPage />} />
              <Route path="shipments/:id" element={<ShipmentTrackingPage />} />
              <Route path="vehicles" element={<VehicleTrackingPage />} />
              <Route path="exceptions" element={<TrackingExceptionsPage />} />
              <Route path="reports" element={<TrackingReportsPage />} />
            </Route>
            <Route element={<RequireAnyPermission permissions={['tracking.read', 'tracking.geofences.manage']} />}>
              <Route path="geofences" element={<GeofencesPage />} />
            </Route>
            <Route element={<RequirePermission permission="tracking.configure" />}>
              <Route path="settings" element={<TrackingSettingsPage />} />
            </Route>
            <Route element={<RequirePermission permission="tracking.execute" />}>
              <Route path="drive" element={<DriverTrackingPage />} />
            </Route>
          </Route>
          <Route path="admin">
            <Route index element={<Navigate to="users" replace />} />
            <Route element={<RequirePermission permission="users.read" />}>
              <Route path="users" element={<UsersPage />} />
            </Route>
            <Route element={<RequirePermission permission="roles.read" />}>
              <Route path="roles" element={<RolesPage />} />
            </Route>
            <Route element={<RequirePermission permission="approvals.policies.manage" />}>
              <Route path="approval-policies" element={<PoliciesPage />} />
            </Route>
            <Route element={<RequirePermission permission="audit.read" />}>
              <Route path="audit" element={<AuditLogPage />} />
            </Route>
          </Route>
          <Route path="*" element={<NotFound />} />
        </Route>
      </Route>
    </Routes>
  )
}
