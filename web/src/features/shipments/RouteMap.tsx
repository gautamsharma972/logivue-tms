import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useRef } from 'react'
import type { PlannedStop } from '@/lib/api/types'

/**
 * Stops on an OpenStreetMap base layer (open source). Lines join the stops in order; they are straight lines between stops,
 * not the road geometry. Tiles come from OpenStreetMap's public servers: fine for planning screens, not for heavy traffic.
 */
export default function RouteMap({ stops }: { stops: PlannedStop[] }) {
  const element = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const points = stops.filter((s) => s.latitude !== null && s.longitude !== null).map((s) => ({ ...s, at: L.latLng(s.latitude!, s.longitude!) }))
    if (!element.current || points.length === 0) return
    const map = L.map(element.current, { scrollWheelZoom: false })
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, attribution: '© OpenStreetMap contributors' }).addTo(map)
    L.polyline(points.map((p) => p.at), { color: '#1677ff', weight: 3, dashArray: '6 6' }).addTo(map)
    points.forEach((p) =>
      L.circleMarker(p.at, { radius: 8, color: p.kind === 'Pickup' ? '#389e0d' : '#1677ff', fillOpacity: 0.9 })
        .bindTooltip(`${p.kind === 'Pickup' ? 'Pickup' : `Drop ${p.sequence}`}: ${p.label}`, { permanent: false })
        .addTo(map),
    )
    map.fitBounds(L.latLngBounds(points.map((p) => p.at)), { padding: [30, 30], maxZoom: 10 })
    return () => {
      map.remove()
    }
  }, [stops])

  return <div ref={element} role="img" aria-label="Route map" style={{ height: 280, borderRadius: 8, overflow: 'hidden' }} />
}
