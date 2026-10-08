import { FilterOutlined } from '@ant-design/icons'
import { useQueries } from '@tanstack/react-query'
import { Badge, Button, Card, DatePicker, Flex, Input, InputNumber, Select, Switch, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'
import { STATIC_OPTIONS, humanise } from './format'
import type { FilterDto } from './types'

const SKIP = new Set(['fromDate', 'toDate', 'period'])

interface Props {
  filters: FilterDto[]
  values: Record<string, string>
  onApply: (values: Record<string, string>) => void
}

/** Filters for any report, built from what the report says it accepts (its metadata). Dates and period come first; the rest follow their lookups. */
export function ReportFilterBar({ filters, values, onApply }: Props) {
  const [draft, setDraft] = useState<Record<string, string>>(values)
  const [open, setOpen] = useState(true)
  const lookups = [...new Set(filters.map((f) => f.lookupSource).filter((s): s is string => Boolean(s) && !STATIC_OPTIONS[s as string]))]
  const results = useQueries({ queries: lookups.map((name) => ({ queryKey: queryKeys.reports.lookup(name), queryFn: () => reportsApi.lookup(name), staleTime: 5 * 60_000 })) })
  const options = (source: string | null): string[] => {
    if (!source) return []
    if (STATIC_OPTIONS[source]) return STATIC_OPTIONS[source]
    const i = lookups.indexOf(source)
    return i >= 0 ? (results[i]?.data?.values ?? []) : []
  }
  const set = (name: string, value: string | null | undefined) => setDraft((d) => ({ ...d, [name]: value ?? '' }))
  const hasDates = filters.some((f) => f.name === 'fromDate')
  const hasPeriod = filters.some((f) => f.name === 'period')
  const active = Object.entries(values).filter(([k, v]) => v && !k.startsWith('f_')).length

  const apply = () => onApply(Object.fromEntries(Object.entries(draft).filter(([, v]) => v !== '')))
  return (
    <Card size="small" style={{ marginBottom: 16 }}>
      <Flex justify="space-between" align="center" wrap gap={8}>
        <Button type="text" icon={<FilterOutlined />} onClick={() => setOpen((o) => !o)} aria-expanded={open}>
          <Badge count={active} size="small" offset={[8, -2]}>
            Filters
          </Badge>
        </Button>
        {!open && <Typography.Text type="secondary">{active === 0 ? 'No filters' : `${active} applied`}</Typography.Text>}
      </Flex>
      {open && (
        <Flex wrap gap={12} align="flex-end" style={{ marginTop: 8 }}>
          {hasPeriod && (
            <Field label="Period">
              <Select
                allowClear
                placeholder="Last 30 days"
                style={{ width: 150 }}
                value={draft.period || undefined}
                onChange={(v) => setDraft((d) => ({ ...d, period: v ?? '', ...(v && v !== 'Custom' ? { fromDate: '', toDate: '' } : {}) }))}
                options={options('period').map((o) => ({ value: o, label: humanise(o) }))}
              />
            </Field>
          )}
          {hasDates && (
            <Field label="Dates">
              <DatePicker.RangePicker
                allowEmpty={[true, true]}
                value={[draft.fromDate ? dayjs(draft.fromDate) : null, draft.toDate ? dayjs(draft.toDate) : null]}
                onChange={(range) => setDraft((d) => ({ ...d, fromDate: range?.[0]?.format('YYYY-MM-DD') ?? '', toDate: range?.[1]?.format('YYYY-MM-DD') ?? '', period: range?.[0] ? '' : (d.period ?? '') }))}
              />
            </Field>
          )}
          {filters
            .filter((f) => !SKIP.has(f.name) && f.name !== 'search' && f.name !== 'compare')
            .map((f) => (
              <Field key={f.name} label={humanise(f.name === 'serviceType' ? 'service' : f.name)}>
                {f.dataType === 'Boolean' ? (
                  <Switch checked={draft[f.name] !== 'false'} onChange={(on) => set(f.name, on ? '' : 'false')} aria-label={humanise(f.name)} />
                ) : f.dataType === 'Whole' ? (
                  <InputNumber min={0} style={{ width: 110 }} value={draft[f.name] ? Number(draft[f.name]) : null} onChange={(v) => set(f.name, v === null ? '' : String(v))} />
                ) : f.lookupSource ? (
                  <Select
                    showSearch
                    allowClear
                    placeholder="All"
                    style={{ minWidth: 170 }}
                    value={draft[f.name] || undefined}
                    onChange={(v) => set(f.name, v)}
                    options={options(f.lookupSource).map((o) => ({ value: o, label: o }))}
                    loading={results.some((r) => r.isLoading)}
                  />
                ) : (
                  <Input allowClear style={{ width: 160 }} value={draft[f.name] ?? ''} onChange={(e) => set(f.name, e.target.value)} onPressEnter={apply} />
                )}
              </Field>
            ))}
          {filters.some((f) => f.name === 'search') && (
            <Field label="Search">
              <Input.Search allowClear style={{ width: 220 }} value={draft.search ?? ''} onChange={(e) => set('search', e.target.value)} onSearch={apply} placeholder="Reference, customer, carrier…" />
            </Field>
          )}
          <Flex gap={8}>
            <Button type="primary" onClick={apply}>
              Apply
            </Button>
            <Button
              onClick={() => {
                setDraft({})
                onApply({})
              }}
            >
              Reset
            </Button>
          </Flex>
        </Flex>
      )}
    </Card>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12, marginBottom: 2 }}>
        {label}
      </Typography.Text>
      {children}
    </div>
  )
}
