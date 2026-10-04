import { Flex, InputNumber, Select, Switch, Typography } from 'antd'
import type { PlanOptions } from '@/lib/api/types'
import { objectiveOptions } from './shared'

interface Props {
  value: PlanOptions
  onChange: (value: PlanOptions) => void
  disabled?: boolean
}

/** The levers a planner can pull. The server re-validates every one of them. */
export function PlanOptionsForm({ value, onChange, disabled }: Props) {
  const set = (patch: Partial<PlanOptions>) => onChange({ ...value, ...patch })
  const toggle = (label: string, key: 'allowFtl' | 'allowPtl' | 'allowConsolidation') => (
    <Flex gap={8} align="center">
      <Switch size="small" aria-label={label} disabled={disabled} checked={value[key]} onChange={(checked) => set({ [key]: checked })} />
      <Typography.Text>{label}</Typography.Text>
    </Flex>
  )
  return (
    <Flex vertical gap={12}>
      <div>
        <Typography.Text type="secondary">Objective</Typography.Text>
        <Select aria-label="Objective" style={{ width: '100%' }} disabled={disabled} virtual={false} value={value.objective} options={objectiveOptions} onChange={(objective) => set({ objective })} />
      </div>
      <Flex gap={16} wrap>
        {toggle('Full truck (FTL)', 'allowFtl')}
        {toggle('Part load (PTL)', 'allowPtl')}
        {toggle('Consolidate orders', 'allowConsolidation')}
      </Flex>
      <Flex gap={8} align="center">
        <Switch size="small" aria-label="Enforce delivery deadlines" disabled={disabled} checked={value.enforceDeadlines} onChange={(enforceDeadlines) => set({ enforceDeadlines })} />
        <Typography.Text>Reject options that miss the deliver-by date</Typography.Text>
      </Flex>
      <Flex gap={8} align="center">
        <Switch size="small" aria-label="Collect returns on the way back" disabled={disabled} checked={value.allowBackhaul} onChange={(allowBackhaul) => set({ allowBackhaul })} />
        <Typography.Text>Collect return pickups on the way back</Typography.Text>
      </Flex>
      {value.allowBackhaul && (
        <Flex gap={8} align="center">
          <Switch size="small" aria-label="Finish deliveries before collecting returns" disabled={disabled} checked={value.returnsAfterDeliveries} onChange={(returnsAfterDeliveries) => set({ returnsAfterDeliveries })} />
          <Typography.Text>Finish all deliveries before collecting returns</Typography.Text>
        </Flex>
      )}
      <Flex gap={12} wrap>
        <div>
          <Typography.Text type="secondary">Max stops per vehicle</Typography.Text>
          <br />
          <InputNumber aria-label="Maximum stops" min={1} max={50} disabled={disabled} value={value.maxStops} onChange={(v) => set({ maxStops: v ?? 8 })} />
        </div>
        <div>
          <Typography.Text type="secondary">Time limit (seconds)</Typography.Text>
          <br />
          <InputNumber aria-label="Time limit" min={1} max={120} disabled={disabled} value={value.timeBudgetSeconds} onChange={(v) => set({ timeBudgetSeconds: v ?? 20 })} />
        </div>
        <div>
          <Typography.Text type="secondary">Part-load extra transit (h)</Typography.Text>
          <br />
          <InputNumber aria-label="Part-load extra transit hours" min={0} max={240} disabled={disabled} value={value.ptlExtraTransitHours} onChange={(v) => set({ ptlExtraTransitHours: v ?? 24 })} />
        </div>
        <div>
          <Typography.Text type="secondary">Minutes at each stop</Typography.Text>
          <br />
          <InputNumber aria-label="Minutes at each stop" min={0} max={480} disabled={disabled} value={value.stopServiceMinutes} onChange={(v) => set({ stopServiceMinutes: v ?? 30 })} />
        </div>
        {value.allowBackhaul && (
          <>
            <div>
              <Typography.Text type="secondary">Return charge (% of a return trip)</Typography.Text>
              <br />
              <InputNumber aria-label="Return charge percent" min={0} max={100} disabled={disabled} value={value.backhaulChargePercent} onChange={(v) => set({ backhaulChargePercent: v ?? 50 })} />
            </div>
            <div>
              <Typography.Text type="secondary">Max return detour (km)</Typography.Text>
              <br />
              <InputNumber aria-label="Maximum return detour" min={0} max={1000} disabled={disabled} value={value.maxBackhaulExtraKm} onChange={(v) => set({ maxBackhaulExtraKm: v ?? 100 })} />
            </div>
          </>
        )}
        <div>
          <Typography.Text type="secondary">Departure hour (0–23)</Typography.Text>
          <br />
          <InputNumber aria-label="Departure hour" min={0} max={23} disabled={disabled} value={value.departureHour} onChange={(v) => set({ departureHour: v ?? 8 })} />
        </div>
      </Flex>
    </Flex>
  )
}
