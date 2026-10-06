import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, Tag, Typography } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { trackingApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { TrackingSettingDto } from '@/lib/api/types'

const TITLES: Record<string, string> = {
  interval: 'How often the phone reports', health: 'When tracking counts as stale or lost', validation: 'What counts as a bad GPS point', geofence: 'Entering and leaving a place',
  route: 'Route deviation', dwell: 'Standing time', eta: 'Arrival estimate and risk', alerts: 'Alert rules and escalation', retention: 'How long data is kept', links: 'Customer links', milestones: 'Milestones',
}

/** Each rule set is shown as the JSON the server stores, validated by the server on save. Policy lives here, not in code. */
export function TrackingSettingsPage() {
  const q = useQuery({ queryKey: queryKeys.tracking.settings, queryFn: trackingApi.settings })
  return (
    <>
      <PageHeader title="Tracking rules" description="Thresholds and rules for this company. Anything not changed here uses the built-in default." />
      <Flex vertical gap={12}>{(q.data ?? []).map((s) => <SettingCard key={s.key} setting={s} />)}</Flex>
    </>
  )
}

function SettingCard({ setting }: { setting: TrackingSettingDto }) {
  const { message } = App.useApp()
  const client = useQueryClient()
  const [text, setText] = useState(JSON.stringify(setting.value, null, 2))
  const [error, setError] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => trackingApi.saveSetting(setting.key, JSON.parse(text) as unknown),
    onSuccess: () => { setError(null); void message.success('Saved'); void client.invalidateQueries({ queryKey: queryKeys.tracking.settings }) },
    onError: (e) => setError(e instanceof SyntaxError ? 'This is not valid JSON.' : toApiError(e).message),
  })
  return (
    <Card size="small" title={<>{TITLES[setting.key.replace('tracking.', '')] ?? setting.key} {setting.isCustomised ? <Tag color="blue">Customised</Tag> : <Tag>Default</Tag>}</>} extra={<Typography.Text type="secondary">{setting.key}</Typography.Text>}>
      <Input.TextArea rows={Math.min(14, text.split('\n').length + 1)} value={text} onChange={(e) => setText(e.target.value)} style={{ fontFamily: 'monospace' }} />
      {error && <Alert type="error" showIcon message={error} style={{ marginTop: 8 }} />}
      <Button type="primary" style={{ marginTop: 8 }} loading={save.isPending} onClick={() => save.mutate()}>Save</Button>
    </Card>
  )
}
