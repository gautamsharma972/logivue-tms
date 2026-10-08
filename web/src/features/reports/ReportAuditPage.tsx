import { useQuery } from '@tanstack/react-query'
import { Alert, Select, Space, Table, Tag } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import type { AuditEntryDto } from './types'

const ACTIONS = ['Viewed', 'Executed', 'ExportRequested', 'Exported', 'Downloaded', 'ExportDenied', 'SubscriptionCreated', 'SubscriptionChanged', 'SubscriptionDeleted', 'ScheduledRunQueued', 'DefinitionChanged', 'SettingsChanged', 'ScopeChanged']

/** Who viewed, ran, exported, downloaded or scheduled which report, and when. */
export function ReportAuditPage() {
  const [action, setAction] = useState<string | undefined>()
  const [page, setPage] = useState(1)
  const params = { action, page, pageSize: 50 }
  const q = useQuery({ queryKey: queryKeys.reports.audit(params), queryFn: () => reportsApi.audit(params), placeholderData: (p) => p })
  return (
    <>
      <PageHeader title="Report audit" description="Every view, run, export, download and schedule change, with who did it and when." actions={<Select allowClear placeholder="All actions" style={{ width: 220 }} value={action} onChange={(v) => { setAction(v); setPage(1) }} options={ACTIONS.map((a) => ({ value: a, label: a }))} />} />
      {q.isError && <Alert type="error" showIcon message={toApiError(q.error).message} style={{ marginBottom: 12 }} />}
      <Table<AuditEntryDto>
        size="small"
        rowKey="id"
        loading={q.isFetching}
        dataSource={q.data?.items ?? []}
        pagination={{ current: page, pageSize: 50, total: q.data?.totalCount ?? 0, onChange: setPage, showSizeChanger: false }}
        columns={[
          { title: 'When', dataIndex: 'performedAtUtc', render: (v: string) => dayjs(v).format('DD MMM YYYY, HH:mm:ss') },
          { title: 'Who', dataIndex: 'userName', render: (v: string | null) => v ?? '—' },
          { title: 'Action', dataIndex: 'action', render: (v: string) => <Tag>{v}</Tag> },
          { title: 'Report', dataIndex: 'reportCode' },
          { title: 'Format', dataIndex: 'exportFormat', render: (v: string | null) => v?.toUpperCase() ?? '' },
          { title: 'Rows', dataIndex: 'rowCount', align: 'right', render: (v: number | null) => v?.toLocaleString('en-IN') ?? '' },
          { title: 'Took', dataIndex: 'durationMs', align: 'right', render: (v: number | null) => (v === null ? '' : `${v} ms`) },
          { title: 'Filters', dataIndex: 'parameters', ellipsis: true, render: (v: string | null) => <Space>{v ?? ''}</Space> },
        ]}
      />
    </>
  )
}
