import { Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AppFooter } from './AppFooter'
import { Logo } from './Logo'

/** The branded split screen shared by sign-in, forgot/reset password and forced password change. */
export function AuthShell({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <Flex style={{ minHeight: '100vh' }}>
      <Flex
        vertical
        justify="space-between"
        className="login-brand"
        style={{ flex: '1 1 46%', padding: 48, color: '#fff', background: 'linear-gradient(145deg, #0f1b33 0%, #1c3a8a 60%, #2f5bea 100%)' }}
      >
        <div style={{ alignSelf: 'flex-start', background: '#fff', borderRadius: 16, padding: '12px 20px', boxShadow: '0 8px 24px rgba(0,0,0,0.25)' }}>
          <Logo height={120} />
        </div>
        <div>
          <Typography.Title style={{ color: '#fff', marginBottom: 12 }}>Every shipment, rate and rupee of freight — in one place.</Typography.Title>
          <Typography.Paragraph style={{ color: 'rgba(255,255,255,0.78)', fontSize: 16, maxWidth: 520 }}>
            Plan loads, manage transporters and contracts, track deliveries and audit freight bills with a full trail of who changed what.
          </Typography.Paragraph>
        </div>
        <Typography.Text style={{ color: 'rgba(255,255,255,0.55)' }}>Transport Management System</Typography.Text>
      </Flex>

      <Flex vertical align="center" justify="space-between" style={{ flex: '1 1 54%', padding: 24, minWidth: 0 }}>
        <span />
        <Flex vertical align="center" gap={16} style={{ width: '100%' }}>
          {/* The brand panel is hidden on narrow screens, so the logo moves above the form. */}
          <div className="login-mobile-logo" style={{ display: 'none' }}><Logo height={96} /></div>
          <Card variant="borderless" style={{ width: '100%', maxWidth: 400, boxShadow: '0 8px 32px rgba(15,27,51,0.12)' }}>
            <Typography.Title level={3} style={{ marginTop: 0 }}>{title}</Typography.Title>
            {subtitle && <Typography.Paragraph type="secondary">{subtitle}</Typography.Paragraph>}
            {children}
          </Card>
        </Flex>
        <AppFooter style={{ marginTop: 24 }} />
      </Flex>
    </Flex>
  )
}
