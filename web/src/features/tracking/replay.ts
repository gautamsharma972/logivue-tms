/** A recorded position: latitude, longitude, epoch milliseconds, speed km/h (0 when unknown). */
export type ReplayPoint = number[]

export interface ReplayFrame {
  latitude: number
  longitude: number
  speedKph: number
  at: number
  /** Index of the last recorded point at or before this moment. */
  index: number
  /** True when the vehicle was between two points more than the silence limit apart: the position is a guess, not a recording. */
  inGap: boolean
}

/** A silence this long between two recorded points is shown as a gap in the replay rather than as smooth movement. */
export const GAP_MS = 15 * 60 * 1000

/**
 * Where the vehicle was at a moment, between recorded points. Within a gap (no points for a long time) it stays at the last known place, because inventing a path
 * across a silence would be showing something that was never recorded.
 */
export function frameAt(points: ReplayPoint[], at: number): ReplayFrame | null {
  if (points.length === 0) return null
  const first = points[0]!
  if (at <= first[2]!) return { latitude: first[0]!, longitude: first[1]!, speedKph: first[3] ?? 0, at: first[2]!, index: 0, inGap: false }
  const last = points[points.length - 1]!
  if (at >= last[2]!) return { latitude: last[0]!, longitude: last[1]!, speedKph: last[3] ?? 0, at: last[2]!, index: points.length - 1, inGap: false }
  let lo = 0
  let hi = points.length - 1
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1
    if (points[mid]![2]! <= at) lo = mid
    else hi = mid
  }
  const a = points[lo]!
  const b = points[hi]!
  if (b[2]! - a[2]! > GAP_MS) return { latitude: a[0]!, longitude: a[1]!, speedKph: 0, at, index: lo, inGap: true }
  const t = (at - a[2]!) / (b[2]! - a[2]!)
  return { latitude: a[0]! + (b[0]! - a[0]!) * t, longitude: a[1]! + (b[1]! - a[1]!) * t, speedKph: a[3] ?? 0, at, index: lo, inGap: false }
}

/** Speeds the player offers: how many seconds of the trip pass in each second of watching. */
export const SPEEDS = [60, 300, 900, 3600] as const

export function speedLabel(secondsPerSecond: number): string {
  return secondsPerSecond >= 3600 ? '1 h/s' : `${secondsPerSecond / 60} min/s`
}
