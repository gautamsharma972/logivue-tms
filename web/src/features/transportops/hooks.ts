import { useQuery } from '@tanstack/react-query'
import { message } from 'antd'
import { useEffect, useState } from 'react'
import { opsApi } from '@/lib/api/opsApi'
import { toApiError } from '@/lib/api/errors'

export function useOpsError() {
  return (e: unknown) => message.error(toApiError(e).message)
}

/** Opens a file the user is allowed to see: fetched with their token, then shown from a temporary address. */
export function useFileOpener() {
  return async (load: () => Promise<Blob>, name: string) => {
    try {
      const blob = await load()
      const url = URL.createObjectURL(blob)
      const w = window.open(url, '_blank')
      if (!w) { const a = document.createElement('a'); a.href = url; a.download = name; a.click() }
      setTimeout(() => URL.revokeObjectURL(url), 60_000)
    } catch (e) { message.error(toApiError(e).message) }
  }
}

export function useCustomerOptions() {
  const q = useQuery({ queryKey: ['ops', 'customer-options'], queryFn: () => opsApi.customers({ pageSize: 200, status: 'Active' }), staleTime: 60_000 })
  return (q.data?.items ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.code})` }))
}

export function useLocationOptions(kind?: 'Cfa' | 'Distributor', customerId?: string) {
  const q = useQuery({ queryKey: ['ops', 'location-options', kind, customerId], queryFn: () => opsApi.locations({ pageSize: 200, kind, customerId, status: 'Active' }), staleTime: 60_000 })
  return (q.data?.items ?? []).map((l) => ({ value: l.id, label: `${l.name} — ${l.city}`, hasCoordinates: l.latitude != null }))
}

/** A debounced copy of a value, so a search box does not call the server on every keystroke. */
export function useDebounced<T>(value: T, ms = 350): T {
  const [v, setV] = useState(value)
  useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t) }, [value, ms])
  return v
}


/** The units of measure an LR line can use, from the master. */
export function useUomOptions() {
  const q = useQuery({ queryKey: ['ops', 'uom-options'], queryFn: () => opsApi.uoms(true), staleTime: 60_000 })
  return (q.data ?? []).map((u) => ({ value: u.code, label: `${u.code} — ${u.name}` }))
}
