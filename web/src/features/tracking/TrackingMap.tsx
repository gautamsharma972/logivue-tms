import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useRef } from 'react'
import type { TrackRouteDto, TrackedSummaryDto, TrackStopDto } from '@/lib/api/types'
import { healthLabel, pinColour, riskLabel } from './shared'

interface Props {
  /** Vehicles to show as pins (the control tower). */
  vehicles?: TrackedSummaryDto[]
  selectedId?: string | null
  onSelect?: (id: string) => void
  /** A single trip: its road, stops and where it has been (the details page). */
  route?: TrackRouteDto | null
  stops?: TrackStopDto[]
  trail?: [number, number][]
  position?: { latitude: number; longitude: number } | null
  height?: number
  /** Nearby pins are merged into one numbered pin below this zoom. */
  clusterBelowZoom?: number
}

/** Merges pins that would sit on top of each other at the current zoom: a small, dependency-free grid cluster. */
export function clusterPins<T extends { latitude: number; longitude: number }>(items: T[], zoom: number, clusterBelowZoom: number): { items: T[]; lat: number; lon: number }[] {
  if (zoom >= clusterBelowZoom) return items.map((i) => ({ items: [i], lat: i.latitude, lon: i.longitude }))
  const cell = 360 / Math.pow(2, zoom) / 6
  const buckets = new Map<string, T[]>()
  for (const item of items) {
    const key = `${Math.floor(item.latitude / cell)}:${Math.floor(item.longitude / cell)}`
    buckets.set(key, [...(buckets.get(key) ?? []), item])
  }
  return [...buckets.values()].map((group) => ({ items: group, lat: group.reduce((a, g) => a + g.latitude, 0) / group.length, lon: group.reduce((a, g) => a + g.longitude, 0) / group.length }))
}

export default function TrackingMap({ vehicles, selectedId, onSelect, route, stops, trail, position, height = 460, clusterBelowZoom = 7 }: Props) {
  const element = useRef<HTMLDivElement>(null)
  const mapRef = useRef<L.Map | null>(null)
  const layer = useRef<L.LayerGroup | null>(null)
  const fitted = useRef(false)

  useEffect(() => {
    if (!element.current) return
    const map = L.map(element.current, { scrollWheelZoom: true }).setView([21.5, 79], 5)
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, attribution: '© OpenStreetMap contributors' }).addTo(map)
    layer.current = L.layerGroup().addTo(map)
    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
      fitted.current = false
    }
  }, [])

  useEffect(() => {
    const map = mapRef.current
    const group = layer.current
    if (!map || !group) return
    const draw = () => {
      group.clearLayers()
      const bounds: L.LatLng[] = []
      if (route && route.points.length > 1) {
        const line = route.points.map((p) => L.latLng(p[0]!, p[1]!))
        L.polyline(line, { color: '#1677ff', weight: 4, opacity: 0.7 }).bindTooltip(`Planned route (${route.source === 'Osrm' ? 'by road' : 'straight-line estimate'})`).addTo(group)
        bounds.push(...line)
      }
      for (const d of route?.deviations ?? []) {
        L.circleMarker([d.latitude, d.longitude], { radius: 7, color: '#cf1322', fillColor: '#cf1322', fillOpacity: 0.8 }).bindTooltip(`Off route ${Math.round(d.distanceFromRouteKm * 10) / 10} km, ${d.durationMinutes} min`).addTo(group)
      }
      if (trail && trail.length > 1) {
        L.polyline(trail.map((p) => L.latLng(p[0], p[1])), { color: '#389e0d', weight: 3 }).bindTooltip('Where the vehicle has been').addTo(group)
        bounds.push(...trail.map((p) => L.latLng(p[0], p[1])))
      }
      for (const s of stops ?? []) {
        if (s.latitude == null || s.longitude == null) continue
        L.circle([s.latitude, s.longitude], { radius: s.radiusM, color: s.kind === 'Pickup' ? '#389e0d' : '#722ed1', weight: 1, fillOpacity: 0.08 }).addTo(group)
        L.circleMarker([s.latitude, s.longitude], { radius: 8, color: s.status === 'Departed' || s.status === 'Arrived' ? '#8c8c8c' : s.kind === 'Pickup' ? '#389e0d' : '#722ed1', fillOpacity: 0.9 })
          .bindTooltip(`${s.kind === 'Pickup' ? 'Pickup' : `Drop ${s.sequence - 1}`}: ${s.name} (${s.status})`)
          .addTo(group)
        bounds.push(L.latLng(s.latitude, s.longitude))
      }
      if (position) {
        L.circleMarker([position.latitude, position.longitude], { radius: 9, color: '#fff', weight: 2, fillColor: '#1677ff', fillOpacity: 1 }).bindTooltip('Vehicle').addTo(group)
        bounds.push(L.latLng(position.latitude, position.longitude))
      }
      const pins = (vehicles ?? []).filter((v) => v.latitude != null && v.longitude != null).map((v) => ({ ...v, latitude: v.latitude!, longitude: v.longitude! }))
      for (const cluster of clusterPins(pins, map.getZoom(), clusterBelowZoom)) {
        if (cluster.items.length > 1) {
          L.marker([cluster.lat, cluster.lon], {
            icon: L.divIcon({ className: '', html: `<div style="background:#1677ff;color:#fff;border-radius:50%;width:34px;height:34px;display:flex;align-items:center;justify-content:center;font-weight:600;border:2px solid #fff">${cluster.items.length}</div>`, iconSize: [34, 34] }),
          })
            .on('click', () => map.setView([cluster.lat, cluster.lon], Math.min(map.getZoom() + 2, 18)))
            .addTo(group)
        } else {
          const v = cluster.items[0]!
          const selected = v.id === selectedId
          L.circleMarker([v.latitude, v.longitude], { radius: selected ? 12 : 9, color: selected ? '#000' : '#fff', weight: selected ? 3 : 2, fillColor: pinColour(v.tracking, v.risk), fillOpacity: 1 })
            .bindTooltip(`${v.vehicleReference ?? 'Vehicle'} · ${v.tripReference} · ${healthLabel[v.tracking]}, ${riskLabel[v.risk]}`)
            .on('click', () => onSelect?.(v.id))
            .addTo(group)
        }
        bounds.push(L.latLng(cluster.lat, cluster.lon))
      }
      if (bounds.length > 0 && !fitted.current) {
        map.fitBounds(L.latLngBounds(bounds), { padding: [30, 30], maxZoom: 12 })
        fitted.current = true
      }
    }
    draw()
    map.on('zoomend', draw)
    return () => void map.off('zoomend', draw)
  }, [vehicles, selectedId, onSelect, route, stops, trail, position, clusterBelowZoom])

  return <div ref={element} role="img" aria-label="Tracking map" style={{ height, borderRadius: 8, overflow: 'hidden' }} />
}
