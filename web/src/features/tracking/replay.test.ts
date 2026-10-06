import { describe, expect, it } from 'vitest'
import { frameAt, GAP_MS, speedLabel } from './replay'

const MIN = 60_000
const P = (lat: number, lon: number, minutes: number, speed = 40) => [lat, lon, minutes * MIN, speed]

describe('frameAt', () => {
  const path = [P(19, 73, 0), P(19.1, 73, 10), P(19.2, 73, 20)]

  it('moves smoothly between two recorded points', () => {
    const f = frameAt(path, 5 * MIN)!
    expect(f.latitude).toBeCloseTo(19.05, 5)
    expect(f.index).toBe(0)
    expect(f.inGap).toBe(false)
  })

  it('is at the first point before the trip and at the last after it', () => {
    expect(frameAt(path, -10 * MIN)!.latitude).toBe(19)
    expect(frameAt(path, 99 * MIN)!.latitude).toBe(19.2)
    expect(frameAt(path, 99 * MIN)!.index).toBe(2)
  })

  it('stays where it was last seen during a long silence instead of inventing a path', () => {
    const gapped = [P(19, 73, 0), P(20, 74, GAP_MS / MIN + 30)]
    const f = frameAt(gapped, 30 * MIN)!
    expect(f.inGap).toBe(true)
    expect(f.latitude).toBe(19)
    expect(f.speedKph).toBe(0)
  })

  it('has nothing to show for an empty recording', () => {
    expect(frameAt([], 0)).toBeNull()
  })
})

describe('speedLabel', () => {
  it('names the speeds in trip time per second watched', () => {
    expect(speedLabel(60)).toBe('1 min/s')
    expect(speedLabel(900)).toBe('15 min/s')
    expect(speedLabel(3600)).toBe('1 h/s')
  })
})
