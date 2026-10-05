import { BellOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge, Button, Dropdown, Empty, Flex, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import { deliveriesApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { formatDateTime } from '@/lib/format'

/** The delivery notices waiting for this person. Checked every minute while the app is open. */
export function NotificationBell() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const params = { unreadOnly: true, pageSize: 6 }
  const unread = useQuery({ queryKey: queryKeys.deliveries.notifications(params), queryFn: () => deliveriesApi.notifications(params), refetchInterval: 60_000, retry: false })
  const items = unread.data?.items ?? []

  const open = async (id: string, to: string) => {
    await deliveriesApi.readNotification(id)
    await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
    navigate(to)
  }

  return (
    <Dropdown
      trigger={['click']}
      popupRender={() => (
        <Flex vertical gap={8} style={{ width: 340, padding: 12, background: 'var(--ant-color-bg-elevated, #1f1f1f)', borderRadius: 8, boxShadow: '0 6px 24px rgba(0,0,0,0.3)' }}>
          {items.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nothing new" /> : items.map((n) => (
            <div key={n.id} style={{ cursor: 'pointer' }} onClick={() => void open(n.id, n.podId ? `/delivery/pods/${n.podId}` : n.deliveryId ? `/delivery/${n.deliveryId}` : '/delivery/notifications')}>
              <Typography.Text strong>{n.title}</Typography.Text>
              <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>{n.body}</Typography.Text>
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>{formatDateTime(n.createdAt)}</Typography.Text>
            </div>
          ))}
          <Button size="small" onClick={() => navigate('/delivery/notifications')}>See all</Button>
        </Flex>
      )}
    >
      <Badge count={unread.data?.totalCount ?? 0} size="small" overflowCount={99}>
        <Button type="text" aria-label="Delivery notices" icon={<BellOutlined />} />
      </Badge>
    </Dropdown>
  )
}
