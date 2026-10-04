import { AuditOutlined, CheckSquareOutlined, ContainerOutlined, FileDoneOutlined, FileProtectOutlined, InboxOutlined, SafetyCertificateOutlined, TeamOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Card, Col, Divider, Empty, Flex, Row, Skeleton, Statistic, Tag, Typography } from 'antd'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { approvalsApi, auditApi, contractsApi, deliveryApi, ordersApi, rolesApi, shipmentsApi, usersApi } from '@/lib/api/endpoints'
import { formatDateTime } from '@/lib/format'

export function DashboardPage() {
  const { user, can } = useAuth()

  const users = useQuery({
    queryKey: ['dashboard', 'users'],
    queryFn: () => usersApi.list({ pageSize: 1, isActive: true }),
    enabled: can('users.read'),
  })
  const approvals = useQuery({
    queryKey: ['dashboard', 'approvals'],
    queryFn: () => approvalsApi.requests({ scope: 'inbox', pageSize: 1 }),
  })
  const expiringContracts = useQuery({
    queryKey: ['dashboard', 'expiring-contracts'],
    queryFn: () => contractsApi.expiring(30, 1),
    enabled: can('contracts.read') || can('contracts.manage'),
  })
  const isVendor = user?.transporterId != null
  const openOrders = useQuery({
    queryKey: ['dashboard', 'open-orders'],
    queryFn: () => ordersApi.list({ status: 'Open', pageSize: 1 }),
    enabled: !isVendor && (can('shipments.read') || can('shipments.plan')),
  })
  const awaiting = useQuery({
    queryKey: ['dashboard', 'awaiting-response'],
    queryFn: () => shipmentsApi.list({ status: 'Tendered', pageSize: 1 }),
    enabled: isVendor ? can('shipments.respond') : can('shipments.read') || can('shipments.plan'),
  })
  const podOverdue = useQuery({
    queryKey: ['dashboard', 'pod-overdue'],
    queryFn: () => deliveryApi.queue({ overdueOnly: true, pageSize: 1 }),
    enabled: isVendor ? can('shipments.respond') : can('shipments.read') || can('shipments.plan'),
  })
  const roles = useQuery({ queryKey: ['dashboard', 'roles'], queryFn: rolesApi.list, enabled: can('roles.read') })
  const activity = useQuery({
    queryKey: ['dashboard', 'activity'],
    queryFn: () => auditApi.list({ pageSize: 6 }),
    enabled: can('audit.read'),
  })

  return (
    <>
      <PageHeader title={`Welcome, ${user?.fullName.split(' ')[0] ?? ''}`} description={`${user?.tenantName} · ${user?.roles.join(', ')}`} />

      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12} lg={8}>
          <Card>
            <Statistic title="Waiting for your approval" value={approvals.data?.totalCount} loading={approvals.isLoading} prefix={<CheckSquareOutlined />} />
            <Link to="/approvals">Open approvals</Link>
          </Card>
        </Col>
        {!isVendor && (can('shipments.read') || can('shipments.plan')) && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title="Open orders to plan" value={openOrders.data?.totalCount} loading={openOrders.isLoading} prefix={<InboxOutlined />} />
              <Link to="/planning">Open planning board</Link>
            </Card>
          </Col>
        )}
        {(isVendor ? can('shipments.respond') : can('shipments.read') || can('shipments.plan')) && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title={isVendor ? 'Loads waiting for your answer' : 'Offers waiting for a transporter'} value={awaiting.data?.totalCount} loading={awaiting.isLoading} prefix={<ContainerOutlined />} />
              <Link to="/shipments?status=Tendered">{isVendor ? 'Respond to tenders' : 'View shipments'}</Link>
            </Card>
          </Col>
        )}
        {(isVendor ? can('shipments.respond') : can('shipments.read') || can('shipments.plan')) && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title="Proof of delivery overdue (7+ days)" value={podOverdue.data?.totalCount} loading={podOverdue.isLoading} prefix={<FileDoneOutlined />} />
              <Link to="/deliveries">{isVendor ? 'Upload proof' : 'Chase proof'}</Link>
            </Card>
          </Col>
        )}
        {(can('contracts.read') || can('contracts.manage')) && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title="Contracts ending within 30 days" value={expiringContracts.data?.totalCount} loading={expiringContracts.isLoading} prefix={<FileProtectOutlined />} />
              <Link to="/contracts">Review contracts</Link>
            </Card>
          </Col>
        )}
        {can('users.read') && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title="Active users" value={users.data?.totalCount} loading={users.isLoading} prefix={<TeamOutlined />} />
              <Link to="/admin/users">Manage users</Link>
            </Card>
          </Col>
        )}
        {can('roles.read') && (
          <Col xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title="Roles" value={roles.data?.length} loading={roles.isLoading} prefix={<SafetyCertificateOutlined />} />
              <Link to="/admin/roles">Roles & permissions</Link>
            </Card>
          </Col>
        )}
        {can('audit.read') && (
          <Col xs={24} lg={8}>
            <Card>
              <Statistic title="Audited changes" value={activity.data?.totalCount} loading={activity.isLoading} prefix={<AuditOutlined />} />
              <Link to="/admin/audit">View audit trail</Link>
            </Card>
          </Col>
        )}

        {can('audit.read') && (
          <Col span={24}>
            <Card title="Recent activity">
              {activity.isLoading ? (
                <Skeleton active paragraph={{ rows: 4 }} />
              ) : activity.data?.items.length ? (
                activity.data.items.map((item, i) => (
                  <div key={item.id}>
                    {i > 0 && <Divider style={{ margin: '12px 0' }} />}
                    <Flex justify="space-between" wrap gap={8}>
                      <span>
                        <Tag>{item.action}</Tag>
                        {item.entityType} <Typography.Text type="secondary">by {item.userName ?? 'system'}</Typography.Text>
                      </span>
                      <Typography.Text type="secondary">{formatDateTime(item.occurredAt)}</Typography.Text>
                    </Flex>
                  </div>
                ))
              ) : (
                <Empty description="No activity yet" />
              )}
            </Card>
          </Col>
        )}
      </Row>
    </>
  )
}
