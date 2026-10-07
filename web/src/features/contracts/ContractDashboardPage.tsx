import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Card, Col, Dropdown, Flex, Row, Skeleton, Statistic, Table, Tabs, Tag, Typography } from 'antd'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { freightApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ExpiryItemDto } from '@/lib/api/types'
import { formatInrExact } from '@/lib/format'

function Tile({ title, value, to, warn }: { title: string; value: number; to?: string; warn?: boolean }) {
  const navigate = useNavigate()
  return (
    <Card size="small" hoverable={!!to} onClick={() => to && navigate(to)} aria-label={title}>
      <Statistic title={title} value={value} styles={{ content: warn && value > 0 ? { color: '#cf1322' } : undefined }} />
    </Card>
  )
}

const bandColour = (band: string) => (band === 'Expired' ? 'red' : band.startsWith('7') || band.startsWith('15') ? 'volcano' : band.startsWith('30') ? 'gold' : 'blue')

/** The commercial picture on one page: what is live, what is about to lapse, where no rate exists, what is wrong with the drafts and how the rates are used. */
export function ContractDashboardPage() {
  const dash = useQuery({ queryKey: queryKeys.freight.dashboard, queryFn: freightApi.dashboard })
  const expiry = useQuery({ queryKey: queryKeys.freight.expiry, queryFn: () => freightApi.expiry() })
  const coverage = useQuery({ queryKey: queryKeys.freight.coverage, queryFn: freightApi.coverage })
  const validation = useQuery({ queryKey: queryKeys.freight.validation, queryFn: freightApi.validation })
  const usage = useQuery({ queryKey: queryKeys.freight.usage, queryFn: () => freightApi.usage(12) })
  const d = dash.data
  const reports = useQuery({ queryKey: ['freight', 'reports'], queryFn: freightApi.reports })

  return (
    <>
      <PageHeader title="Freight contracts" description="Contracts, rates and the rating engine. Loads with no applicable contractual rate are the figure to watch: they are what breaks planning and tendering."
        actions={<Dropdown menu={{ items: (reports.data ?? []).map((r) => ({ key: r, label: r.replaceAll('-', ' ') })), onClick: ({ key }) => void freightApi.downloadReport(key, 'xlsx', {}) }}><Button>Download a report</Button></Dropdown>} />
      {!d ? <Skeleton active /> : (
        <>
          <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
            {([
              ['Active contracts', d.active, '/contracts?status=Active'], ['Pending approval', d.pendingApproval, '/contracts?status=PendingApproval'], ['Draft', d.draft, '/contracts?status=Draft'], ['Expiring (60 days)', d.expiringSoon, '/contracts/renewals', true],
              ['Expired', d.expired, '/contracts?status=Expired', true], ['Suspended', d.suspended, '/contracts?status=Suspended', true], ['Active rates', d.activeRates, '/contracts/rates'], ['Rates expiring', d.ratesExpiring, '/contracts/rates', true],
              ['DPH rules in force', d.dphRules, '/contracts/dph'], ['DPH revisions due', d.dphRevisionsDue, '/contracts/dph', true], ['Uncovered lanes', d.uncoveredLanes, '/contracts/ratings?qualified=false', true], ['Rate validation errors', d.validationErrors, undefined, true],
            ] as [string, number, string | undefined, boolean?][]).map(([title, value, to, warn]) => <Col xs={12} md={8} xl={4} key={title}><Tile title={title} value={value} to={to} warn={warn} /></Col>)}
          </Row>
          {d.failedRatings30Days > 0 && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title={`${d.failedRatings30Days} load(s) in the last 30 days had no applicable contractual rate`} description={<Link to="/contracts/ratings">See which</Link>} />}
        </>
      )}
      <Tabs items={[
        {
          key: 'expiry', label: `Expiry (${expiry.data?.items.length ?? 0})`,
          children: (
            <Table<ExpiryItemDto> size="small" rowKey={(r) => r.kind + r.reference + r.contractId} loading={expiry.isLoading} dataSource={expiry.data?.items ?? []} pagination={{ pageSize: 12, hideOnSinglePage: true }} columns={[
              { title: 'What', dataIndex: 'kind' }, { title: 'Reference', dataIndex: 'reference' }, { title: 'Title', dataIndex: 'title', ellipsis: true },
              { title: 'Contract', render: (_, r) => <Link to={`/contracts/${r.contractId}`}>{r.contractNumber}</Link> }, { title: 'Ends', dataIndex: 'expiresOn' },
              { title: 'Left', dataIndex: 'daysLeft', render: (v: number) => (v < 0 ? `${-v} days ago` : `${v} days`) }, { title: 'Band', dataIndex: 'band', render: (b: string) => <Tag color={bandColour(b)}>{b}</Tag> },
            ]} />
          ),
        },
        {
          key: 'coverage', label: 'Rate coverage',
          children: coverage.data && (
            <Flex vertical gap={12}>
              <Row gutter={[12, 12]}>
                <Col xs={12} md={6}><Statistic title="Lanes asked for" value={coverage.data.requiredLanes} /></Col>
                <Col xs={12} md={6}><Statistic title="Covered" value={coverage.data.coveredLanes} /></Col>
                <Col xs={12} md={6}><Statistic title="Uncovered" value={coverage.data.uncoveredLanes} styles={{ content: coverage.data.uncoveredLanes > 0 ? { color: '#cf1322' } : undefined }} /></Col>
                <Col xs={12} md={6}><Statistic title="Covered by a zone or default rate" value={coverage.data.fallbackCovered} /></Col>
                <Col xs={12} md={6}><Statistic title="Active rates" value={coverage.data.activeRates} /></Col>
                <Col xs={12} md={6}><Statistic title="Duplicate rates" value={coverage.data.duplicateRates} /></Col>
                <Col xs={12} md={6}><Statistic title="Overlapping rates" value={coverage.data.overlappingRates} /></Col>
                <Col xs={12} md={6}><Statistic title="Loads without a rate (30 days)" value={coverage.data.loadsWithoutRate} /></Col>
              </Row>
              <Typography.Text type="secondary">Lanes are the ones that have been rated or asked for in the last 90 days.</Typography.Text>
              <Table size="small" rowKey="lane" pagination={{ pageSize: 8, hideOnSinglePage: true }} dataSource={coverage.data.uncovered} locale={{ emptyText: 'Every lane that was asked for has a rate.' }} columns={[
                { title: 'Uncovered lane', dataIndex: 'lane' }, { title: 'Service', dataIndex: 'service' }, { title: 'Asked', dataIndex: 'requests' }, { title: 'Failed', dataIndex: 'failed' }, { title: 'Why', dataIndex: 'reason', render: (v: string | null) => v?.replaceAll('_', ' ') ?? '—' },
              ]} />
            </Flex>
          ),
        },
        {
          key: 'validation', label: `Validation (${validation.data?.errors ?? 0})`,
          children: (
            <Table size="small" rowKey="contractId" loading={validation.isLoading} dataSource={validation.data?.contracts ?? []} locale={{ emptyText: 'No contract being prepared has a rate problem.' }} pagination={{ pageSize: 8, hideOnSinglePage: true }}
              expandable={{ expandedRowRender: (c) => c.issues.map((i) => <div key={i.code + i.message} style={{ color: i.severity === 'Error' ? '#cf1322' : '#d48806' }}>{i.row ? `Rate ${i.row}: ` : ''}{i.message}</div>) }}
              columns={[{ title: 'Contract', render: (_, c) => <Link to={`/contracts/${c.contractId}`}>{c.reference}</Link> }, { title: 'Status', dataIndex: 'status' }, { title: 'Errors', dataIndex: 'errors' }, { title: 'Warnings', dataIndex: 'warnings' }]} />
          ),
        },
        {
          key: 'usage', label: 'Rate usage',
          children: (
            <Table size="small" rowKey={(r) => r.rateCode + r.contractId} loading={usage.isLoading} dataSource={usage.data ?? []} pagination={{ pageSize: 10, hideOnSinglePage: true }} locale={{ emptyText: 'No freight has been rated and kept yet.' }} columns={[
              { title: 'Contract', render: (_, r) => `${r.contractNumber} V${r.contractRevision}` }, { title: 'Rate', render: (_, r) => `${r.rateCode} V${r.rateVersion}` }, { title: 'Lane', dataIndex: 'lane' },
              { title: 'Shipments', dataIndex: 'shipments', align: 'right' }, { title: 'Freight rated', dataIndex: 'totalFreight', align: 'right', render: formatInrExact }, { title: 'Average', dataIndex: 'averageFreight', align: 'right', render: formatInrExact },
            ]} />
          ),
        },
      ]} />
    </>
  )
}
