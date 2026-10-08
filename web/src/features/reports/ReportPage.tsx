import { CalendarOutlined, StarFilled, StarOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Col, Empty, Flex, Result, Row, Select, Skeleton, Tag, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import { drillLink, filtersFromSearch } from './drill'
import { ReportChart } from './ReportChart'
import { ReportExportMenu } from './ReportExportMenu'
import { ReportFilterBar } from './ReportFilterBar'
import { ReportKpiCard } from './ReportKpiCard'
import { ReportRefreshControl } from './ReportRefreshControl'
import { ReportScheduleDialog } from './ReportScheduleDialog'
import { ReportSections, ReportTotals } from './ReportSections'
import { ReportTable } from './ReportTable'
import type { ReportResult } from './types'

/** Any report, drawn from its own metadata: filters, grouping, KPI cards, totals, charts, sections and the paged table, with export, schedule, refresh and drill-down. */
export function ReportPage({ code: fixedCode }: { code?: string }) {
  const params = useParams()
  const code = fixedCode ?? params.code ?? ''
  const [search, setSearch] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [scheduling, setScheduling] = useState(false)
  const [seconds, setSeconds] = useState(0)
  const [nonce, setNonce] = useState(0)

  const filters = useMemo(() => filtersFromSearch(search), [search])
  const groupParam = search.get('groupBy')
  const groupBy = groupParam === null ? undefined : groupParam === 'none' ? [] : groupParam.split(',')
  const sortParam = search.get('sort')
  const sort: { field: string; direction: 'ASC' | 'DESC' } | null = sortParam ? { field: sortParam.split(':')[0] ?? '', direction: sortParam.endsWith(':DESC') ? 'DESC' : 'ASC' } : null
  const page = Number(search.get('page') ?? 1)
  const pageSize = Number(search.get('pageSize') ?? 50)

  const meta = useQuery({ queryKey: queryKeys.reports.metadata(code), queryFn: () => reportsApi.metadata(code), enabled: Boolean(code) })
  const request = { filters, groupBy, sort: sort ? [sort] : undefined, page, pageSize, refresh: nonce > 0 }
  const run = useQuery({
    queryKey: queryKeys.reports.run(code, { ...request, nonce }),
    queryFn: () => reportsApi.run(code, request),
    enabled: meta.isSuccess,
    placeholderData: (previous) => previous,
    refetchInterval: seconds > 0 ? seconds * 1000 : false,
  })
  const favourite = useMutation({
    mutationFn: async () => {
      const prefs = await reportsApi.preferences()
      const next = meta.data?.isFavourite ? prefs.favourites.filter((c) => c !== code) : [...prefs.favourites, code]
      return reportsApi.savePreferences({ favourites: next })
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.reports.all }),
  })

  const update = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(search)
    for (const [k, v] of Object.entries(changes)) {
      if (v === null || v === '') next.delete(k)
      else next.set(k, v)
    }
    setSearch(next, { replace: false })
  }

  if (meta.isError) {
    const error = toApiError(meta.error)
    return <Result status={error.status === 403 ? '403' : '404'} title={error.status === 403 ? 'You cannot open this report' : 'Report not found'} subTitle={error.message} extra={<Link to="/reports/catalogue">All reports</Link>} />
  }
  if (!meta.data) return <Skeleton active />
  const m = meta.data
  const result: ReportResult | undefined = run.data
  const demo = result?.dataSourceMode === 'Demo' || result?.dataSourceMode === 'Mixed'
  const hasTable = (result?.rows.length ?? 0) > 0 || (result?.columns.length ?? 0) > 0 && result?.sections.length === 0 && result?.cards.length === 0
  const onFilters = (values: Record<string, string>) => {
    const next = new URLSearchParams()
    for (const [k, v] of Object.entries(values)) next.set(k, v)
    if (groupParam) next.set('groupBy', groupParam)
    setSearch(next)
  }

  return (
    <>
      <PageHeader
        title={m.name}
        description={m.description}
        actions={
          <>
            <Button aria-label={m.isFavourite ? 'Remove from favourites' : 'Add to favourites'} icon={m.isFavourite ? <StarFilled style={{ color: '#eab308' }} /> : <StarOutlined />} onClick={() => favourite.mutate()} />
            {m.canSchedule && (
              <Button icon={<CalendarOutlined />} onClick={() => setScheduling(true)}>
                Schedule
              </Button>
            )}
            {m.canExport && <ReportExportMenu code={code} formats={m.exportFormats} filters={filters} groupBy={groupBy} sort={sort} />}
          </>
        }
      />
      <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 12 }}>
        <Flex gap={8} align="center" wrap>
          <Tag>{m.category}</Tag>
          <Tag bordered={false}>{m.reportType}</Tag>
          {result && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {result.period.from} to {result.period.to}
            </Typography.Text>
          )}
        </Flex>
        <ReportRefreshControl result={result} loading={run.isFetching} seconds={seconds} onSeconds={setSeconds} onRefresh={() => setNonce((n) => n + 1)} />
      </Flex>
      {demo && <Alert type="info" showIcon style={{ marginBottom: 12 }} message="Showing demonstration data" description="The figures come from the built-in demonstration dataset, not from your transactions. An administrator can switch to live data in Report settings." />}
      {m.note && <Alert type="info" showIcon style={{ marginBottom: 12 }} message={m.note} />}
      <ReportFilterBar key={JSON.stringify(filters)} filters={m.filters} values={filters} onApply={onFilters} />

      {run.isError && <Alert type="error" showIcon style={{ marginBottom: 12 }} message={toApiError(run.error).message} />}
      {!result && run.isLoading && <Skeleton active paragraph={{ rows: 8 }} />}
      {result && (
        <>
          {result.cards.length > 0 && (
            <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
              {result.cards.map((c) => (
                <Col key={c.code} xs={12} md={8} xl={m.reportType === 'Dashboard' ? 4 : 6}>
                  <ReportKpiCard card={c} filters={filters} comparison={m.supportsComparison} />
                </Col>
              ))}
            </Row>
          )}
          <ReportTotals totals={result.totals} filters={filters} />
          {result.charts.length > 0 && (
            <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
              {result.charts.map((c, i) => (
                <Col key={c.id} style={{ minWidth: 0 }} xs={24} xl={c.kind === 'map' || (i === 0 && result.charts.length === 1) ? 24 : 12}>
                  <ReportChart chart={c} onDrill={(row) => c.drillReport && navigate(drillLink(c.drillReport, c.drillMap ?? { [c.xField ?? 'x']: c.xField ?? 'x' }, row, filters))} />
                </Col>
              ))}
            </Row>
          )}
          {result.sections.length > 0 && <ReportSections sections={result.sections} filters={filters} />}
          {hasTable && (
            <>
              <Flex justify="space-between" align="center" wrap gap={8} style={{ marginBottom: 8 }}>
                <Typography.Text strong>{result.groupedBy.length > 0 ? `Grouped by ${result.groupedBy.join(', ')}` : 'Detail'}</Typography.Text>
                {m.availableGrouping.length > 0 && (
                  <Select
                    mode="multiple"
                    allowClear
                    style={{ minWidth: 240 }}
                    placeholder="Group by…"
                    aria-label="Group by"
                    value={groupBy ?? result.groupedBy}
                    onChange={(values) => update({ groupBy: values.length === 0 ? 'none' : values.join(','), page: null })}
                    options={m.availableGrouping.map((g) => ({ value: g.field, label: g.displayName }))}
                    maxTagCount="responsive"
                  />
                )}
              </Flex>
              <ReportTable
                columns={result.columns}
                rows={result.rows}
                total={result.totalRows}
                page={result.page}
                pageSize={result.pageSize}
                drills={result.drills}
                filters={filters}
                sort={sort}
                loading={run.isFetching}
                onChange={(c) => update({ page: c.page === 1 ? null : String(c.page), pageSize: c.pageSize === 50 ? null : String(c.pageSize), sort: c.sort ? `${c.sort.field}:${c.sort.direction}` : null })}
              />
            </>
          )}
          {!hasTable && result.cards.length === 0 && result.sections.length === 0 && result.charts.length === 0 && result.totals.length === 0 && <Empty description="No data found for the selected filters." />}
          {result.notes.length > 0 && (
            <div style={{ marginTop: 12 }}>
              {result.notes.map((n) => (
                <Typography.Paragraph key={n} type="secondary" style={{ fontSize: 12, marginBottom: 4 }}>
                  {n}
                </Typography.Paragraph>
              ))}
            </div>
          )}
        </>
      )}
      {m.canSchedule && <ReportScheduleDialog open={scheduling} onClose={() => setScheduling(false)} code={code} reportName={m.name} formats={m.exportFormats} filters={filters} />}
    </>
  )
}
