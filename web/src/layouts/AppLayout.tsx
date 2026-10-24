import {
  KeyOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  MoonOutlined,
  SunOutlined,
} from '@ant-design/icons'
import { Avatar, Badge, Button, Dropdown, Flex, Layout, Menu, Skeleton, Tag, Tooltip, Typography, type MenuProps } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { Suspense, useCallback, useMemo, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useThemeMode } from '@/app/theme'
import { NotificationBell } from '@/features/deliveries/NotificationBell'
import { useAuth } from '@/features/auth/AuthContext'
import { approvalsApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { AppFooter } from '@/components/AppFooter'
import { Logo } from '@/components/Logo'
import { initials } from '@/lib/format'
import { useIsMobile } from '@/lib/useIsMobile'
import { navigation, type NavGroup, type NavItem } from './navigation'

const groupKey = (group: NavGroup) => `module:${group.label}`

const { Sider, Header, Content, Footer } = Layout

export function AppLayout() {
  const { user, can, logout } = useAuth()
  const { mode, toggle } = useThemeMode()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const mobile = useIsMobile()
  // On a phone the menu is a drawer that starts closed; on a desktop it starts open.
  const [collapsed, setCollapsed] = useState(mobile)
  const [wasMobile, setWasMobile] = useState(mobile)
  if (mobile !== wasMobile) {
    setWasMobile(mobile)
    setCollapsed(mobile)
  }

  const inbox = useQuery({
    queryKey: queryKeys.approvals.requests({ scope: 'inbox', pageSize: 1 }),
    queryFn: () => approvalsApi.requests({ scope: 'inbox', pageSize: 1 }),
    refetchInterval: 60_000,
  })
  const pendingApprovals = inbox.data?.totalCount ?? 0

  const visible = useCallback(
    (item: NavItem) => (!item.anyPermission || item.anyPermission.some(can)) && (!item.audience || (item.audience === 'vendor') === (user?.transporterId != null)),
    [can, user?.transporterId],
  )
  const toMenuItem = useCallback(
    (item: NavItem) => ({
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
    }),
    [pendingApprovals],
  )

  // One collapsible entry per module; its reports sit under it in their own sub-heading.
  const menuItems = useMemo<MenuProps['items']>(
    () =>
      navigation.flatMap((group) => {
        const pages = group.items.filter(visible).map(toMenuItem)
        const reports = (group.reports ?? []).filter(visible).map(toMenuItem)
        if (pages.length === 0 && reports.length === 0) return []
        const [only] = pages
        if (only && pages.length === 1 && reports.length === 0) return [only] // a module with one page is just that page
        const children = [...pages, ...(reports.length > 0 ? [{ type: 'group' as const, key: `${group.label}:reports`, label: 'Reports', children: reports }] : [])]
        return [{ key: groupKey(group), icon: group.icon, label: group.label, children }]
      }),
    [visible, toMenuItem],
  )

  const activeKey = useMemo(() => {
    // Vendors reach their own company at /transporters/{id}; highlight "My company" there.
    if (user?.transporterId && pathname.startsWith('/transporters') && !pathname.startsWith('/transporters/compliance')) return '/my-company'
    const paths = navigation.flatMap((g) => [...g.items, ...(g.reports ?? [])].map((i) => i.path)).filter((p) => p !== '/')
    // Longest match wins, so /transporters/compliance beats /transporters.
    return paths.filter((p) => pathname === p || pathname.startsWith(`${p}/`)).sort((a, b) => b.length - a.length)[0] ?? '/'
  }, [pathname, user?.transporterId])

  // The module holding the open page is expanded when you arrive on it; people can still open or close any module.
  const activeGroup = useMemo(() => navigation.find((g) => [...g.items, ...(g.reports ?? [])].some((i) => i.path === activeKey)), [activeKey])
  const [openKeys, setOpenKeys] = useState<string[]>(() => (activeGroup ? [groupKey(activeGroup)] : []))
  const [seenGroup, setSeenGroup] = useState(activeGroup)
  if (activeGroup !== seenGroup) {
    setSeenGroup(activeGroup)
    if (activeGroup && !openKeys.includes(groupKey(activeGroup))) setOpenKeys([...openKeys, groupKey(activeGroup)])
  }

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
        collapsedWidth={mobile ? 0 : 80}
        width={mobile ? 280 : 248}
        style={mobile ? { position: 'fixed', top: 0, left: 0, bottom: 0, zIndex: 1001, height: '100vh', overflow: 'auto' } : { position: 'sticky', top: 0, height: '100vh', overflow: 'auto' }}
      >
        {/* The logo is dark blue and orange, so it sits on a light plate against the dark sidebar. */}
        <Flex align="center" justify="center" style={{ height: 72, padding: '0 12px' }}>
          <div style={{ background: '#fff', borderRadius: 10, padding: collapsed ? '4px 6px' : '4px 12px', display: 'flex', alignItems: 'center' }}>
            {collapsed ? <Logo variant="mark" height={30} /> : <Logo height={56} />}
          </div>
        </Flex>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[activeKey]}
          openKeys={collapsed ? undefined : openKeys}
          onOpenChange={setOpenKeys}
          items={menuItems}
          onClick={({ key }) => { navigate(key); if (mobile) setCollapsed(true) }}
          style={{ padding: '4px 8px', borderInlineEnd: 0 }}
        />
      </Sider>

      {mobile && !collapsed && <div aria-hidden onClick={() => setCollapsed(true)} style={{ position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(0,0,0,0.45)' }} />}

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
            paddingInline: mobile ? 12 : undefined,
          }}
        >
          <Button
            type="text"
            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
            icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setCollapsed((c) => !c)}
          />
          <Flex align="center" gap={12}>
            {!mobile && (
              <Tag color="blue" variant="filled" style={{ margin: 0 }}>
                {user?.tenantName}
              </Tag>
            )}
            <Tooltip title={mode === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}>
              <Button
                type="text"
                aria-label="Toggle theme"
                icon={mode === 'dark' ? <SunOutlined /> : <MoonOutlined />}
                onClick={toggle}
              />
            </Tooltip>
            {can('deliveries.read') && <NotificationBell />}
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

        <Content style={{ padding: mobile ? 12 : 24, maxWidth: 1480, width: '100%', margin: '0 auto', minWidth: 0 }}>
          <Suspense fallback={<Skeleton active paragraph={{ rows: 8 }} />}>
            <Outlet />
          </Suspense>
        </Content>
        <Footer style={{ padding: mobile ? '12px' : '16px 24px', background: 'transparent' }}>
          <AppFooter />
        </Footer>
      </Layout>
    </Layout>
  )
}
