import { Flex, Input, Select } from 'antd'
import type { PlaceDto, PlaceKind, ZoneDto } from '@/lib/api/types'
import { INDIAN_STATES } from '@/lib/indiaStates'

export const anywhere: PlaceDto = { kind: 'Any', state: null, city: null, zoneCode: null }

const kinds: { value: PlaceKind; label: string }[] = [
  { value: 'City', label: 'A city' },
  { value: 'Zone', label: 'A zone' },
  { value: 'State', label: 'A whole state' },
  { value: 'Any', label: 'Anywhere' },
]

interface Props {
  value?: PlaceDto
  onChange?: (value: PlaceDto) => void
  zones: ZoneDto[]
  disabled?: boolean
}

/** Picks one end of a lane: a city, a zone, a state, or anywhere. A more specific place beats a broader one when rates are chosen. */
export function PlaceInput({ value = anywhere, onChange, zones, disabled }: Props) {
  const set = (patch: Partial<PlaceDto>) => onChange?.({ ...value, ...patch })

  return (
    <Flex gap={8} wrap>
      <Select<PlaceKind>
        style={{ width: 140 }}
        disabled={disabled}
        value={value.kind}
        options={kinds}
        onChange={(kind) => onChange?.({ kind, state: kind === 'City' || kind === 'State' ? value.state : null, city: kind === 'City' ? value.city : null, zoneCode: kind === 'Zone' ? value.zoneCode : null })}
      />
      {(value.kind === 'City' || value.kind === 'State') && (
        <Select
          showSearch
          style={{ width: 200 }}
          disabled={disabled}
          placeholder="State"
          value={value.state ?? undefined}
          options={INDIAN_STATES.map((s) => ({ value: s.toUpperCase(), label: s }))}
          onChange={(state) => set({ state })}
        />
      )}
      {value.kind === 'City' && (
        <Input style={{ width: 180 }} disabled={disabled} placeholder="City" value={value.city ?? ''} onChange={(e) => set({ city: e.target.value })} />
      )}
      {value.kind === 'Zone' && (
        <Select
          showSearch
          optionFilterProp="label"
          style={{ width: 240 }}
          disabled={disabled}
          placeholder={zones.length ? 'Zone' : 'No zones yet — add them under Rate masters'}
          value={value.zoneCode ?? undefined}
          options={zones.map((z) => ({ value: z.code, label: `${z.code} — ${z.name}` }))}
          onChange={(zoneCode) => set({ zoneCode })}
        />
      )}
    </Flex>
  )
}
