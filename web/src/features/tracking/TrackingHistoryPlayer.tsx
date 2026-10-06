import { CaretRightOutlined, PauseOutlined, ReloadOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Select, Slider, Space, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { ReplayDto, TimelineEntryDto, TrackStopDto } from '@/lib/api/types'
import { frameAt, GAP_MS, SPEEDS, speedLabel } from './replay'

interface Props {
  replay: ReplayDto
  stops?: TrackStopDto[]
  /** Things that happened on the trip (arrivals, deviations, stops): listed under the map so a click jumps to that moment. */
  events?: TimelineEntryDto[]
  height?: number
}

/**
 * Plays a recorded trip back: select, play, pause, replay, choose how fast. Silences in the recording are shown as breaks in the line and named while the
 * clock is inside one, because a vehicle that was not reporting was not necessarily standing still.
 */
export function TrackingHistoryPlayer({ replay, stops, events, height = 460 }: Props) {
  const points = replay.points
  const start = points[0]?.[2] ?? 0
  const end = points[points.length - 1]?.[2] ?? 0
  const [at, setAt] = useState(start)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState<number>(300)
  const element = useRef<HTMLDivElement>(null)
  const marker = useRef<L.CircleMarker | null>(null)
  const done = useRef<L.Polyline | null>(null)
  const stamp = useRef<number | null>(null)

  // a new clock tick every frame while playing
  useEffect(() => {
    if (!playing) {
      stamp.current = null
      return
    }
    let frame = 0
    const tick = (now: number) => {
      const last = stamp.current ?? now
      stamp.current = now
      setAt((current) => {
        const next = current + ((now - last) / 1000) * speed * 1000
        if (next >= end) {
          setPlaying(false)
          return end
        }
        return next
      })
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [playing, speed, end])

  // the map: the whole recorded line (broken at silences), the stops, and the moving vehicle
  useEffect(() => {
    if (!element.current || points.length === 0) return
    const map = L.map(element.current, { scrollWheelZoom: true })
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, attribution: '© OpenStreetMap contributors' }).addTo(map)
    const segments: L.LatLng[][] = [[]]
    points.forEach((p, i) => {
      if (i > 0 && p[2]! - points[i - 1]![2]! > GAP_MS) segments.push([])
      segments[segments.length - 1]!.push(L.latLng(p[0]!, p[1]!))
    })
    segments.forEach((s) => L.polyline(s, { color: '#91caff', weight: 4 }).addTo(map))
    // dashed hint across each silence, so the break is seen as a break rather than as a missing road
    for (let i = 1; i < segments.length; i++) L.polyline([segments[i - 1]!.at(-1)!, segments[i]![0]!], { color: '#cf1322', weight: 2, dashArray: '4 6' }).bindTooltip('No location recorded').addTo(map)
    done.current = L.polyline([], { color: '#1677ff', weight: 5 }).addTo(map)
    marker.current = L.circleMarker(L.latLng(points[0]![0]!, points[0]![1]!), { radius: 9, color: '#fff', weight: 2, fillColor: '#1677ff', fillOpacity: 1 }).addTo(map)
    for (const s of stops ?? []) {
      if (s.latitude == null || s.longitude == null) continue
      L.circleMarker([s.latitude, s.longitude], { radius: 7, color: s.kind === 'Pickup' ? '#389e0d' : '#722ed1', fillOpacity: 0.9 }).bindTooltip(`${s.kind === 'Pickup' ? 'Pickup' : 'Drop'}: ${s.name}`).addTo(map)
    }
    map.fitBounds(L.latLngBounds(points.map((p) => L.latLng(p[0]!, p[1]!))), { padding: [30, 30] })
    return () => {
      map.remove()
      marker.current = null
      done.current = null
    }
  }, [points, stops])

  const frame = useMemo(() => frameAt(points, at), [points, at])
  useEffect(() => {
    if (!frame || !marker.current || !done.current) return
    marker.current.setLatLng([frame.latitude, frame.longitude])
    marker.current.setStyle({ fillColor: frame.inGap ? '#cf1322' : '#1677ff' })
    done.current.setLatLngs([...points.slice(0, frame.index + 1).map((p) => L.latLng(p[0]!, p[1]!)), L.latLng(frame.latitude, frame.longitude)])
  }, [frame, points])

  if (points.length === 0) return <Alert type="info" showIcon message="There are no recorded locations for this selection." />
  const jump = (iso: string) => {
    setPlaying(false)
    setAt(Math.min(end, Math.max(start, dayjs(iso).valueOf())))
  }

  return (
    <Flex vertical gap={12}>
      {replay.source === 'Summary' && <Alert type="warning" showIcon message="The detailed GPS for this trip has been archived. This replay follows its simplified path, so small turns and exact speeds are not shown." />}
      <div ref={element} role="img" aria-label="Replay map" style={{ height, borderRadius: 8, overflow: 'hidden' }} />
      <Flex gap={12} align="center" wrap>
        <Button type="primary" aria-label={playing ? 'Pause' : 'Play'} icon={playing ? <PauseOutlined /> : <CaretRightOutlined />} onClick={() => (at >= end ? (setAt(start), setPlaying(true)) : setPlaying(!playing))}>
          {playing ? 'Pause' : 'Play'}
        </Button>
        <Button icon={<ReloadOutlined />} onClick={() => { setAt(start); setPlaying(true) }}>Replay</Button>
        <Select value={speed} onChange={setSpeed} style={{ width: 120 }} aria-label="Speed" options={SPEEDS.map((v) => ({ value: v, label: speedLabel(v) }))} />
        <Typography.Text strong>{dayjs(at).format('DD MMM HH:mm:ss')}</Typography.Text>
        <Space>
          <Tag>{frame ? `${Math.round(frame.speedKph)} km/h` : ''}</Tag>
          {frame?.inGap && <Tag color="red">No location recorded</Tag>}
        </Space>
      </Flex>
      <Slider min={start} max={end} value={at} tooltip={{ formatter: (v) => dayjs(v).format('DD MMM HH:mm') }} onChange={(v) => { setPlaying(false); setAt(v) }} />
      {events && events.length > 0 && (
        <Flex vertical gap={4}>
          <Typography.Text type="secondary">What happened (click to jump there)</Typography.Text>
          {events.filter((e) => e.kind === 'Actual').map((e) => (
            <Button key={`${e.at}${e.label}`} type="link" size="small" style={{ textAlign: 'left', height: 'auto', padding: 0 }} onClick={() => jump(e.at)}>
              {dayjs(e.at).format('DD MMM HH:mm')} · {e.label}{e.detail ? ` — ${e.detail}` : ''}
            </Button>
          ))}
        </Flex>
      )}
    </Flex>
  )
}
