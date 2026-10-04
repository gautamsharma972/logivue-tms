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
