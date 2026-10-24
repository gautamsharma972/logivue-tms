import { useSearchParams } from 'react-router-dom'
import { LrsPage } from './DispatchPages'

/** LRs, opening the one named in the address (from a request or a link). */
export function LrsRoute() {
  const [params, setParams] = useSearchParams()
  const open = params.get('open') ?? undefined
  return <LrsPage key={open ?? 'list'} openId={open} onOpen={(id) => setParams(id ? { open: id } : {}, { replace: true })} />
}
