import { ArrowDownOutlined, ArrowUpOutlined, MinusOutlined } from '@ant-design/icons'
import { Card, Tag, Tooltip, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { drillLink } from './drill'
import { formatChange, formatKpi } from './format'
import type { KpiCard } from './types'

const COLOUR = { good: '#16a34a', bad: '#dc2626', neutral: '#6b7280' } as const

interface Props {
  card: KpiCard
  filters: Record<string, string>
  comparison?: boolean
}

/** A KPI with its current value, the period before it and the same period last year, how it moved and where a click goes. Not measurable is shown as such, never as zero. */
export function ReportKpiCard({ card, filters, comparison = true }: Props) {
  const colour = COLOUR[card.assessment]
  const icon = card.trend === 'up' ? <ArrowUpOutlined /> : card.trend === 'down' ? <ArrowDownOutlined /> : <MinusOutlined />
  const link = card.drillReport ? drillLink(card.drillReport, card.drillFilters, null, filters, true) : null
  const detail = card.measurable && card.numerator !== null && card.denominator !== null && card.unit === 'Percent' ? `${card.numerator.toLocaleString('en-IN', { maximumFractionDigits: 1 })} of ${card.denominator.toLocaleString('en-IN', { maximumFractionDigits: 1 })}` : null
  const body = (
    <Card size="small" hoverable={Boolean(link)} style={{ height: '100%' }} data-testid={`kpi-${card.code}`}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {card.name}
      </Typography.Text>
      <div style={{ fontSize: 24, fontWeight: 600, lineHeight: 1.3, color: card.measurable ? undefined : '#9ca3af' }}>{formatKpi(card)}</div>
      {detail && (
        <Tooltip title={`Numerator and denominator behind the percentage (calculation v${card.calculationVersion})`}>
          <Typography.Text type="secondary" style={{ fontSize: 11 }}>
            {detail}
          </Typography.Text>
        </Tooltip>
      )}
      {!card.measurable && card.note && (
        <Typography.Text type="secondary" style={{ fontSize: 11, display: 'block' }}>
          {card.note}
        </Typography.Text>
      )}
      {comparison && card.measurable && (
        <div style={{ marginTop: 6, fontSize: 12 }}>
          {card.change !== null ? (
            <span style={{ color: colour }} aria-label={`${card.trend}, ${card.assessment}`}>
              {icon} {formatChange(card)}
            </span>
          ) : (
            <Tag bordered={false}>no earlier period</Tag>
          )}
          <div style={{ color: '#6b7280' }}>
            {card.previous !== null && <span>prev {formatKpi({ ...card, value: card.previous })}</span>}
            {card.samePeriodLastYear !== null && <span> · last year {formatKpi({ ...card, value: card.samePeriodLastYear })}</span>}
          </div>
        </div>
      )}
    </Card>
  )
  return link ? <Link to={link}>{body}</Link> : body
}
