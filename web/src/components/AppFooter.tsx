import { Typography } from 'antd'

export const COMPANY = 'Nexspire Tech LLP'
const YEAR = new Date().getFullYear()

export function AppFooter({ style }: { style?: React.CSSProperties }) {
  return (
    <Typography.Text type="secondary" style={{ display: 'block', textAlign: 'center', fontSize: 13, ...style }}>
      © {YEAR} {COMPANY}
    </Typography.Text>
  )
}
