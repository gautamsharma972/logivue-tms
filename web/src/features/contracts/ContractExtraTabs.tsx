import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Alert, App, Button, Checkbox, Descriptions, Flex, Form, Input, InputNumber, Select, Table, Tag, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { AuditPanel } from '@/features/audit/AuditPanel'
import { useAuth } from '@/features/auth/AuthContext'
import { contractsApi, freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractDto, ContractExtras, ContractType, RateRowDto, SaveContractRequest } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { ImpactCard } from './ContractRenewalPage'
import { ContractStatusTag } from './shared'

const SERVICES: { value: ContractType; label: string }[] = [{ value: 'Ftl', label: 'FTL' }, { value: 'Ptl', label: 'PTL' }, { value: 'Dedicated', label: 'Dedicated vehicle' }]

/** The header terms that sit beside the agreement: currency, business unit, contact, how much notice a renewal needs and which services the contract covers. */
export function ServicesTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const { message } = App.useApp()
  const client = useQueryClient()
  const s = contract.summary
  const extras = contract.extras
  const save = useMutation({
    mutationFn: (v: { currency: string; businessUnit?: string; primaryContact?: string; renewalNoticeDays: number; autoRenewal?: boolean; services: ContractType[] }) => {
      const body: SaveContractRequest = {
        transporterId: s.transporterId, type: s.type, title: s.title, effectiveFrom: s.effectiveFrom, effectiveTo: s.effectiveTo, paymentTermsDays: contract.paymentTermsDays, estimatedAnnualSpend: s.estimatedAnnualSpend,
        ownerUserId: contract.ownerUserId, terms: contract.terms, fuel: contract.fuel, version: contract.version,
        extras: { currency: v.currency, businessUnit: v.businessUnit ?? null, primaryContact: v.primaryContact ?? null, renewalNoticeDays: v.renewalNoticeDays, autoRenewal: !!v.autoRenewal, services: v.services } as ContractExtras,
      }
      return contractsApi.update(s.id, body)
    },
    onSuccess: () => { void client.invalidateQueries({ queryKey: queryKeys.contracts.all }); void message.success('Saved') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  if (!editable) {
    return (
      <Descriptions bordered size="small" column={{ xs: 1, md: 2 }}>
        <Descriptions.Item label="Services">{(s.services ?? [s.type]).map((x) => <Tag key={x}>{x.toUpperCase()}</Tag>)}</Descriptions.Item>
        <Descriptions.Item label="Currency">{extras?.currency ?? 'INR'}</Descriptions.Item>
        <Descriptions.Item label="Business unit">{extras?.businessUnit ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="Primary contact">{extras?.primaryContact ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="Renewal notice">{extras?.renewalNoticeDays ?? 60} days</Descriptions.Item>
        <Descriptions.Item label="Renews automatically">{extras?.autoRenewal ? 'Yes: a renewal draft is prepared at the notice date' : 'No'}</Descriptions.Item>
        <Descriptions.Item label="Calculation version">{contract.calculationVersion ?? '1.0'}</Descriptions.Item>
      </Descriptions>
    )
  }
  return (
    <Form layout="vertical" style={{ maxWidth: 560 }} initialValues={{ currency: extras?.currency ?? 'INR', businessUnit: extras?.businessUnit, primaryContact: extras?.primaryContact, renewalNoticeDays: extras?.renewalNoticeDays ?? 60, autoRenewal: extras?.autoRenewal, services: s.services ?? [s.type] }} onFinish={(v) => save.mutate(v)}>
      <Form.Item name="services" label="Services this contract covers" extra={`${s.type.toUpperCase()} is the contract's own type and always stays.`}><Select mode="multiple" options={SERVICES} /></Form.Item>
      <Flex gap={8}>
        <Form.Item name="currency" label="Currency" style={{ width: 120 }}><Input maxLength={3} /></Form.Item>
        <Form.Item name="businessUnit" label="Business unit" style={{ flex: 1 }}><Input /></Form.Item>
      </Flex>
      <Form.Item name="primaryContact" label="Primary contact"><Input /></Form.Item>
      <Flex gap={16} align="end">
        <Form.Item name="renewalNoticeDays" label="Renewal notice (days)"><InputNumber min={0} max={365} /></Form.Item>
        <Form.Item name="autoRenewal" valuePropName="checked"><Checkbox>Prepare a renewal draft automatically</Checkbox></Form.Item>
      </Flex>
      <Button type="primary" htmlType="submit" loading={save.isPending}>Save</Button>
    </Form>
  )
}

const band = (a: number | null, b: number | null, unit: string) => (a == null && b == null ? '—' : `${a ?? 0}–${b ?? '∞'} ${unit}`)

/** A contract's rates seen as lanes, zones or slabs: the same rates, filtered to the way someone is thinking about them. */
export function RateViewTab({ contractId, kind }: { contractId: string; kind: 'lanes' | 'zones' | 'slabs' }) {
  const rates = useQuery({ queryKey: queryKeys.freight.rates({ contractId, pageSize: 500 }), queryFn: () => freightApi.rates({ contractId, pageSize: 500 }) })
  const all = rates.data?.items ?? []
  const rows = all.filter((r) =>
    kind === 'zones' ? r.origin.kind === 'Zone' || r.destination.kind === 'Zone'
      : kind === 'lanes' ? r.origin.kind !== 'Zone' && r.destination.kind !== 'Zone'
        : r.minWeightKg != null || r.maxWeightKg != null || r.minDistanceKm != null || r.maxDistanceKm != null || r.minVolumeCbm != null || r.maxVolumeCbm != null || ['FLAT_SLAB', 'PROGRESSIVE', 'BASE_EXCESS', 'WEIGHT_SLABS'].includes(r.pricingKind))
  return (
    <Table<RateRowDto> size="small" rowKey="id" loading={rates.isLoading} dataSource={rows} pagination={{ pageSize: 15, hideOnSinglePage: true }} locale={{ emptyText: `This contract has no ${kind}.` }}
      columns={[
        { title: 'Rate', render: (_, r) => <span>{r.code} <Tag>V{r.version}</Tag></span> }, { title: 'Lane', dataIndex: 'lane' }, { title: 'Service', dataIndex: 'service' }, { title: 'Vehicle', dataIndex: 'vehicleTypeName', render: (v: string | null) => v ?? 'Any' },
        ...(kind === 'slabs' ? [{ title: 'Weight', render: (_: unknown, r: RateRowDto) => band(r.minWeightKg, r.maxWeightKg, 'kg') }, { title: 'Distance', render: (_: unknown, r: RateRowDto) => band(r.minDistanceKm, r.maxDistanceKm, 'km') }, { title: 'Volume', render: (_: unknown, r: RateRowDto) => band(r.minVolumeCbm, r.maxVolumeCbm, 'CBM') }] : []),
        { title: 'Rate', dataIndex: 'rateSummary' }, { title: 'Priority', dataIndex: 'priority' }, { title: 'Valid', render: (_, r) => `${r.validFrom} → ${r.validTo}` },
      ]} />
  )
}

/** Every revision of this contract, newest first, and what a draft revision would cost against what was rated. */
export function VersionsTab({ contract }: { contract: ContractDto }) {
  const number = contract.summary.number
  const versions = useQuery({ queryKey: queryKeys.freight.versions(number), queryFn: () => freightApi.versions(number) })
  const impact = useQuery({ queryKey: queryKeys.freight.impact(contract.summary.id), queryFn: () => freightApi.impact(contract.summary.id), enabled: !!contract.revisionOfId && contract.summary.status !== 'Active' })
  return (
    <Flex vertical gap={12}>
      <Typography.Text type="secondary">An approved version never changes. A change is a new version, and a shipment is priced by the version in force on its date.</Typography.Text>
      <Table size="small" rowKey="id" loading={versions.isLoading} dataSource={versions.data ?? []} pagination={false} columns={[
        { title: 'Version', render: (_, v) => <Link to={`/contracts/${v.id}`}>V{v.revision}</Link> }, { title: 'Kind', dataIndex: 'revisionKind' }, { title: 'Status', render: (_, v) => <ContractStatusTag status={v.status} /> },
        { title: 'Effective', render: (_, v) => `${v.effectiveFrom} → ${v.effectiveTo}` }, { title: 'Rates', dataIndex: 'rateCount' }, { title: 'This one', render: (_, v) => (v.id === contract.summary.id ? <Tag color="blue">You are here</Tag> : '') },
      ]} />
      {contract.revisionOfId && contract.summary.status !== 'Active' && <ImpactCard impact={impact.data} loading={impact.isLoading} contractId={contract.summary.id} />}
    </Flex>
  )
}

/** The approval of this contract: where it stands and, for someone the policy allows, a way to decide it from here. */
export function ApprovalsTab({ contract }: { contract: ContractDto }) {
  const { can } = useAuth()
  const { message } = App.useApp()
  const client = useQueryClient()
  const s = contract.summary
  const [comment, setComment] = useState('')
  const decide = useMutation({
    mutationFn: ({ approve, comment }: { approve: boolean; comment: string }) => (approve ? freightApi.approve(s.id, comment || null) : freightApi.reject(s.id, comment)),
    onSuccess: () => { void client.invalidateQueries({ queryKey: queryKeys.contracts.all }); void message.success('Decision recorded') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Flex vertical gap={12}>
      <Descriptions bordered size="small" column={1}>
        <Descriptions.Item label="Status"><ContractStatusTag status={s.status} /></Descriptions.Item>
        <Descriptions.Item label="Approval request">{contract.approvalRequestId ? <Link to="/approvals">Open it in Approvals</Link> : 'None yet: submit the contract first'}</Descriptions.Item>
        <Descriptions.Item label="Approved">{formatDateTime(contract.activatedAt)}</Descriptions.Item>
        <Descriptions.Item label="Estimated annual spend">{formatInrExact(s.estimatedAnnualSpend)} (the amount the approval policy looks at)</Descriptions.Item>
      </Descriptions>
      {s.status === 'PendingApproval' && can('contracts.approve') && (
        <Flex gap={8} wrap>
          <Input aria-label="Comment" placeholder="Comment (required to reject)" style={{ width: 320 }} value={comment} onChange={(e) => setComment(e.target.value)} />
          <Button type="primary" loading={decide.isPending} onClick={() => decide.mutate({ approve: true, comment })}>Approve</Button>
          <Button danger loading={decide.isPending} disabled={comment.trim() === ''} onClick={() => decide.mutate({ approve: false, comment })}>Reject</Button>
        </Flex>
      )}
      {s.status === 'PendingApproval' && !can('contracts.approve') && <Alert type="info" showIcon title="Waiting for an approver" description="The approval policy decides who may approve this contract." />}
    </Flex>
  )
}

export function RatingHistoryTab({ contractId }: { contractId: string }) {
  const ratings = useQuery({ queryKey: queryKeys.freight.ratings({ contractId }), queryFn: () => freightApi.ratings({ contractId, pageSize: 25 }) })
  return (
    <Table size="small" rowKey="id" loading={ratings.isLoading} dataSource={ratings.data?.items ?? []} pagination={{ pageSize: 10, hideOnSinglePage: true }} locale={{ emptyText: 'Nothing has been rated against this contract yet.' }}
      columns={[
        { title: 'Rating', dataIndex: 'reference' }, { title: 'Shipment', dataIndex: 'shipmentReference', render: (v: string | null) => v ?? '—' }, { title: 'Lane', dataIndex: 'lane' }, { title: 'Date', dataIndex: 'shipmentDate' },
        { title: 'Rate', render: (_, r) => (r.rateCode ? `${r.rateCode} V${r.rateVersion}` : '—') }, { title: 'Freight', align: 'right', render: (_, r) => formatInrExact(r.overrideAmount ?? r.totalFreight) },
        { title: 'Kept', dataIndex: 'committed', render: (v: boolean) => (v ? <Tag color="green">Kept</Tag> : <Tag>Not kept</Tag>) },
      ]} />
  )
}

export function AuditTab({ contractId }: { contractId: string }) {
  return <AuditPanel subjects={[{ entityType: 'Contract', entityId: contractId }]} title="Every change to this contract" />
}
