import { Button, Result, Spin } from 'antd'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'

export function RequireAuth() {
  const { status, user } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <Spin size="large" description="Restoring your session…">
          <div style={{ padding: 48 }} />
        </Spin>
      </div>
    )
  }
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (user?.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />
  return <Outlet />
}

export function RequirePermission({ permission }: { permission: string }) {
  const { can } = useAuth()
  return can(permission) ? <Outlet /> : <ForbiddenPage />
}

export function RequireAnyPermission({ permissions }: { permissions: string[] }) {
  const { can } = useAuth()
  return permissions.some(can) ? <Outlet /> : <ForbiddenPage />
}

export function ForbiddenPage() {
  return (
    <Result
      status="403"
      title="Access denied"
      subTitle="You don't have permission to view this page. Ask an administrator if you need access."
      extra={
        <Button type="primary" href="/">
          Back to dashboard
        </Button>
      }
    />
  )
}
