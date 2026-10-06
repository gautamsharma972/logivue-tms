import * as signalR from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { session } from '@/lib/api/session'
import { queryKeys } from '@/lib/api/queryKeys'

export type LiveState = 'live' | 'polling'

/**
 * Pushes from the tracking hub nudge the queries to refresh; if the connection cannot be made (or drops), the page falls back to polling,
 * and says so, so nobody mistakes a quiet screen for a quiet road.
 */
export function useTrackingLive(): { state: LiveState; pollMs: number | false } {
  const client = useQueryClient()
  const [state, setState] = useState<LiveState>('polling')

  useEffect(() => {
    const base = import.meta.env.VITE_API_URL ?? ''
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${base}/hubs/tracking`, { accessTokenFactory: () => session.getAccessToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.None)
      .build()
    let closed = false
    let timer: ReturnType<typeof setTimeout> | null = null
    // Several fixes can arrive in a burst; one refresh covers them.
    const soon = () => {
      timer ??= setTimeout(() => {
        timer = null
        void client.invalidateQueries({ queryKey: queryKeys.tracking.all })
      }, 800)
    }
    for (const kind of ['position', 'alert', 'exception', 'notification']) connection.on(kind, soon)
    connection.onreconnecting(() => setState('polling'))
    connection.onreconnected(() => {
      setState('live')
      soon()
    })
    connection.onclose(() => setState('polling'))
    connection
      .start()
      .then(() => !closed && setState('live'))
      .catch(() => setState('polling'))
    return () => {
      closed = true
      if (timer) clearTimeout(timer)
      void connection.stop()
    }
  }, [client])

  return { state, pollMs: state === 'live' ? false : 20_000 }
}
