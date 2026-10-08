import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Descriptions, Form, InputNumber, Radio, Select, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { usersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import type { DataScopeDto, KpiDefinitionDto, ReportSettings } from './types'

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

function DataTab() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: queryKeys.reports.settings, queryFn: reportsApi.settings })
  const source = useQuery({ queryKey: queryKeys.reports.dataSource, queryFn: reportsApi.dataSource })
  const [form] = Form.useForm<ReportSettings>()
  useEffect(() => {
    if (settings.data) form.setFieldsValue(settings.data.settings)
  }, [settings.data, form])
  const save = useMutation({
    mutationFn: (values: ReportSettings) => reportsApi.saveSettings({ ...settings.data!.settings, ...values }, settings.data!.version),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.reports.all })
      void message.success('Report settings saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const rebuild = useMutation({ mutationFn: () => reportsApi.rebuildSummary(90), onSuccess: (n) => void message.success(`Rebuilt ${n.toLocaleString('en-IN')} daily values`), onError: (e) => void message.error(toApiError(e).message) })
  if (settings.isError) return <Alert type="error" showIcon message={toApiError(settings.error).message} />
  return (
    <Form<ReportSettings> form={form} layout="vertical" onFinish={(v) => save.mutate(v)} style={{ maxWidth: 760 }}>
      <Card size="small" title="Where the figures come from" style={{ marginBottom: 16 }}>
        <Form.Item name="dataSource" label="Data" extra="Live reads your transactions through each module. Demo uses the built-in demonstration dataset, clearly marked on every report.">
          <Radio.Group optionType="button" buttonStyle="solid" options={[{ value: 'Live', label: 'Live data' }, { value: 'Demo', label: 'Demonstration data' }]} />
        </Form.Item>
        {source.data && (
          <Descriptions size="small" column={1} bordered>
            {Object.entries(source.data.providers).map(([k, v]) => (
              <Descriptions.Item key={k} label={k}>
                <Tag color={v === 'Live' ? 'success' : 'warning'}>{v}</Tag>
              </Descriptions.Item>
            ))}
          </Descriptions>
        )}
      </Card>
      <Card size="small" title="Measures" style={{ marginBottom: 16 }}>
        <Space wrap size="large">
          <Form.Item name="otpGraceMinutes" label="OTP grace (minutes)"><InputNumber min={0} max={600} /></Form.Item>
          <Form.Item name="otdGraceMinutes" label="OTD grace (minutes)"><InputNumber min={0} max={600} /></Form.Item>
          <Form.Item name="etaToleranceMinutes" label="ETA accurate within (minutes)"><InputNumber min={1} max={600} /></Form.Item>
          <Form.Item name="classificationMethod" label="Cost / performance dividing line"><Select style={{ width: 140 }} options={[{ value: 'Median', label: 'Median' }, { value: 'Average', label: 'Average' }]} /></Form.Item>
          <Form.Item name="defaultPeriodDays" label="Default period (days)"><InputNumber min={1} max={365} /></Form.Item>
          <Form.Item name="topN" label="Top N in charts"><InputNumber min={3} max={50} /></Form.Item>
        </Space>
      </Card>
      <Card size="small" title="Ageing and the business calendar" style={{ marginBottom: 16 }}>
        <Form.Item name="ageingBuckets" label="Ageing buckets (upper bound in days)" extra="For example 1, 3, 7, 15, 30 gives 0–1, 2–3, 4–7, 8–15, 16–30 and more than 30 days.">
          <Select mode="tags" open={false} tokenSeparators={[',', ' ']} />
        </Form.Item>
        <Form.Item name="ageingUsesWorkingDays" label="Count ageing in working days" valuePropName="checked"><Switch /></Form.Item>
        <Form.Item name="workingDays" label="Working days"><Select mode="multiple" options={WEEKDAYS.map((d, i) => ({ value: i, label: d }))} /></Form.Item>
        <Form.Item name="holidays" label="Holidays (yyyy-MM-dd)"><Select mode="tags" open={false} tokenSeparators={[',', ' ']} /></Form.Item>
      </Card>
      <Card size="small" title="Exports and caching" style={{ marginBottom: 16 }}>
        <Space wrap size="large">
          <Form.Item name="syncExportRows" label="Export while you wait up to (rows)"><InputNumber min={100} max={100000} step={500} /></Form.Item>
          <Form.Item name="exportMaxRows" label="Largest export (rows)"><InputNumber min={1000} step={10000} /></Form.Item>
          <Form.Item name="jobKeepHours" label="Keep exported files (hours)"><InputNumber min={1} max={720} /></Form.Item>
          <Form.Item name="dashboardCacheSeconds" label="Dashboard cache (seconds)"><InputNumber min={0} max={3600} /></Form.Item>
          <Form.Item name="controlTowerCacheSeconds" label="Control tower cache (seconds)"><InputNumber min={0} max={600} /></Form.Item>
          <Form.Item name="slowReportMs" label="Warn when a report takes longer than (ms)"><InputNumber min={100} step={500} /></Form.Item>
        </Space>
      </Card>
      <Space>
        <Button type="primary" htmlType="submit" loading={save.isPending}>Save settings</Button>
        <Button loading={rebuild.isPending} onClick={() => rebuild.mutate()}>Rebuild daily KPI values (90 days)</Button>
      </Space>
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, fontSize: 12 }}>Changing the data source or the region map clears the kept daily values so a trend is never read from the other data.</Typography.Paragraph>
    </Form>
  )
}

function KpiTab() {
  const q = useQuery({ queryKey: queryKeys.reports.kpis, queryFn: reportsApi.kpis })
  return (
    <Table<KpiDefinitionDto>
      size="small"
      rowKey="kpiCode"
      loading={q.isLoading}
      dataSource={q.data ?? []}
      pagination={{ pageSize: 15 }}
      columns={[
        { title: 'KPI', dataIndex: 'kpiName', render: (v: string, k) => <Space orientation="vertical" size={0}><strong>{v}</strong><Typography.Text type="secondary" style={{ fontSize: 12 }}>{k.kpiCode}</Typography.Text></Space> },
        { title: 'Formula', dataIndex: 'formula' },
        { title: 'Numerator', dataIndex: 'numerator' },
        { title: 'Denominator', dataIndex: 'denominator' },
        { title: 'Source of truth', dataIndex: 'sourceOfTruth' },
        { title: 'Version', dataIndex: 'calculationVersion', width: 80 },
      ]}
    />
  )
}

function ScopeTab() {
  const { message } = App.useApp()
  const [search, setSearch] = useState('')
  const [userId, setUserId] = useState<string | undefined>()
  const users = useQuery({ queryKey: ['reports', 'scope-users', search], queryFn: () => usersApi.lookup(search) })
  const scopes = useQuery({ queryKey: ['reports', 'scopes', userId], queryFn: () => reportsApi.scopes(userId), enabled: Boolean(userId) })
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: (body: DataScopeDto) => reportsApi.saveScope(body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['reports', 'scopes'] })
      void message.success('Saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const current = (dimension: string) => scopes.data?.find((s) => s.dimension === dimension)?.values ?? []
  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%', maxWidth: 640 }}>
      <Alert type="info" showIcon message="Limit what a staff user can see in reports" description="A user with no limit on a dimension sees everything on it. Reports whose rows do not carry the dimension are not shown to a limited user. A transporter's own limit is automatic." />
      <Select
        showSearch
        filterOption={false}
        placeholder="Choose a user"
        style={{ width: '100%' }}
        onSearch={setSearch}
        onChange={(v) => setUserId(v)}
        options={(users.data ?? []).map((u) => ({ value: u.id, label: `${u.fullName} (${u.email})` }))}
        loading={users.isLoading}
      />
      {userId &&
        ['Customer', 'Region', 'BusinessUnit'].map((dimension) => (
          <Card key={dimension} size="small" title={dimension === 'BusinessUnit' ? 'Business unit' : dimension}>
            <Select
              key={`${userId}-${dimension}-${current(dimension).join('|')}`}
              mode="tags"
              style={{ width: '100%' }}
              defaultValue={current(dimension)}
              placeholder="All (no limit)"
              onBlur={() => undefined}
              onChange={(values: string[]) => save.mutate({ userId, dimension, values })}
              tokenSeparators={[',']}
            />
          </Card>
        ))}
    </Space>
  )
}

/** Reports administration: data source and thresholds, KPI definitions and who is limited to which customers, regions or business units. */
export function ReportSettingsPage() {
  return (
    <>
      <PageHeader title="Report settings" description="Thresholds and choices the reports use. Nothing here is hard-coded in a report." />
      <Tabs
        items={[
          { key: 'data', label: 'Data & thresholds', children: <DataTab /> },
          { key: 'kpis', label: 'KPI definitions', children: <KpiTab /> },
          { key: 'scopes', label: 'Data limits', children: <ScopeTab /> },
        ]}
      />
    </>
  )
}
