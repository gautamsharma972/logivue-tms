import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Checkbox, Flex, List, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { deliveriesApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import { formatDateTime } from '@/lib/format'

export function NotificationsPage() {
  const queryClient = useQueryClient()
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [page, setPage] = useState(1)
  const params = { unreadOnly: unreadOnly || undefined, page, pageSize: 20 }
  const list = useQuery({ queryKey: queryKeys.deliveries.notifications(params), queryFn: () => deliveriesApi.notifications(params), placeholderData: (p) => p })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
  const readAll = useMutation({ mutationFn: () => deliveriesApi.readAllNotifications(), onSuccess: refresh })
  const readOne = useMutation({ mutationFn: (id: string) => deliveriesApi.readNotification(id), onSuccess: refresh })

  return (
    <>
      <PageHeader title="Delivery notices" description="Proofs that are overdue or sent back, and shortages, damage, refusals and escalations that need someone." actions={<Button onClick={() => readAll.mutate()} loading={readAll.isPending}>Mark all as read</Button>} />
      <Card>
        <Checkbox checked={unreadOnly} onChange={(e) => { setUnreadOnly(e.target.checked); setPage(1) }} style={{ marginBottom: 12 }}>Unread only</Checkbox>
        {list.isError && <Alert type="error" showIcon title={list.error.message} />}
        <List
          loading={list.isLoading} dataSource={list.data?.items} locale={{ emptyText: 'No notices' }}
          pagination={{ current: page, pageSize: 20, total: list.data?.totalCount ?? 0, onChange: setPage, hideOnSinglePage: true }}
          renderItem={(n) => (
            <List.Item actions={n.read ? [] : [<Button key="r" size="small" onClick={() => readOne.mutate(n.id)}>Mark as read</Button>]}>
              <List.Item.Meta
                title={<Flex gap={8} align="center">{!n.read && <Tag color="blue">New</Tag>}{n.podId ? <Link to={`/delivery/pods/${n.podId}`}>{n.title}</Link> : n.deliveryId ? <Link to={`/delivery/${n.deliveryId}`}>{n.title}</Link> : n.title}</Flex>}
                description={<><Typography.Text>{n.body}</Typography.Text><br /><Typography.Text type="secondary">{formatDateTime(n.createdAt)}</Typography.Text></>}
              />
            </List.Item>
          )}
        />
      </Card>
    </>
  )
}
