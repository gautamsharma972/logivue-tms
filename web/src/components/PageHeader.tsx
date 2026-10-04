import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'

interface PageHeaderProps {
  title: string
  description?: string
  actions?: ReactNode
}

export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <Flex justify="space-between" align="flex-start" wrap gap={12} style={{ marginBottom: 20 }}>
      <div>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {description && (
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
            {description}
          </Typography.Text>
        )}
      </div>
      {actions && <Flex gap={8}>{actions}</Flex>}
    </Flex>
  )
}
