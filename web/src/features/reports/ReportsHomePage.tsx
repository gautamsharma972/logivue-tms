import { StarFilled, StarOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Col, Empty, Flex, Input, Row, Skeleton, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import type { ReportSummaryDto } from './types'

const ORDER = ['Executive Dashboard', 'Planning & Load Optimisation', 'Transporter Management', 'POD & Delivery', 'Shipment Tracking & Visibility', 'Freight Contract Management', 'Cross-Module Analytics']

/** Every report a person may open, grouped as the business thinks of them, searchable, with favourites on top. */
export function ReportsHomePage() {
  const [text, setText] = useState('')
  const search = useDebouncedValue(text, 250)
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey: queryKeys.reports.catalogue(search), queryFn: () => reportsApi.list(search) })
  const toggle = useMutation({
    mutationFn: async (report: ReportSummaryDto) => {
      const prefs = await reportsApi.preferences()
      return reportsApi.savePreferences({ favourites: report.isFavourite ? prefs.favourites.filter((c) => c !== report.reportCode) : [...prefs.favourites, report.reportCode] })
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.reports.all }),
  })

  const reports = list.data ?? []
  const favourites = reports.filter((r) => r.isFavourite)
  const groups = ORDER.map((category) => ({ category, items: reports.filter((r) => r.category === category) })).filter((g) => g.items.length > 0)

  const card = (r: ReportSummaryDto) => (
    <Col key={r.reportCode} xs={24} md={12} xl={8}>
      <Card
        size="small"
        hoverable
        title={<Link to={`/reports/${r.reportCode}`}>{r.name}</Link>}
        extra={
          <Button
            type="text"
            size="small"
            aria-label={r.isFavourite ? `Remove ${r.name} from favourites` : `Add ${r.name} to favourites`}
            icon={r.isFavourite ? <StarFilled style={{ color: '#eab308' }} /> : <StarOutlined />}
            onClick={() => toggle.mutate(r)}
          />
        }
        style={{ height: '100%' }}
      >
        <Typography.Paragraph type="secondary" style={{ minHeight: 66, fontSize: 13 }} ellipsis={{ rows: 3 }}>
          {r.description}
        </Typography.Paragraph>
        <Flex gap={6} wrap>
          <Tag bordered={false}>{r.reportType}</Tag>
          <Tag bordered={false}>{r.refresh.replace(/([A-Z])/g, ' $1').trim()}</Tag>
          {r.exportFormats.length > 0 && <Tag bordered={false}>{r.exportFormats.join(' · ').toUpperCase()}</Tag>}
        </Flex>
      </Card>
    </Col>
  )

  return (
    <>
      <PageHeader title="All reports" description="Find a report by what it measures. Star the ones you use most." actions={<Input.Search allowClear placeholder="Search reports, e.g. transporter" style={{ width: 300 }} onChange={(e) => setText(e.target.value)} />} />
      {list.isError && <Alert type="error" showIcon message={toApiError(list.error).message} />}
      {list.isLoading && <Skeleton active />}
      {list.data && reports.length === 0 && <Empty description={search ? `No report matches “${search}”.` : 'You do not have access to any report yet.'} />}
      {favourites.length > 0 && (
        <section aria-label="My reports" style={{ marginBottom: 24 }}>
          <Typography.Title level={5}>My reports</Typography.Title>
          <Row gutter={[12, 12]}>{favourites.map(card)}</Row>
        </section>
      )}
      {groups.map((g) => (
        <section key={g.category} aria-label={g.category} style={{ marginBottom: 24 }}>
          <Typography.Title level={5}>
            {g.category} <Typography.Text type="secondary">({g.items.length})</Typography.Text>
          </Typography.Title>
          <Row gutter={[12, 12]}>{g.items.map(card)}</Row>
        </section>
      ))}
    </>
  )
}
