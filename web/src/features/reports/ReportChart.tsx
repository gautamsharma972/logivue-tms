import { Card, Empty, Typography } from 'antd'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { useEffect, useRef } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ReferenceLine, ResponsiveContainer, Scatter, ScatterChart, Tooltip, XAxis, YAxis } from 'recharts'
import { formatCell } from './format'
import type { ChartData, ReportRow } from './types'

const PALETTE = ['#2563eb', '#16a34a', '#f97316', '#dc2626', '#7c3aed', '#0891b2', '#ca8a04', '#db2777']
const RISK_COLOUR: Record<string, string> = { OnTime: '#16a34a', AtRisk: '#eab308', Delayed: '#f97316', SeverelyDelayed: '#dc2626' }

const colourOf = (chart: ChartData, index: number) => chart.series[index]?.colour ?? PALETTE[index % PALETTE.length]

function numeric(value: unknown): number | null {
  return typeof value === 'number' ? value : null
}

function HeatTable({ chart }: { chart: ChartData }) {
  const lanes = [...new Set(chart.data.map((d) => String(d.lane)))]
  const metrics = [...new Set(chart.data.map((d) => String(d.metric)))]
  const value = (lane: string, metric: string) => numeric(chart.data.find((d) => d.lane === lane && d.metric === metric)?.value)
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 12 }}>
        <thead>
          <tr>
            <th style={{ textAlign: 'left', padding: 4 }}>Lane</th>
            {metrics.map((m) => (
              <th key={m} style={{ padding: 4 }}>
                {m}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {lanes.map((lane) => (
            <tr key={lane}>
              <td style={{ padding: 4, whiteSpace: 'nowrap' }}>{lane}</td>
              {metrics.map((m) => {
                const v = value(lane, m)
                const hue = v === null ? 0 : Math.max(0, Math.min(120, (v - 50) * 2.4))
                return (
                  <td key={m} title={v === null ? 'Not measurable' : String(v)} style={{ padding: 4, textAlign: 'center', background: v === null ? 'transparent' : `hsla(${hue}, 70%, 50%, 0.35)` }}>
                    {v === null ? '—' : v.toFixed(0)}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Vehicles as risk-coloured pins, or a trip's planned (blue) and driven (orange) road. */
function ReportMap({ chart, height }: { chart: ChartData; height: number }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!ref.current) return
    const map = L.map(ref.current, { scrollWheelZoom: false }).setView([21, 78], 5)
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 18, attribution: '© OpenStreetMap' }).addTo(map)
    const bounds: [number, number][] = []
    const routes = new Map<string, { planned: [number, number][]; actual: [number, number][] }>()
    for (const d of chart.data) {
      const lat = numeric(d.lat)
      const lon = numeric(d.lon)
      if (lat === null || lon === null) continue
      bounds.push([lat, lon])
      if (d.kind === 'planned' || d.kind === 'actual') {
        const key = String(d.trip)
        const entry = routes.get(key) ?? { planned: [], actual: [] }
        entry[d.kind].push([lat, lon])
        routes.set(key, entry)
      } else {
        const colour = d.health === 'Stale' || d.health === 'Lost' ? '#6b7280' : (RISK_COLOUR[String(d.risk)] ?? '#2563eb')
        L.circleMarker([lat, lon], { radius: 8, color: '#fff', weight: 2, fillColor: colour, fillOpacity: 0.95 }).bindTooltip(String(d.label ?? '')).addTo(map)
      }
    }
    for (const r of routes.values()) {
      if (r.planned.length > 1) L.polyline(r.planned, { color: '#2563eb', weight: 3 }).addTo(map)
      if (r.actual.length > 1) L.polyline(r.actual, { color: '#f97316', weight: 3, dashArray: '6 4' }).addTo(map)
    }
    const fit = window.setTimeout(() => {
      map.invalidateSize()
      if (bounds.length > 0) map.fitBounds(bounds, { padding: [30, 30], maxZoom: 9 })
    }, 150)
    return () => {
      window.clearTimeout(fit)
      map.remove()
    }
  }, [chart])
  return <div ref={ref} style={{ height, borderRadius: 8 }} role="img" aria-label={chart.title} />
}

interface Props {
  chart: ChartData
  height?: number
  onDrill?: (row: ReportRow) => void
}

/** One chart the report asked for, drawn from its rows. A chart is only ever drawn from what the report returned: no figures are invented here. */
export function ReportChart({ chart, height = 280, onDrill }: Props) {
  const empty = chart.data.length === 0
  const click = onDrill && chart.drillReport ? (row: ReportRow) => onDrill(row) : undefined
  const x = chart.xField ?? 'x'
  let body
  if (empty) {
    body = <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="No data for the selected filters" />
  } else if (chart.kind === 'map') {
    body = <ReportMap chart={chart} height={height + 40} />
  } else if (chart.kind === 'heatmap') {
    body = <HeatTable chart={chart} />
  } else if (chart.kind === 'donut') {
    const key = chart.series[0]?.key ?? 'value'
    body = (
      <ResponsiveContainer width="100%" height={height}>
        <PieChart>
          <Pie isAnimationActive={false} data={chart.data} dataKey={key} nameKey={x} innerRadius="55%" outerRadius="85%" paddingAngle={2} onClick={(e) => click?.(e as unknown as ReportRow)} cursor={click ? 'pointer' : undefined}>
            {chart.data.map((d, i) => (
              <Cell key={String(d[x])} fill={RISK_COLOUR[String(d[x])] ?? PALETTE[i % PALETTE.length]} />
            ))}
          </Pie>
          <Tooltip />
          <Legend />
        </PieChart>
      </ResponsiveContainer>
    )
  } else if (chart.kind === 'scatter') {
    const names = chart.series.map((s) => s.key)
    const groups = chart.series.map((s, i) => ({ s, i, points: chart.data.filter((d) => d.quadrant === s.key || d.quadrant === s.name) }))
    const matched = groups.some((g) => g.points.length > 0)
    body = (
      <ResponsiveContainer width="100%" height={height + 40}>
        <ScatterChart margin={{ top: 16, right: 16, bottom: 36, left: 8 }}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis type="number" dataKey={x} name={chart.xLabel ?? 'Cost'} label={{ value: chart.xLabel ?? '', position: 'insideBottom', offset: -12 }} tickFormatter={(v: number) => formatCell(v, 'Whole')} domain={['auto', 'auto']} />
          <YAxis type="number" dataKey="performance" name={chart.yLabel ?? 'Performance'} label={{ value: chart.yLabel ?? '', angle: -90, position: 'insideLeft' }} domain={['auto', 'auto']} />
          <Tooltip cursor={{ strokeDasharray: '3 3' }} content={({ payload }) => (payload?.[0] ? <ScatterTip row={payload[0].payload as ReportRow} chart={chart} /> : null)} />
          {chart.lines?.map((l) => (l.axis === 'x' ? <ReferenceLine key={l.label} x={l.value} stroke="#6b7280" strokeDasharray="4 4" label={l.label} /> : <ReferenceLine key={l.label} y={l.value} stroke="#6b7280" strokeDasharray="4 4" label={l.label} />))}
          {matched ? (
            groups.filter((g) => g.points.length > 0).map((g) => <Scatter isAnimationActive={false} key={g.s.key} name={g.s.name} data={g.points} fill={colourOf(chart, g.i)} />)
          ) : (
            <Scatter isAnimationActive={false} name={names[0] ?? 'Groups'} data={chart.data} fill={PALETTE[0]} />
          )}
          <Legend verticalAlign="top" height={30} />
        </ScatterChart>
      </ResponsiveContainer>
    )
  } else if (chart.kind === 'line') {
    body = (
      <ResponsiveContainer width="100%" height={height}>
        <LineChart data={chart.data} margin={{ top: 8, right: 16, bottom: 4, left: 8 }}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey={x} />
          <YAxis tickFormatter={(v: number) => (v >= 100_000 ? `${(v / 100_000).toFixed(0)}L` : String(v))} />
          <Tooltip formatter={(v) => (typeof v === 'number' ? formatCell(v, 'Number') : String(v ?? 'Not measurable'))} />
          <Legend />
          {chart.series.map((s, i) => (
            <Line isAnimationActive={false} key={s.key} type="monotone" dataKey={s.key} name={s.name} stroke={colourOf(chart, i)} strokeWidth={2} dot={{ r: 3 }} connectNulls={false} />
          ))}
        </LineChart>
      </ResponsiveContainer>
    )
  } else {
    const stacked = chart.kind === 'stackedBar'
    body = (
      <ResponsiveContainer width="100%" height={height}>
        <BarChart data={chart.data} margin={{ top: 8, right: 16, bottom: 4, left: 8 }}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey={x} interval={0} angle={chart.data.length > 6 ? -25 : 0} textAnchor={chart.data.length > 6 ? 'end' : 'middle'} height={chart.data.length > 6 ? 70 : 30} tick={{ fontSize: 11 }} />
          <YAxis />
          <Tooltip />
          <Legend />
          {chart.series.map((s, i) => (
            <Bar isAnimationActive={false} key={s.key} dataKey={s.key} name={s.name} fill={colourOf(chart, i)} stackId={stacked ? 'a' : undefined} cursor={click ? 'pointer' : undefined} onClick={(e) => click?.(e as unknown as ReportRow)} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    )
  }
  return (
    <Card size="small" title={chart.title} data-testid={`chart-${chart.id}`} styles={{ body: { minWidth: 0 } }}>
      <div style={{ width: '100%', minWidth: 0, overflow: 'hidden' }}>{body}</div>
      {chart.note && (
        <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginTop: 8 }}>
          {chart.note}
        </Typography.Text>
      )}
    </Card>
  )
}

function ScatterTip({ row, chart }: { row: ReportRow; chart: ChartData }) {
  return (
    <div style={{ background: '#fff', border: '1px solid #d9d9d9', borderRadius: 6, padding: 8, color: '#111', fontSize: 12 }}>
      <strong>{String(row.label ?? '')}</strong>
      <div>
        {chart.xLabel ?? 'x'}: {formatCell(numeric(row.cost), 'Number')}
      </div>
      <div>
        {chart.yLabel ?? 'y'}: {formatCell(numeric(row.performance), 'Number')}
      </div>
      {row.quadrant ? <div>{String(row.quadrant)}</div> : null}
    </div>
  )
}
