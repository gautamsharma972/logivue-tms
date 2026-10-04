import { CheckCircleOutlined, LockOutlined } from '@ant-design/icons'
import { Alert, App, Button, Flex, Form, Input, Result } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { AuthShell } from '@/components/AuthShell'
import { authApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { applyFieldErrors } from '@/lib/formErrors'
import { PASSWORD_HINT, passwordRules } from '@/lib/passwordRules'
import { useAuth } from './AuthContext'

function ConfirmField({ name = 'newPassword' }: { name?: string }) {
  return (
    <Form.Item
      label="Confirm new password"
      name="confirm"
      dependencies={[name]}
      rules={[
        { required: true, message: 'Repeat the new password' },
        ({ getFieldValue }) => ({
          validator: (_, value: string) => (!value || getFieldValue(name) === value ? Promise.resolve() : Promise.reject(new Error('The passwords do not match'))),
        }),
      ]}
    >
      <Input.Password prefix={<LockOutlined />} autoComplete="new-password" size="large" />
    </Form.Item>
  )
}

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const onFinish = async (values: { tenantCode: string; email: string }) => {
    setSubmitting(true)
    setError(null)
    try {
      await authApi.forgotPassword({ tenantCode: values.tenantCode.trim(), email: values.email.trim() })
      setSent(true)
    } catch (e) {
      setError(toApiError(e).message)
    } finally {
      setSubmitting(false)
    }
  }

  if (sent) {
    return (
      <AuthShell title="Check your email">
        <Result
          status="success"
          icon={<CheckCircleOutlined />}
          title="If that account exists, we've sent a link"
          subTitle="The link lets you choose a new password and works once for the next hour."
          extra={<Link to="/login"><Button type="primary">Back to sign in</Button></Link>}
          style={{ padding: 0 }}
        />
      </AuthShell>
    )
  }

  return (
    <AuthShell title="Forgot your password?" subtitle="Enter your organisation code and email and we'll send you a link to choose a new one.">
      {error && <Alert type="error" showIcon title={error} style={{ marginBottom: 16 }} role="alert" />}
      <Form layout="vertical" requiredMark={false} onFinish={onFinish}>
        <Form.Item label="Organisation code" name="tenantCode" rules={[{ required: true, message: 'Enter your organisation code' }]}>
          <Input size="large" autoComplete="organization" />
        </Form.Item>
        <Form.Item label="Email" name="email" rules={[{ required: true, message: 'Enter your email' }, { type: 'email', message: 'Enter a valid email' }]}>
          <Input size="large" autoComplete="username" />
        </Form.Item>
        <Button type="primary" htmlType="submit" size="large" block loading={submitting}>Send reset link</Button>
        <Flex justify="center" style={{ marginTop: 16 }}><Link to="/login">Back to sign in</Link></Flex>
      </Form>
    </AuthShell>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [form] = Form.useForm<{ newPassword: string; confirm: string }>()
  const [done, setDone] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!token) {
    return (
      <AuthShell title="Reset link missing">
        <Result status="warning" title="This link is incomplete" subTitle="Open the link from your email again, or request a new one." extra={<Link to="/forgot-password"><Button type="primary">Request a new link</Button></Link>} style={{ padding: 0 }} />
      </AuthShell>
    )
  }

  if (done) {
    return (
      <AuthShell title="Password updated">
        <Result status="success" title="You can now sign in" subTitle="Any other devices have been signed out." extra={<Link to="/login"><Button type="primary">Go to sign in</Button></Link>} style={{ padding: 0 }} />
      </AuthShell>
    )
  }

  const onFinish = async ({ newPassword }: { newPassword: string }) => {
    setSubmitting(true)
    setError(null)
    try {
      await authApi.resetPassword({ token, newPassword })
      setDone(true)
    } catch (e) {
      const apiError = toApiError(e)
      if (!applyFieldErrors(form, apiError)) setError(apiError.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthShell title="Choose a new password" subtitle={PASSWORD_HINT}>
      {error && (
        <Alert type="error" showIcon title={error} style={{ marginBottom: 16 }} role="alert" action={<Link to="/forgot-password">New link</Link>} />
      )}
      <Form form={form} layout="vertical" requiredMark={false} onFinish={onFinish}>
        <Form.Item label="New password" name="newPassword" rules={passwordRules}>
          <Input.Password prefix={<LockOutlined />} autoComplete="new-password" size="large" autoFocus />
        </Form.Item>
        <ConfirmField />
        <Button type="primary" htmlType="submit" size="large" block loading={submitting}>Set password</Button>
      </Form>
    </AuthShell>
  )
}

/** Used both when an administrator-set password must be replaced and when a signed-in user chooses to change theirs. */
export function ChangePasswordPage() {
  const { user, acceptAuth, logout } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [form] = Form.useForm<{ currentPassword: string; newPassword: string; confirm: string }>()
  const [submitting, setSubmitting] = useState(false)
  const forced = user?.mustChangePassword === true

  const onFinish = async ({ currentPassword, newPassword }: { currentPassword: string; newPassword: string }) => {
    setSubmitting(true)
    try {
      acceptAuth(await authApi.changePassword({ currentPassword, newPassword }))
      void message.success('Password changed. Other devices were signed out.')
      navigate('/', { replace: true })
    } catch (e) {
      const apiError = toApiError(e)
      if (!applyFieldErrors(form, apiError)) void message.error(apiError.message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <AuthShell
      title={forced ? 'Choose your own password' : 'Change password'}
      subtitle={forced ? 'Your administrator set a temporary password. Choose a new one to continue.' : PASSWORD_HINT}
    >
      <Form form={form} layout="vertical" requiredMark={false} onFinish={onFinish}>
        <Form.Item label="Current password" name="currentPassword" rules={[{ required: true, message: 'Enter your current password' }]}>
          <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" autoFocus />
        </Form.Item>
        <Form.Item label="New password" name="newPassword" rules={passwordRules} extra={forced ? PASSWORD_HINT : undefined}>
          <Input.Password prefix={<LockOutlined />} autoComplete="new-password" size="large" />
        </Form.Item>
        <ConfirmField />
        <Button type="primary" htmlType="submit" size="large" block loading={submitting}>Change password</Button>
        <Flex justify="center" style={{ marginTop: 16 }}>
          {forced ? <Button type="link" onClick={() => void logout()}>Sign out</Button> : <Link to="/">Cancel</Link>}
        </Flex>
      </Form>
    </AuthShell>
  )
}
