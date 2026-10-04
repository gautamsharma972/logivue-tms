import { CheckCircleOutlined, ClockCircleOutlined, CloseCircleOutlined, StopOutlined } from '@ant-design/icons'
import { Tag } from 'antd'
import type { ApprovalStatus } from '@/lib/api/types'

const config: Record<ApprovalStatus, { color: string; icon: React.ReactNode; label: string }> = {
  Pending: { color: 'gold', icon: <ClockCircleOutlined />, label: 'Pending' },
  Approved: { color: 'green', icon: <CheckCircleOutlined />, label: 'Approved' },
  Rejected: { color: 'red', icon: <CloseCircleOutlined />, label: 'Rejected' },
  Cancelled: { color: 'default', icon: <StopOutlined />, label: 'Cancelled' },
}

export function StatusTag({ status }: { status: ApprovalStatus }) {
  const { color, icon, label } = config[status]
  return (
    <Tag color={color} icon={icon}>
      {label}
    </Tag>
  )
}
