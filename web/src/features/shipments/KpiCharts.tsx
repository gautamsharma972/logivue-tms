import { Bar, BarChart, CartesianGrid, ComposedChart, Legend, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { DailyKpi } from '@/lib/api/types'

const unplannedLabel = (code: string) => code.replaceAll('_', ' ').toLowerCase()

/** Freight spend per planning day with the weight fill of the trucks used that day. */
export function DailyChart({ daily }: { daily: DailyKpi[] }) {
  const data = daily.map((d) => ({ date: d.date.slice(5), cost: d.cost, weightFill: d.averageWeightUtilisation === null ? null : Math.round(d.averageWeightUtilisation * 100), vehicles: d.vehicles }))
  return (
    <div role="img" aria-label="Daily freight cost and weight fill" style={{ height: 280 }}>
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={data} margin={{ top: 8, right: 16, bottom: 0, left: 0 }}>
          <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
          <XAxis dataKey="date" />
          <YAxis yAxisId="cost" tickFormatter={(v: number) => `₹${Math.round(v / 1000)}k`} />
          <YAxis yAxisId="fill" orientation="right" domain={[0, 100]} tickFormatter={(v: number) => `${v}%`} />
          <Tooltip />
          <Legend />
          <Bar yAxisId="cost" dataKey="cost" name="Freight (₹)" fill="#1677ff" isAnimationActive={false} />
          <Line yAxisId="fill" dataKey="weightFill" name="Weight fill (%)" stroke="#52c41a" connectNulls isAnimationActive={false} />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  )
}

export function ReasonsChart({ reasons }: { reasons: { code: string; orders: number }[] }) {
  const data = reasons.map((r) => ({ reason: unplannedLabel(r.code), orders: r.orders }))
  return (
    <div role="img" aria-label="Unplanned orders by reason" style={{ height: Math.max(160, data.length * 44) }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 8, right: 16, bottom: 0, left: 40 }}>
          <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
          <XAxis type="number" allowDecimals={false} />
          <YAxis type="category" dataKey="reason" width={150} />
          <Tooltip />
          <Bar dataKey="orders" name="Orders" fill="#fa541c" isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
