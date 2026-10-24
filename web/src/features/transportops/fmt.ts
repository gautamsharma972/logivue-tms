import dayjs from 'dayjs'
import type { EventType } from '@/lib/api/opsApi'

/** Times are shown in India Standard Time whatever the browser's zone, because that is the time the business works in. */
const ist = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Kolkata' })
export const when = (v: string | null | undefined) => (v ? ist.format(new Date(v)) : '—')
export const day = (v: string | null | undefined) => (v ? dayjs(v).format('DD MMM YYYY') : '—')
export const mins = (m: number | null | undefined) => (m == null ? '—' : m >= 60 ? `${Math.floor(m / 60)} h ${Math.round(m % 60)} min` : `${Math.round(m)} min`)
export const kg = (v: number | null | undefined) => (v == null ? '—' : `${v.toLocaleString('en-IN', { maximumFractionDigits: 2 })} kg`)

export const eventLabel: Record<EventType, string> = {
  CfaReached: 'Vehicle reached the CFA', LoadingStarted: 'Loading started', LoadingCompleted: 'Loading completed', CfaDeparted: 'Vehicle left the CFA', DestinationArrival: 'Arrived at destination',
  UnloadingCompleted: 'Unloading completed', PodUploaded: 'POD uploaded', PodPending: 'POD not available yet', ArrivalRejected: 'Arrival check failed', ArrivalOverridden: 'Arrival recorded by office', Correction: 'Time corrected',
}
