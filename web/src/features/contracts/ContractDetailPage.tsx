import { ArrowLeftOutlined, EditOutlined, ForkOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Flex, Input, Modal, Popconfirm, Row, Skeleton, Tabs, Tag, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'
import { contractsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractDto, FuelClause } from '@/lib/api/types'
import { formatDateTime, formatInr } from '@/lib/format'
import { ContractDocumentsTab } from './ContractDocumentsTab'
import { ContractFormDrawer } from './ContractFormDrawer'
import { QuotePanel } from './QuotePanel'
import { RatesTab } from './RatesTab'
import { ContractStatusTag, ContractTypeTag, expiryColor, typeLabel } from './shared'

function fuelText(f: FuelClause): string {
  const unit = f.unit === 'Percent' ? '%' : '₹'
  const step = f.unit === 'Percent' ? `${f.stepSize}%` : `₹${f.stepSize}`
  return `Every ${step} change in ${f.region} diesel from ₹${f.basePricePerLitre}/L moves freight ${f.impactPercentPerStep}%` +
    `${f.direction === 'EscalationOnly' ? ' (increases only)' : ''}` +
    `${f.deadBand ? `, after a ${unit === '%' ? `${f.deadBand}%` : `₹${f.deadBand}`} tolerance` : ''}` +
    `${f.capPercent ? `, capped at ±${f.capPercent}%` : ''}`
}

function Overview({ c }: { c: ContractDto }) {
  const s = c.summary
  const t = c.terms
  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} xl={14}>
        <Card title="Agreement">
          <Descriptions column={{ xs: 1, md: 2 }} size="small" layout="vertical" colon={false}>
            <Descriptions.Item label="Transporter"><Link to={`/transporters/${s.transporterId}`}>{s.transporterName}</Link></Descriptions.Item>
            <Descriptions.Item label="Type">{typeLabel[s.type]}</Descriptions.Item>
            <Descriptions.Item label="Valid">{s.effectiveFrom} → {s.effectiveTo}</Descriptions.Item>
            <Descriptions.Item label="Payment terms">{c.paymentTermsDays} days</Descriptions.Item>
            <Descriptions.Item label="Estimated annual spend">{formatInr(s.estimatedAnnualSpend)}</Descriptions.Item>
            <Descriptions.Item label="Owner">{c.ownerName ?? '—'}</Descriptions.Item>
          </Descriptions>
        </Card>
      </Col>
      <Col xs={24} xl={10}>
        <Flex vertical gap={16}>
          <Card title="Commercial terms">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Volumetric factor">{t.volumetricKgPerCbm} kg / CBM</Descriptions.Item>
              <Descriptions.Item label="Minimum per consignment">{formatInr(t.minChargePerConsignment)}</Descriptions.Item>
              <Descriptions.Item label="Loading / unloading">{formatInr(t.loadingCharge)} / {formatInr(t.unloadingCharge)}</Descriptions.Item>
              <Descriptions.Item label="Extra drop point">{formatInr(t.multiDropChargePerPoint)}</Descriptions.Item>
              <Descriptions.Item label="Detention">{t.detentionFreeHours} h free, then {formatInr(t.detentionRatePerHour)}/h</Descriptions.Item>
            </Descriptions>
            {t.notes && <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>{t.notes}</Typography.Paragraph>}
          </Card>
          <Card title="Diesel price variation">
            {c.fuel ? <Typography.Text>{fuelText(c.fuel)}</Typography.Text> : <Typography.Text type="secondary">No diesel clause: rates are fixed.</Typography.Text>}
          </Card>
          <Card title="Record">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="Created">{formatDateTime(c.createdAt)}</Descriptions.Item>
              <Descriptions.Item label="Approved">{formatDateTime(c.activatedAt)}</Descriptions.Item>
            </Descriptions>
          </Card>
        </Flex>
      </Col>
    </Row>
  )
}

export function ContractDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [terminating, setTerminating] = useState(false)
  const [reason, setReason] = useState('')
  const [revising, setRevising] = useState(false)
  const [period, setPeriod] = useState<[Dayjs, Dayjs] | null>(null)

  const query = useQuery({ queryKey: queryKeys.contracts.detail(id), queryFn: () => contractsApi.get(id) })
  const c = query.data

  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.contracts.all })
  const submit = useMutation({
    mutationFn: () => contractsApi.submit(id),
    onSuccess: async () => { await refresh(); void message.success('Submitted for approval') },
    onError: (e) => {
      const error = toApiError(e)
      void message.error(error.fieldErrors.requirements ? `Not ready: ${error.fieldErrors.requirements.join(', ')}` : error.message)
    },
  })
  const terminate = useMutation({
    mutationFn: () => contractsApi.terminate(id, reason.trim()),
    onSuccess: async () => { await refresh(); setTerminating(false); setReason(''); void message.success('Contract terminated') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const revise = useMutation({
    mutationFn: () => contractsApi.revise(id, period![0].format('YYYY-MM-DD'), period![1].format('YYYY-MM-DD')),
    onSuccess: async (revision) => { await refresh(); setRevising(false); void message.success('Revision drafted'); navigate(`/contracts/${revision.summary.id}`) },
    onError: (e) => void message.error(toApiError(e).message),
  })

  if (query.isLoading) return <Skeleton active paragraph={{ rows: 10 }} />
  if (query.isError || !c) return <Alert type="error" showIcon title={query.error?.message ?? 'Contract not found'} action={<Button onClick={() => navigate('/contracts')}>Back</Button>} />

  const s = c.summary
  const manage = can('contracts.manage')
  const editable = manage && (s.status === 'Draft' || s.status === 'Rejected')
  const incomplete = c.missingForSubmission.length > 0
  const days = s.daysUntilExpiry

  return (
    <>
      <Link to="/contracts"><Button type="link" icon={<ArrowLeftOutlined />} style={{ paddingLeft: 0 }}>All contracts</Button></Link>
      <Flex justify="space-between" align="flex-start" wrap gap={12} style={{ marginBottom: 16 }}>
        <div>
          <Flex align="center" gap={12} wrap>
            <Typography.Title level={3} style={{ margin: 0 }}>{s.reference}</Typography.Title>
            <ContractStatusTag status={s.status} />
            <ContractTypeTag type={s.type} />
            {s.status === 'Active' && days !== null && days <= 60 && <Tag color={expiryColor(days)}>{days < 0 ? `ended ${-days} days ago` : days === 0 ? 'ends today' : `ends in ${days} days`}</Tag>}
          </Flex>
          <Typography.Text type="secondary">{s.title} · {s.transporterName}</Typography.Text>
        </div>
        <Flex gap={8} wrap>
          {editable && <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>Edit details</Button>}
          {editable && (
            <Tooltip title={incomplete ? <div>Still needed:{c.missingForSubmission.map((m) => <div key={m}>• {m}</div>)}</div> : undefined}>
              <Popconfirm title="Submit for approval?" description="The contract is locked until it is decided." okText="Submit" disabled={incomplete} onConfirm={() => submit.mutate()}>
                <Button type="primary" disabled={incomplete} loading={submit.isPending}>Submit for approval</Button>
              </Popconfirm>
            </Tooltip>
          )}
          {manage && (s.status === 'Active' || s.status === 'Expired') && (
            <Button icon={<ForkOutlined />} onClick={() => { setPeriod([dayjs(s.status === 'Expired' ? undefined : s.effectiveTo).add(s.status === 'Expired' ? 0 : 1, 'day'), dayjs(s.status === 'Expired' ? undefined : s.effectiveTo).add(s.status === 'Expired' ? 0 : 1, 'day').add(1, 'year').subtract(1, 'day')]); setRevising(true) }}>Revise</Button>
          )}
          {manage && s.status === 'Active' && <Button danger onClick={() => setTerminating(true)}>Terminate</Button>}
        </Flex>
      </Flex>

      {s.status === 'PendingApproval' && <Alert type="info" showIcon style={{ marginBottom: 16 }} title="Awaiting approval" description={<>The contract is locked until it is decided. <Link to="/approvals">Open approvals</Link></>} />}
      {s.status === 'Rejected' && <Alert type="error" showIcon style={{ marginBottom: 16 }} title="Approval was rejected" description="Correct the rates or terms and submit again. The reviewer's reason is in the approval request." />}
      {s.status === 'Terminated' && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="Terminated" description={c.terminationReason} />}
      {s.status === 'Superseded' && <Alert type="info" showIcon style={{ marginBottom: 16 }} title="Replaced by a newer revision" description="This contract still prices shipments dated before the revision took over." />}
      {c.revisionOfId && <Alert type="info" showIcon style={{ marginBottom: 16 }} title="This is a revision" description={<>It replaces <Link to={`/contracts/${c.revisionOfId}`}>the previous version</Link> from {s.effectiveFrom} once approved.</>} />}
      {editable && incomplete && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title="Before this can be submitted" description={<ul style={{ margin: 0, paddingLeft: 18 }}>{c.missingForSubmission.map((m) => <li key={m}>{m}</li>)}</ul>} />}

      <Tabs
        items={[
          { key: 'overview', label: 'Overview', children: <Overview c={c} /> },
          { key: 'rates', label: `Rates (${s.rateCount})`, children: <RatesTab contract={c} editable={editable} /> },
          { key: 'test', label: 'Test rates', children: <QuotePanel contractId={id} contractType={s.type} /> },
          { key: 'documents', label: 'Documents', children: <ContractDocumentsTab contractId={id} /> },
        ]}
      />

      <ContractFormDrawer open={editing} contract={c} onClose={() => setEditing(false)} />
      <Modal open={terminating} title="Terminate contract" okText="Terminate" okButtonProps={{ danger: true, disabled: reason.trim() === '' }} confirmLoading={terminate.isPending} onCancel={() => setTerminating(false)} onOk={() => terminate.mutate()} destroyOnHidden>
        <Typography.Paragraph type="secondary">It stops pricing new shipments from tomorrow. Shipments up to today are still priced and audited against it.</Typography.Paragraph>
        <Input.TextArea rows={3} maxLength={500} showCount value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Reason" autoFocus />
      </Modal>
      <Modal open={revising} title="Revise contract" okText="Draft revision" confirmLoading={revise.isPending} okButtonProps={{ disabled: !period }} onCancel={() => setRevising(false)} onOk={() => revise.mutate()} destroyOnHidden>
        <Typography.Paragraph type="secondary">
          A copy of this contract (every rate, the terms and the diesel clause) is created as a draft for you to change and get approved.
          When approved, it takes over from the start date below and this contract ends the day before.
        </Typography.Paragraph>
        <DatePicker.RangePicker style={{ width: '100%' }} format="D MMM YYYY" value={period} onChange={(v) => setPeriod(v as [Dayjs, Dayjs] | null)} />
      </Modal>
    </>
  )
}
