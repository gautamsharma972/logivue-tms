import { ReloadOutlined } from '@ant-design/icons'
import { Button, Flex, Select, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import type { ReportResult } from './types'

const OPTIONS = [
  { value: 0, label: 'Manual' },
  { value: 15, label: 'Every 15 s' },
  { value: 30, label: 'Every 30 s' },
  { value: 60, label: 'Every minute' },
  { value: 300, label: 'Every 5 min' },
]

interface Props {
  result: ReportResult | undefined
  loading: boolean
  seconds: number
  onSeconds: (seconds: number) => void
  onRefresh: () => void
}

/** Refresh now, how old the figures are, and how often to refresh by itself. */
export function ReportRefreshControl({ result, loading, seconds, onSeconds, onRefresh }: Props) {
  const age = result ? result.dataFreshnessSeconds : null
  const fresh = age === null ? '' : age < 5 ? 'just now' : age < 60 ? `${age} s old` : `${Math.round(age / 60)} min old`
  return (
    <Flex align="center" gap={8} wrap>
      {result && (
        <Tooltip title={`Calculation version ${result.calculationVersion} · built in ${result.durationMs} ms${result.fromCache ? ' · from a recent run' : ''}`}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            Last updated {dayjs(result.generatedAtUtc).format('HH:mm')} · data {fresh} · {result.refresh.replace(/([A-Z])/g, ' $1').trim().toLowerCase()}
          </Typography.Text>
        </Tooltip>
      )}
      <Select size="small" value={seconds} onChange={onSeconds} options={OPTIONS} style={{ width: 120 }} aria-label="Refresh interval" />
      <Button icon={<ReloadOutlined />} loading={loading} onClick={onRefresh}>
        Refresh
      </Button>
    </Flex>
  )
}
