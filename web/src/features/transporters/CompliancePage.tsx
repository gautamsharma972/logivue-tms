import { useQuery } from '@tanstack/react-query'
import { Alert, Card, Flex, Select, Table, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { transportersApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ComplianceItemDto } from '@/lib/api/types'
import { ExpiryTag } from './tags'

const windows = [
  { value: 0, label: 'Already expired' },
  { value: 15, label: 'Expired or expiring in 15 days' },
  { value: 30, label: 'Expired or expiring in 30 days' },
  { value: 60, label: 'Expired or expiring in 60 days' },
  { value: 90, label: 'Expired or expiring in 90 days' },
]

export function CompliancePage() {
  const [withinDays, setWithinDays] = useState(30)
  const [page, setPage] = useState(1)
  const report = useQuery({ queryKey: queryKeys.transporters.compliance(withinDays, page), queryFn: () => transportersApi.compliance({ withinDays, page, pageSize: 25 }), placeholderData: (p) => p })

  const columns: TableColumnsType<ComplianceItemDto> = [
    { title: 'Status', key: 'status', width: 150, render: (_, i) => <ExpiryTag status={i.document.expiryStatus} /> },
    { title: 'Valid until', key: 'until', render: (_, i) => i.document.expiresOn },
    { title: 'Paper', key: 'paper', render: (_, i) => <><Typography.Text strong>{i.document.kindLabel}</Typography.Text><br /><Typography.Text type="secondary">{i.ownerLabel}</Typography.Text></> },
    { title: 'Transporter', key: 'transporter', render: (_, i) => <Link to={`/transporters/${i.document.transporterId}`}>{i.transporterName}</Link> },
  ]

  return (
    <>
      <PageHeader title="Compliance watchlist" description="Insurance, fitness, permits and licences that have lapsed or are about to — the renewals to chase." />
      <Card styles={{ body: { padding: 0 } }}>
        <Flex style={{ padding: 16 }}>
          <Select style={{ width: 280 }} value={withinDays} options={windows} onChange={(v) => { setWithinDays(v); setPage(1) }} />
        </Flex>
        {report.isError && <Alert type="error" showIcon title={report.error.message} style={{ margin: '0 16px 16px' }} />}
        <Table<ComplianceItemDto>
          rowKey={(i) => i.document.id}
          columns={columns}
          dataSource={report.data?.items}
          loading={report.isFetching}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: 'Nothing is expiring in this period' }}
          pagination={{ current: page, pageSize: 25, total: report.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false, hideOnSinglePage: true }}
        />
      </Card>
    </>
  )
}
