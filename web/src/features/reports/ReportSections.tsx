import { Card, Col, Descriptions, Row, Statistic, Table, Tag, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { drillLink } from './drill'
import { formatCell, toneOf } from './format'
import type { FieldType, ReportSection, TotalDto } from './types'

const TONE_COLOUR: Record<string, string> = { good: '#16a34a', warn: '#ca8a04', bad: '#dc2626' }

/** Plain totals and counts beside a report. A total that leads somewhere is a link, with the current filters kept. */
export function ReportTotals({ totals, filters }: { totals: TotalDto[]; filters: Record<string, string> }) {
  if (totals.length === 0) return null
  return (
    <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
      {totals.map((t) => {
        const unit = t.unit === 'currency' ? 'Currency' : t.unit === 'percent' ? 'Percent' : t.unit === 'number' ? 'Number' : 'Whole'
        const card = (
          <Card size="small" hoverable={Boolean(t.drillReport)} data-testid={`total-${t.key}`}>
            <Statistic title={t.label} valueRender={() => <span style={{ color: t.tone ? TONE_COLOUR[t.tone] : undefined }}>{formatCell(t.value, unit)}</span>} />
          </Card>
        )
        return (
          <Col key={t.key} xs={12} md={8} xl={4}>
            {t.drillReport ? <Link to={drillLink(t.drillReport, t.drillFilters, null, filters, true)}>{card}</Link> : card}
          </Col>
        )
      })}
    </Row>
  )
}

function itemType(format: string | null): FieldType {
  return format === 'currency' ? 'Currency' : format === 'percent' ? 'Percent' : format === 'number' ? 'Number' : format === 'date' ? 'Date' : format === 'datetime' ? 'DateTime' : 'Text'
}

/** Sections of facts, each from one module, as Shipment 360 reads across them. Every figure that has a report behind it links to it. */
export function ReportSections({ sections, filters }: { sections: ReportSection[]; filters: Record<string, string> }) {
  return (
    <>
      {sections.map((s) => (
        <Card
          key={s.key}
          size="small"
          style={{ marginBottom: 12 }}
          title={s.title}
          extra={s.module ? <Tag bordered={false}>{s.module}</Tag> : null}
          data-testid={`section-${s.key}`}
        >
          {s.items.length > 0 && (
            <Descriptions size="small" column={{ xs: 1, md: 2, xl: 3 }}>
              {s.items.map((i) => (
                <Descriptions.Item key={i.label} label={i.label}>
                  {i.drillReport && i.value !== null ? (
                    <Link to={drillLink(i.drillReport, i.drillFilters, null, filters, true)}>{formatCell(i.value, itemType(i.format))}</Link>
                  ) : s.key === 'answer' && i.label === 'Reading' ? (
                    <Typography.Text strong>{String(i.value)}</Typography.Text>
                  ) : typeof i.value === 'string' && toneOf(i.value) !== 'default' && i.label.toLowerCase().includes('status') ? (
                    <Tag color={toneOf(i.value)}>{i.value}</Tag>
                  ) : (
                    formatCell(i.value, itemType(i.format))
                  )}
                </Descriptions.Item>
              ))}
            </Descriptions>
          )}
          {s.table && s.table.length > 0 && s.tableColumns && (
            <Table
              size="small"
              style={{ marginTop: s.items.length > 0 ? 12 : 0 }}
              pagination={s.table.length > 20 ? { pageSize: 20, showSizeChanger: false } : false}
              rowKey={(_, i) => String(i)}
              dataSource={s.table}
              columns={s.tableColumns.map((c) => ({
                key: c.field,
                dataIndex: c.field,
                title: c.display,
                render: (v: unknown) => formatCell(v as string | number | null, typeof c.type === 'string' ? c.type : 'Text'),
              }))}
            />
          )}
        </Card>
      ))}
    </>
  )
}
