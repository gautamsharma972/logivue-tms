import { Outlet } from 'react-router-dom'
import { OfflineProvider } from './offline/OfflineProvider'

/** Everything under /driver shares one copy of the phone's deliveries and its queue of unsent work. */
export function DriverArea() {
  return (
    <OfflineProvider>
      <Outlet />
    </OfflineProvider>
  )
}
