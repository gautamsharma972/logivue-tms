import { ArrowLeftOutlined, BankOutlined, EditOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, Descriptions, Flex, Input, Modal, Popconfirm, Row, Skeleton, Tabs, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Can, useAuth } from '@/features/auth/AuthContext'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { OwnerKind, TransporterDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { BankModal } from './BankModal'
import { DocumentsTab } from './DocumentsTab'
import { FleetTab } from './FleetTab'
import { CoverageTab } from './performance/CoverageTab'
import { PerformanceTab } from './performance/PerformanceTab'
import { TransporterFormDrawer } from './TransporterFormDrawer'
import { TransporterStatusTag } from './tags'

function Overview({ t, onEditBank }: { t: TransporterDto; onEditBank: () => void }) {
  const { can, user } = useAuth()
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={14}>
        <Card title="Company">
          <Descriptions column={{ xs: 1, md: 2 }} size="small" layout="vertical" colon={false}>
            <Descriptions.Item label="Legal name">{t.legalName}</Descriptions.Item>
            <Descriptions.Item label="Trade name">{t.tradeName ?? '—'}</Descriptions.Item>
            <Descriptions.Item label="PAN">{t.pan}</Descriptions.Item>
            <Descriptions.Item label="GSTIN">{t.gstin ?? 'Not registered'}</Descriptions.Item>
            <Descriptions.Item label="Contact person">{t.contactPerson}</Descriptions.Item>
            <Descriptions.Item label="Mobile">{t.phone}</Descriptions.Item>
            <Descriptions.Item label="Email">{t.email}</Descriptions.Item>
            <Descriptions.Item label="Services">{t.serviceModes.map((m) => <Tag key={m}>{m.toUpperCase()}</Tag>)}</Descriptions.Item>
            <Descriptions.Item label="Address" span={{ xs: 1, md: 2 }}>
              {[t.address.line1, t.address.line2, `${t.address.city}, ${t.address.state} ${t.address.pincode}`].filter(Boolean).join(', ')}
            </Descriptions.Item>
          </Descriptions>
        </Card>
      </Col>
      <Col xs={24} xl={10}>
        <Flex vertical gap={16}>
          {(can('transporters.bank.manage') || t.bank) && user?.transporterId == null && (
            <Card
              title="Bank account"
              extra={
                <Can permission="transporters.bank.manage">
                  {t.status !== 'PendingApproval' && <Button size="small" icon={<BankOutlined />} onClick={onEditBank}>{t.bank ? 'Change' : 'Add'}</Button>}
                </Can>
              }
            >
              {t.bank ? (
                <Descriptions column={1} size="small">
                  <Descriptions.Item label="Account holder">{t.bank.accountHolder}</Descriptions.Item>
                  <Descriptions.Item label="Account number">{t.bank.accountNumberMasked}</Descriptions.Item>
                  <Descriptions.Item label="IFSC">{t.bank.ifsc}</Descriptions.Item>
                  <Descriptions.Item label="Bank">{t.bank.bankName}</Descriptions.Item>
                </Descriptions>
              ) : (
                <Typography.Text type="secondary">No bank details yet.</Typography.Text>
              )}
            </Card>
          )}
          <Card title="Record">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Code">{t.code}</Descriptions.Item>
              <Descriptions.Item label="Created">{formatDateTime(t.createdAt)}</Descriptions.Item>
              <Descriptions.Item label="Activated">{formatDateTime(t.activatedAt)}</Descriptions.Item>
            </Descriptions>
          </Card>
        </Flex>
      </Col>
    </Row>
  )
}

export function TransporterDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { can, user } = useAuth()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [bankOpen, setBankOpen] = useState(false)
  const [suspendOpen, setSuspendOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [tab, setTab] = useState('overview')
  const [uploadOpen, setUploadOpen] = useState(false)
  const [uploadPreset, setUploadPreset] = useState<{ kind: OwnerKind; id: string } | null>(null)

  const isVendor = user?.transporterId != null
  const query = useQuery({ queryKey: queryKeys.transporters.detail(id), queryFn: () => transportersApi.get(id) })
  const t = query.data

  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
  const lifecycle = useMutation({
    mutationFn: (action: 'submit' | 'reactivate' | 'suspend') =>
      action === 'submit' ? transportersApi.submit(id) : action === 'reactivate' ? transportersApi.reactivate(id) : transportersApi.suspend(id, reason.trim()),
    onSuccess: async (_, action) => {
      await refresh()
      setSuspendOpen(false)
      setReason('')
      void message.success(action === 'submit' ? 'Submitted for approval' : action === 'suspend' ? 'Transporter suspended' : 'Transporter reactivated')
    },
    onError: (e) => {
      const error = toApiError(e)
      void message.error(error.fieldErrors.requirements ? `Not ready to submit: ${error.fieldErrors.requirements.join(', ')}` : error.message)
    },
  })

  if (query.isLoading) return <Skeleton active paragraph={{ rows: 10 }} />
  if (query.isError || !t) {
    return <Alert type="error" showIcon title={query.error?.message ?? 'Transporter not found'} action={<Button onClick={() => navigate(isVendor ? '/' : '/transporters')}>Back</Button>} />
  }

  const canManage = can('transporters.manage')
  const canEdit = (canManage || isVendor) && t.status !== 'PendingApproval'
  const incomplete = t.missingForSubmission.length > 0
  const submittable = canManage && (t.status === 'Draft' || t.status === 'Rejected')

  return (
    <>
      {!isVendor && <Link to="/transporters"><Button type="link" icon={<ArrowLeftOutlined />} style={{ paddingLeft: 0 }}>All transporters</Button></Link>}
      <Flex justify="space-between" align="flex-start" wrap gap={12} style={{ marginBottom: 16 }}>
        <div>
          <Flex align="center" gap={12} wrap>
            <Typography.Title level={3} style={{ margin: 0 }}>{t.legalName}</Typography.Title>
            <TransporterStatusTag status={t.status} />
          </Flex>
          <Typography.Text type="secondary">{t.code}{t.tradeName ? ` · ${t.tradeName}` : ''}</Typography.Text>
        </div>
        <Flex gap={8} wrap>
          {canEdit && <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>Edit</Button>}
          {submittable && (
            <Tooltip title={incomplete ? <div>Still needed:{t.missingForSubmission.map((m) => <div key={m}>• {m}</div>)}</div> : undefined}>
              <Popconfirm title="Submit for approval?" description="The record is locked until the decision." okText="Submit" disabled={incomplete} onConfirm={() => lifecycle.mutate('submit')}>
                <Button type="primary" disabled={incomplete} loading={lifecycle.isPending && lifecycle.variables === 'submit'}>Submit for approval</Button>
              </Popconfirm>
            </Tooltip>
          )}
          {canManage && t.status === 'Active' && <Button danger onClick={() => setSuspendOpen(true)}>Suspend</Button>}
          {canManage && t.status === 'Suspended' && <Button type="primary" loading={lifecycle.isPending} onClick={() => lifecycle.mutate('reactivate')}>Reactivate</Button>}
        </Flex>
      </Flex>

      {t.status === 'PendingApproval' && <Alert type="info" showIcon style={{ marginBottom: 16 }} title="Awaiting approval" description={<>This transporter is locked until the onboarding is decided. <Link to="/approvals">Open approvals</Link></>} />}
      {t.status === 'Rejected' && <Alert type="error" showIcon style={{ marginBottom: 16 }} title="Onboarding was rejected" description="Correct the details or documents, then submit again. The reviewer’s reason is in the approval request." />}
      {t.status === 'Suspended' && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="Suspended" description={t.suspensionReason} />}
      {submittable && incomplete && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="Before this can be submitted for approval" description={<ul style={{ margin: 0, paddingLeft: 18 }}>{t.missingForSubmission.map((m) => <li key={m}>{m}</li>)}</ul>} />}

      <Tabs
        activeKey={tab}
        onChange={setTab}
        items={[
          { key: 'overview', label: 'Overview', children: <Overview t={t} onEditBank={() => setBankOpen(true)} /> },
          { key: 'fleet', label: 'Vehicles & drivers', children: <FleetTab transporterId={t.id} onUploadFor={(kind, ownerId) => { setUploadPreset({ kind, id: ownerId }); setUploadOpen(true); setTab('documents') }} /> },
          ...(can('transporters.performance.read') || can('transporters.performance.manage') || can('transporters.performance.self')
            ? [
                { key: 'performance', label: 'Performance', children: <PerformanceTab transporterId={t.id} /> },
                { key: 'coverage', label: 'Coverage & rules', children: <CoverageTab transporterId={t.id} /> },
              ]
            : []),
          { key: 'documents', label: 'Documents', children: <DocumentsTab transporterId={t.id} transporterName={t.legalName} uploadPreset={uploadPreset} uploadOpen={uploadOpen} onUploadOpen={(open) => { setUploadOpen(open); if (!open) setUploadPreset(null) }} /> },
        ]}
      />

      <TransporterFormDrawer open={editing} transporter={t} onClose={() => setEditing(false)} />
      <BankModal transporter={t} open={bankOpen} onClose={() => setBankOpen(false)} />
      <Modal
        open={suspendOpen}
        title="Suspend transporter"
        okText="Suspend"
        okButtonProps={{ danger: true, disabled: reason.trim() === '' }}
        confirmLoading={lifecycle.isPending}
        onCancel={() => setSuspendOpen(false)}
        onOk={() => lifecycle.mutate('suspend')}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">A suspended transporter cannot be given new work. State the reason; it is shown on the record.</Typography.Paragraph>
        <Input.TextArea rows={3} maxLength={500} showCount value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Reason" autoFocus />
      </Modal>
    </>
  )
}
