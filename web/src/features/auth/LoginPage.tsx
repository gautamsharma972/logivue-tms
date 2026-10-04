import { BankOutlined, LockOutlined, MailOutlined } from '@ant-design/icons'
import { Alert, Button, Checkbox, Flex, Form, Input } from 'antd'
import { useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { AuthShell } from '@/components/AuthShell'
import { toApiError } from '@/lib/api/errors'
import { useAuth } from './AuthContext'

const TENANT_KEY = 'tms.tenantCode'

interface FormValues {
  tenantCode: string
  email: string
  password: string
  remember: boolean
}

function savedTenant(): string {
  try {
    return localStorage.getItem(TENANT_KEY) ?? ''
  } catch {
    return ''
  }
}

export function LoginPage() {
  const { status, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    return <Navigate to={(location.state as { from?: string } | null)?.from ?? '/'} replace />
  }

  const onFinish = async ({ remember, ...credentials }: FormValues) => {
    setSubmitting(true)
    setError(null)
    try {
      await login({ ...credentials, tenantCode: credentials.tenantCode.trim() })
      try {
        if (remember) localStorage.setItem(TENANT_KEY, credentials.tenantCode.trim())
        else localStorage.removeItem(TENANT_KEY)
      } catch {
        /* optional convenience only */
      }
      navigate((location.state as { from?: string } | null)?.from ?? '/', { replace: true })
    } catch (e) {
      setError(toApiError(e).message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthShell title="Sign in" subtitle="Use the organisation code provided by your administrator.">
      {error && <Alert type="error" showIcon title={error} style={{ marginBottom: 16 }} role="alert" />}

      <Form<FormValues>
        layout="vertical"
        requiredMark={false}
        initialValues={{ tenantCode: savedTenant(), remember: savedTenant() !== '' }}
        onFinish={onFinish}
        onValuesChange={() => error && setError(null)}
      >
        <Form.Item label="Organisation code" name="tenantCode" rules={[{ required: true, message: 'Enter your organisation code' }]}>
          <Input prefix={<BankOutlined />} autoComplete="organization" placeholder="e.g. ACME" size="large" />
        </Form.Item>
        <Form.Item
          label="Email"
          name="email"
          rules={[{ required: true, message: 'Enter your email' }, { type: 'email', message: 'Enter a valid email' }]}
        >
          <Input prefix={<MailOutlined />} autoComplete="username" placeholder="you@company.com" size="large" />
        </Form.Item>
        <Form.Item label="Password" name="password" rules={[{ required: true, message: 'Enter your password' }]}>
          <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" />
        </Form.Item>
        <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
          <Form.Item name="remember" valuePropName="checked" noStyle>
            <Checkbox>Remember organisation code</Checkbox>
          </Form.Item>
          <Link to="/forgot-password">Forgot password?</Link>
        </Flex>
        <Button type="primary" htmlType="submit" size="large" block loading={submitting}>
          Sign in
        </Button>
      </Form>
    </AuthShell>
  )
}
