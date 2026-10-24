import { Drawer, Tag, type DrawerProps } from 'antd'
import { useIsMobile } from '@/lib/useIsMobile'

const colours: Record<string, string> = {
  Active: 'green', Inactive: 'default', Draft: 'default', Superseded: 'default', Cancelled: 'default',
  Received: 'blue', UnderReview: 'gold', Incomplete: 'red', Validated: 'cyan', ReadyForDispatch: 'green', Converted: 'purple',
  Created: 'default', Assigned: 'blue', AtCfa: 'cyan', Loading: 'gold', Loaded: 'gold', InTransit: 'processing', Arrived: 'purple', UnloadingCompleted: 'green',
  Planned: 'default', Completed: 'green',
  NotYetDue: 'default', Pending: 'gold', Accepted: 'green', Rejected: 'red',
  Calculated: 'blue', OnHold: 'red', Reviewed: 'green', Invoiced: 'purple',
  PendingApproval: 'gold', Issued: 'green',
  NotSubmitted: 'default', Submitted: 'blue', Acknowledged: 'green', ResubmissionRequired: 'red',
  Unpaid: 'default', PartiallyPaid: 'gold', Paid: 'green',
}
const words = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')
export const StatusTag = ({ value }: { value: string }) => <Tag color={colours[value]}>{words(value)}</Tag>

/** A drawer that is the given width on a desktop and the full screen on a phone. */
export function ResponsiveDrawer({ desktopWidth, ...props }: DrawerProps & { desktopWidth: number }) {
  const mobile = useIsMobile()
  return <Drawer {...props} size={mobile ? '100%' : desktopWidth} />
}
