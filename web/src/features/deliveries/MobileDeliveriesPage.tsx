import { CheckCircleFilled, ExclamationCircleFilled, SyncOutlined, WifiOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Flex, Tag, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { formatDateTime } from '@/lib/format'
import { useOffline } from './offline/OfflineProvider'
import { localStatus } from './offline/sync'
import { DeliveryStatusTag } from './shared'

/** The sync state the driver always sees: saved on the phone, waiting for a signal, sent, or failed. Never colour alone. */
export function SyncBanner() {
  const { pending, online, busy, sync, commands } = useOffline()
  const problems = commands.filter((c) => c.status === 'Failed' || c.status === 'Conflict').length
  return (
    <Alert
      type={problems > 0 ? 'error' : pending > 0 || !online ? 'warning' : 'success'}
      showIcon
      icon={problems > 0 ? <ExclamationCircleFilled /> : pending > 0 ? <SyncOutlined spin={busy} /> : online ? <CheckCircleFilled /> : <WifiOutlined />}
      title={problems > 0 ? `${problems} change${problems === 1 ? '' : 's'} could not be sent` : pending > 0 ? `⟳ ${pending} change${pending === 1 ? '' : 's'} saved on this phone, waiting to be sent` : online ? '✓ Everything is synced' : 'No signal: your work is saved on this phone'}
      action={<Button size="small" loading={busy} disabled={!online && pending === 0} onClick={() => void sync()}>Sync now</Button>}
    />
  )
}

export function MobileDeliveriesPage() {
  const { bundle, commands } = useOffline()
  const deliveries = bundle?.deliveries ?? []

  return (
    <Flex vertical gap={12} style={{ maxWidth: 560, margin: '0 auto' }}>
      <Typography.Title level={3} style={{ margin: 0 }}>My deliveries</Typography.Title>
      <SyncBanner />
      {bundle && <Typography.Text type="secondary">Downloaded {formatDateTime(bundle.downloadedAt)}</Typography.Text>}
      {deliveries.length === 0 && <Empty description={bundle ? 'Nothing to deliver right now' : 'Connect once to download your deliveries'} />}
      {deliveries.map(({ delivery }) => {
        const mine = commands.filter((c) => c.deliveryId === delivery.summary.id)
        const waiting = mine.filter((c) => c.status === 'Pending').length
        const trouble = mine.filter((c) => c.status === 'Failed' || c.status === 'Conflict').length
        return (
          <Link key={delivery.summary.id} to={`/driver/${delivery.summary.id}`} style={{ color: 'inherit' }}>
            <Card size="small" hoverable>
              <Flex justify="space-between" align="start" gap={8}>
                <div>
                  <Typography.Text strong style={{ fontSize: 16 }}>{delivery.summary.number}</Typography.Text>
                  <br />
                  <Typography.Text>{delivery.summary.customerName}</Typography.Text>
                  <br />
                  <Typography.Text type="secondary">{delivery.destinationAddress ?? delivery.summary.destinationReference ?? ''}</Typography.Text>
                  <br />
                  <Typography.Text type="secondary">Due {formatDateTime(delivery.windowEnd ?? delivery.summary.plannedDeliveryAt)}</Typography.Text>
                </div>
                <Flex vertical gap={4} align="end">
                  <DeliveryStatusTag status={localStatus(delivery.summary.status, mine)} />
                  {waiting > 0 && <Tag color="gold">⟳ Pending sync</Tag>}
                  {trouble > 0 && <Tag color="red">⚠ Sync failed</Tag>}
                  {waiting === 0 && trouble === 0 && mine.length > 0 && <Tag color="green">✓ Synced</Tag>}
                </Flex>
              </Flex>
            </Card>
          </Link>
        )
      })}
    </Flex>
  )
}
