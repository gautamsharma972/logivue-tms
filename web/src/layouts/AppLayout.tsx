import {
  CarOutlined,
  KeyOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  MoonOutlined,
  SunOutlined,
} from '@ant-design/icons'
import { Avatar, Badge, Button, Dropdown, Flex, Layout, Menu, Skeleton, Tag, Tooltip, Typography, type MenuProps } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { Suspense, useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useThemeMode } from '@/app/theme'
import { useAuth } from '@/features/auth/AuthContext'
import { approvalsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { initials } from '@/lib/format'
import { navigation } from './navigation'

const { Sider, Header, Content } = Layout

export function AppLayout() {
  const { user, can, logout } = useAuth()
  const { mode, toggle } = useThemeMode()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [collapsed, setCollapsed] = useState(false)

  const inbox = useQuery({
    queryKey: queryKeys.approvals.requests({ scope: 'inbox', pageSize: 1 }),
    queryFn: () => approvalsApi.requests({ scope: 'inbox', pageSize: 1 }),
    refetchInterval: 60_000,
  })
  const pendingApprovals = inbox.data?.totalCount ?? 0

  const menuItems = useMemo<MenuProps['items']>(
    () =>
      navigation
        .map((group) => ({
          type: 'group' as const,
          key: group.label,
          label: group.label,
          children: group.items
            .filter(
              (item) =>
                (!item.anyPermission || item.anyPermission.some(can)) &&
                (!item.audience || (item.audience === 'vendor') === (user?.transporterId != null)),
            )
            .map((item) => ({
              key: item.path,
              icon: item.icon,
              label:
                item.path === '/approvals' && pendingApprovals > 0 ? (
                  <Flex justify="space-between" align="center">
                    {item.label}
                    <Badge count={pendingApprovals} size="small" />
                  </Flex>
                ) : (
                  item.label
                ),
            })),
        }))
        .filter((group) => group.children.length > 0),
    [can, pendingApprovals, user?.transporterId],
  )

  const activeKey = useMemo(() => {
    // Vendors reach their own company at /transporters/{id}; highlight "My company" there.
    if (user?.transporterId && pathname.startsWith('/transporters') && !pathname.startsWith('/transporters/compliance')) return '/my-company'
    const paths = navigation.flatMap((g) => g.items.map((i) => i.path)).filter((p) => p !== '/')
    // Longest match wins, so /transporters/compliance beats /transporters.
    return paths.filter((p) => pathname.startsWith(p)).sort((a, b) => b.length - a.length)[0] ?? '/'
  }, [pathname, user?.transporterId])

  const userMenu: MenuProps['items'] = [
    {
      key: 'info',
      disabled: true,
      label: (
        <div style={{ cursor: 'default' }}>
          <Typography.Text strong>{user?.fullName}</Typography.Text>
          <br />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {user?.email}
          </Typography.Text>
        </div>
      ),
    },
    { type: 'divider' },
    { key: 'password', icon: <KeyOutlined />, label: 'Change password' },
    { key: 'logout', icon: <LogoutOutlined />, label: 'Sign out', danger: true },
  ]

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider
        collapsible
        trigger={null}
        collapsed={collapsed}
        onCollapse={setCollapsed}
        breakpoint="lg"
        width={248}
        style={{ position: 'sticky', top: 0, height: '100vh', overflow: 'auto' }}
      >
        <Flex align="center" gap={10} style={{ height: 56, padding: '0 20px' }}>
          <CarOutlined style={{ fontSize: 22, color: '#7fa2ff' }} />
          {!collapsed && (
            <Typography.Text strong style={{ color: '#fff', fontSize: 16, letterSpacing: 0.3 }}>
              TMS
            </Typography.Text>
          )}
        </Flex>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[activeKey]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
          style={{ padding: '4px 8px', borderInlineEnd: 0 }}
        />
      </Sider>

      <Layout>
        <Header
          style={{
            position: 'sticky',
            top: 0,
            zIndex: 10,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            borderBottom: '1px solid rgba(128,128,128,0.18)',
          }}
        >
          <Button
            type="text"
            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
            icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setCollapsed((c) => !c)}
          />
          <Flex align="center" gap={12}>
            <Tag color="blue" variant="filled" style={{ margin: 0 }}>
              {user?.tenantName}
            </Tag>
            <Tooltip title={mode === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}>
              <Button
                type="text"
                aria-label="Toggle theme"
                icon={mode === 'dark' ? <SunOutlined /> : <MoonOutlined />}
                onClick={toggle}
              />
            </Tooltip>
            <Dropdown
              trigger={['click']}
              menu={{ items: userMenu, onClick: ({ key }) => (key === 'logout' ? void logout() : key === 'password' && navigate('/change-password')) }}
            >
              <Avatar style={{ cursor: 'pointer', background: '#2f5bea' }} aria-label="Account menu">
                {initials(user?.fullName ?? '?')}
              </Avatar>
            </Dropdown>
          </Flex>
        </Header>

        <Content style={{ padding: 24, maxWidth: 1480, width: '100%', margin: '0 auto' }}>
          <Suspense fallback={<Skeleton active paragraph={{ rows: 8 }} />}>
            <Outlet />
          </Suspense>
        </Content>
      </Layout>
    </Layout>
  )
}
