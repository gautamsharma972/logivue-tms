import { DownloadOutlined, ReloadOutlined, StopOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Progress, Space, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import type { ReportJobDto } from './types'

const COLOUR: Record<ReportJobDto['status'], string> = { Queued: 'default', Running: 'processing', Completed: 'success', Failed: 'error', Expired: 'default', Cancelled: 'default' }

function size(bytes: number | null): string {
  if (bytes === null) return '—'
  return bytes > 1_048_576 ? `${(bytes / 1_048_576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

/** The exports a person asked for: progress while one is built, then a secure download until it expires. Only they can download their files. */
export function ExportsPage() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const jobs = useQuery({ queryKey: queryKeys.reports.jobs, queryFn: reportsApi.jobs, refetchInterval: (q) => (q.state.data?.some((j) => j.status === 'Queued' || j.status === 'Running') ? 2_000 : 15_000) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.reports.jobs })
  const act = useMutation({ mutationFn: (v: { id: string; what: 'cancel' | 'retry' }) => (v.what === 'cancel' ? reportsApi.cancelJob(v.id) : reportsApi.retryJob(v.id)), onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })
  const download = useMutation({ mutationFn: reportsApi.download, onSuccess: refresh, onError: (e) => void message.error(toApiError(e).message) })

  return (
    <>
      <PageHeader title="My exports" description="Exports you asked for. Large ones are built in the background and kept for a limited time; only you can download them." actions={<Button icon={<ReloadOutlined />} onClick={() => void refresh()}>Refresh</Button>} />
      {jobs.isError && <Alert type="error" showIcon message={toApiError(jobs.error).message} style={{ marginBottom: 12 }} />}
      <Table<ReportJobDto>
        size="small"
        rowKey="id"
        loading={jobs.isLoading}
        dataSource={jobs.data ?? []}
        locale={{ emptyText: 'No exports yet. Use Export on any report.' }}
        columns={[
          { title: 'Reference', dataIndex: 'jobReference', render: (v: string, j) => <Space orientation="vertical" size={0}><Typography.Text code>{v}</Typography.Text>{j.fromSchedule && <Tag bordered={false}>scheduled</Tag>}</Space> },
          { title: 'Report', dataIndex: 'reportName', render: (v: string, j) => <Link to={`/reports/${j.reportCode}`}>{v}</Link> },
          { title: 'Format', dataIndex: 'format', render: (v: string) => v.toUpperCase() },
          {
            title: 'Status',
            dataIndex: 'status',
            render: (v: ReportJobDto['status'], j) => (
              <Space orientation="vertical" size={2}>
                <Tag color={COLOUR[v]}>{v}</Tag>
                {(v === 'Running' || v === 'Queued') && <Progress percent={j.progress} size="small" style={{ width: 120 }} />}
                {j.errorMessage && <Typography.Text type="danger" style={{ fontSize: 12 }}>{j.errorMessage}</Typography.Text>}
              </Space>
            ),
          },
          { title: 'Requested', dataIndex: 'requestedAt', render: (v: string) => dayjs(v).format('DD MMM, HH:mm') },
          { title: 'Rows', dataIndex: 'rowCount', align: 'right', render: (v: number | null) => v?.toLocaleString('en-IN') ?? '—' },
          { title: 'Size', dataIndex: 'sizeBytes', align: 'right', render: (v: number | null) => size(v) },
          { title: 'Kept until', dataIndex: 'expiresAt', render: (v: string | null) => (v ? dayjs(v).format('DD MMM, HH:mm') : '—') },
          {
            title: '',
            key: 'actions',
            render: (_, j) => (
              <Space>
                {j.canDownload && <Button size="small" type="primary" icon={<DownloadOutlined />} loading={download.isPending} onClick={() => download.mutate(j)}>Download</Button>}
                {(j.status === 'Queued' || j.status === 'Running') && <Button size="small" icon={<StopOutlined />} onClick={() => act.mutate({ id: j.id, what: 'cancel' })}>Cancel</Button>}
                {(j.status === 'Failed' || j.status === 'Cancelled') && <Button size="small" onClick={() => act.mutate({ id: j.id, what: 'retry' })}>Try again</Button>}
              </Space>
            ),
          },
        ]}
      />
    </>
  )
}
