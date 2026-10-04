import { LoadingOutlined } from '@ant-design/icons'
import { Alert, Card, Flex, Spin, Timeline, Typography } from 'antd'
import type { RunDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'

/** Shown while the server calculates a plan: what it is doing now and what it has done, refreshed by the page. */
export function OptimizationProgress({ run }: { run: RunDto }) {
  const log = run.log ?? []
  const latest = log[log.length - 1]
  return (
    <Flex vertical gap={16}>
      <Alert
        type="info"
        showIcon
        icon={<Spin indicator={<LoadingOutlined spin />} size="small" />}
        title="Calculating the plan"
        description={`${latest?.message ?? 'Waiting to start.'} You can leave this page; the plan will be here when it is ready.`}
      />
      <Card size="small" title="Progress">
        <Timeline
          items={log.map((entry, i) => ({
            key: `${entry.at}-${i}`,
            color: i === log.length - 1 ? 'blue' : 'gray',
            content: (
              <>
                <Typography.Text>{entry.message}</Typography.Text> <Typography.Text type="secondary">{formatDateTime(entry.at)}</Typography.Text>
              </>
            ),
          }))}
        />
      </Card>
    </Flex>
  )
}

/** What the planner did, kept with the plan. */
export function PlanningLog({ run }: { run: RunDto }) {
  const log = run.log ?? []
  if (log.length === 0) return null
  return (
    <Card size="small" title="Planning log">
      <Timeline
        items={log.map((entry, i) => ({
          key: `${entry.at}-${i}`,
          content: (
            <>
              <Typography.Text>{entry.message}</Typography.Text> <Typography.Text type="secondary">{formatDateTime(entry.at)}</Typography.Text>
            </>
          ),
        }))}
      />
    </Card>
  )
}
