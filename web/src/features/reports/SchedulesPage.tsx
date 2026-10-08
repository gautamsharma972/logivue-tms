import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Popconfirm, Space, Switch, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import type { SubscriptionDto } from './types'

/** Reports that are produced on a schedule. Pause one, run it now, or delete it. A schedule stops by itself if its owner loses access to the report. */
export function SchedulesPage() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: queryKeys.reports.subscriptions, queryFn: () => reportsApi.subscriptions(false) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.reports.subscriptions })
  const onError = (e: unknown) => void message.error(toApiError(e).message)
  const toggle = useMutation({
    mutationFn: (s: SubscriptionDto) => reportsApi.updateSubscription(s.id, { reportCode: s.reportCode, name: s.name, filters: s.filters, scheduleType: s.scheduleType, schedule: s.schedule, format: s.format, timeZone: s.timeZone, recipients: s.recipients, active: !s.active, version: s.version }),
    onSuccess: refresh,
    onError,
  })
  const remove = useMutation({ mutationFn: (s: SubscriptionDto) => reportsApi.deleteSubscription(s.id), onSuccess: refresh, onError })
  const now = useMutation({ mutationFn: (s: SubscriptionDto) => reportsApi.runSubscriptionNow(s.id), onSuccess: () => void message.success('Started. You will find the file under My exports.'), onError })

  return (
    <>
      <PageHeader title="Scheduled reports" description="Open any report and choose Schedule to add one here." />
      {list.isError && <Alert type="error" showIcon message={toApiError(list.error).message} style={{ marginBottom: 12 }} />}
      <Table<SubscriptionDto>
        size="small"
        rowKey="id"
        loading={list.isLoading}
        dataSource={list.data ?? []}
        locale={{ emptyText: 'Nothing is scheduled yet.' }}
        columns={[
          { title: 'Name', dataIndex: 'name', render: (v: string, s) => <Link to={`/reports/${s.reportCode}?${new URLSearchParams(s.filters).toString()}`}>{v}</Link> },
          { title: 'When', dataIndex: 'summary' },
          { title: 'Format', dataIndex: 'format', render: (v: string) => v.toUpperCase() },
          { title: 'Next run', dataIndex: 'nextRunAt', render: (v: string | null) => (v ? dayjs(v).format('DD MMM, HH:mm') : <Tag>paused</Tag>) },
          { title: 'Last run', dataIndex: 'lastRunAt', render: (v: string | null) => (v ? dayjs(v).format('DD MMM, HH:mm') : '—') },
          { title: 'Tells', dataIndex: 'recipients', render: (v: string[]) => (v.length === 0 ? '—' : <Typography.Text type="secondary">{v.join(', ')}</Typography.Text>) },
          { title: 'Active', dataIndex: 'active', render: (_: boolean, s) => <Switch checked={s.active} onChange={() => toggle.mutate(s)} aria-label={`${s.active ? 'Pause' : 'Resume'} ${s.name}`} /> },
          {
            title: '',
            key: 'x',
            render: (_, s) => (
              <Space>
                <Button size="small" onClick={() => now.mutate(s)}>Run now</Button>
                <Popconfirm title="Delete this schedule?" onConfirm={() => remove.mutate(s)}>
                  <Button size="small" danger>Delete</Button>
                </Popconfirm>
              </Space>
            ),
          },
        ]}
      />
    </>
  )
}
