import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Card, Descriptions, Flex, InputNumber, Modal, Statistic, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { contractsApi, freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractSummaryDto, ImpactDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'
import { expiryColor } from './shared'

const stateLabel: Record<string, string> = { RenewalDue: 'Renewal due', ExpiringSoon: 'Expiring soon', RenewalInProgress: 'Renewal in progress', Renewed: 'Renewed', Expired: 'Expired' }
const stateColour: Record<string, string> = { RenewalDue: 'red', ExpiringSoon: 'gold', RenewalInProgress: 'blue', Renewed: 'green', Expired: 'default' }

/** Contracts nearing their end, the next term as a draft with an optional uplift, and what the renewal would cost against what was actually rated. */
export function ContractRenewalPage() {
  const { can } = useAuth()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const client = useQueryClient()
  const [renewing, setRenewing] = useState<ContractSummaryDto | null>(null)
  const [uplift, setUplift] = useState<number | null>(null)
  const [impactOf, setImpactOf] = useState<string | null>(null)
  const expiring = useQuery({ queryKey: queryKeys.contracts.expiring(120), queryFn: () => contractsApi.expiring(120, 100) })
  const renew = useMutation({
    mutationFn: () => freightApi.renew(renewing!.id, { upliftPercent: uplift }),
    onSuccess: (c) => { setRenewing(null); void client.invalidateQueries({ queryKey: queryKeys.contracts.all }); void message.success('Renewal drafted'); navigate(`/contracts/${c.summary.id}`) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const impact = useQuery({ queryKey: queryKeys.freight.impact(impactOf ?? ''), queryFn: () => freightApi.impact(impactOf!), enabled: !!impactOf })
  return (
    <>
      <PageHeader title="Renewals" description="Active contracts ending within 120 days. Renewing copies the contract, with an optional change to every rate, into a draft that is approved before it takes over." />
      <Table<ContractSummaryDto> size="small" rowKey="id" loading={expiring.isLoading} dataSource={expiring.data?.items ?? []} pagination={{ pageSize: 15, hideOnSinglePage: true }} locale={{ emptyText: 'Nothing is due for renewal.' }} columns={[
        { title: 'Contract', render: (_, c) => <Link to={`/contracts/${c.id}`}>{c.reference}</Link> }, { title: 'Transporter', dataIndex: 'transporterName' }, { title: 'Title', dataIndex: 'title', ellipsis: true },
        { title: 'Ends', dataIndex: 'effectiveTo' }, { title: 'Left', dataIndex: 'daysUntilExpiry', render: (v: number | null) => <Tag color={expiryColor(v)}>{v == null ? '—' : v < 0 ? `${-v} days ago` : `${v} days`}</Tag> },
        { title: 'Renewal', dataIndex: 'renewalState', render: (v: string | null | undefined) => (v ? <Tag color={stateColour[v]}>{stateLabel[v] ?? v}</Tag> : '—') },
        { title: 'Spend', dataIndex: 'estimatedAnnualSpend', align: 'right', render: formatInrExact },
        { title: '', render: (_, c) => <Flex gap={4}>{can('contracts.manage') && c.renewalState !== 'RenewalInProgress' && <Button size="small" type="primary" onClick={() => { setRenewing(c); setUplift(null) }}>Start renewal</Button>}{c.renewalState === 'RenewalInProgress' && <Button size="small" onClick={() => navigate(`/contracts/${c.id}`)}>Open</Button>}</Flex> },
      ]} />
      <Modal open={!!renewing} title={renewing ? `Renew ${renewing.reference}` : ''} okText="Draft the renewal" confirmLoading={renew.isPending} onCancel={() => setRenewing(null)} onOk={() => renew.mutate()} destroyOnHidden>
        <Typography.Paragraph type="secondary">The next term starts the day after this one ends. Every rate, DPH rule, charge, commitment and service level is copied; nothing is live until it is approved.</Typography.Paragraph>
        <Flex vertical gap={4}>
          <Typography.Text>Move every rate by (%)</Typography.Text>
          <InputNumber value={uplift} onChange={setUplift} min={-90} max={500} style={{ width: 160 }} placeholder="No change" />
        </Flex>
      </Modal>
      <ImpactCard impact={impact.data} loading={impact.isLoading} contractId={impactOf} setContractId={setImpactOf} />
    </>
  )
}

/** The cost of a renewal draft against what the current contract was actually rated at. Used here and on the contract page. */
export function ImpactCard({ impact, loading, contractId, setContractId }: { impact: ImpactDto | undefined; loading: boolean; contractId: string | null; setContractId?: (id: string | null) => void }) {
  const drafts = useQuery({ queryKey: queryKeys.contracts.list({ status: 'Draft', pageSize: 50 }), queryFn: () => contractsApi.list({ status: 'Draft', pageSize: 50 }) })
  return (
    <Card title="What a renewal would cost" style={{ marginTop: 16 }} loading={loading} extra={setContractId && (
      <select aria-label="Renewal draft" value={contractId ?? ''} onChange={(e) => setContractId(e.target.value || null)}>
        <option value="">Choose a draft revision…</option>
        {(drafts.data?.items ?? []).filter((c) => c.revision > 1).map((c) => <option key={c.id} value={c.id}>{c.reference} · {c.transporterName}</option>)}
      </select>
    )}>
      {!impact ? <Typography.Text type="secondary">Choose a draft renewal or revision to see its effect on the freight that was rated.</Typography.Text> : (
        <Flex vertical gap={12}>
          <Flex gap={32} wrap>
            <Statistic title={`Spend under ${impact.currentReference}`} value={formatInrExact(impact.currentSpend)} />
            <Statistic title={`Under ${impact.proposedReference}`} value={formatInrExact(impact.proposedSpend)} />
            <Statistic title="Change" value={`${impact.variance > 0 ? '+' : ''}${formatInrExact(impact.variance)} (${impact.variancePercent}%)`} styles={{ content: { color: impact.variance > 0 ? '#cf1322' : '#389e0d' } }} />
            <Statistic title="Per year at this volume" value={formatInrExact(impact.annualisedVariance)} />
          </Flex>
          <Descriptions size="small" column={1}><Descriptions.Item label="Basis">{impact.basis}</Descriptions.Item></Descriptions>
          <Table size="small" pagination={false} rowKey="rateCode" dataSource={impact.rows} columns={[
            { title: 'Rate', dataIndex: 'rateCode' }, { title: 'Lane', dataIndex: 'lane' }, { title: 'Now', align: 'right', render: (_, r) => r.currentRate }, { title: 'Proposed', align: 'right', render: (_, r) => r.proposedRate },
            { title: 'Change', align: 'right', render: (_, r) => `${r.changePercent > 0 ? '+' : ''}${r.changePercent}%` }, { title: 'Shipments', align: 'right', dataIndex: 'shipments' }, { title: 'Effect', align: 'right', render: (_, r) => formatInrExact(r.impact) },
          ]} />
        </Flex>
      )}
    </Card>
  )
}
