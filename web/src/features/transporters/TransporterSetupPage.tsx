import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, InputNumber, Switch, Table, Tabs, Tag, Typography, type TableColumnsType } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { masterDataApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DocumentRuleDto, MasterEntryDto } from '@/lib/api/types'

function MasterList({ title, help, queryKey, load, save }: {
  title: string
  help: string
  queryKey: readonly string[]
  load: () => Promise<MasterEntryDto[]>
  save: (body: { code: string; name: string; isActive: boolean }) => Promise<MasterEntryDto>
}) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const list = useQuery({ queryKey, queryFn: load })
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const mutation = useMutation({
    mutationFn: (body: { code: string; name: string; isActive: boolean }) => save(body),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey })
      void message.success('Saved')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const columns: TableColumnsType<MasterEntryDto> = [
    { title: 'Code', dataIndex: 'code', render: (v: string, e) => <>{v} {e.isBuiltIn && <Tag>Built in</Tag>}</> },
    { title: 'Name', dataIndex: 'name' },
    { title: 'In use', key: 'on', render: (_, e) => <Switch aria-label={`${e.name} in use`} checked={e.isActive} loading={mutation.isPending} onChange={(on) => mutation.mutate({ code: e.code, name: e.name, isActive: on })} /> },
  ]

  return (
    <Card title={title}>
      <Typography.Paragraph type="secondary">{help}</Typography.Paragraph>
      {list.isError && <Alert type="error" showIcon title={list.error.message} style={{ marginBottom: 12 }} />}
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input aria-label="New code" placeholder="Code, e.g. COLD_CHAIN" style={{ width: 220 }} value={code} onChange={(e) => setCode(e.target.value)} />
        <Input aria-label="New name" placeholder="Name" style={{ width: 240 }} value={name} onChange={(e) => setName(e.target.value)} />
        <Button icon={<PlusOutlined />} disabled={!code.trim() || !name.trim()} loading={mutation.isPending} onClick={() => mutation.mutate({ code: code.trim(), name: name.trim(), isActive: true }, { onSuccess: () => { setCode(''); setName('') } })}>Add</Button>
      </Flex>
      <Table<MasterEntryDto> rowKey="code" size="small" pagination={false} loading={list.isLoading} dataSource={list.data} columns={columns} />
    </Card>
  )
}

type RuleValues = Pick<DocumentRuleDto, 'isMandatory' | 'expiryRequired' | 'renewalReminderDays' | 'blockWhenExpired' | 'isActive'>

const valuesOf = (r: DocumentRuleDto): RuleValues => ({ isMandatory: r.isMandatory, expiryRequired: r.expiryRequired, renewalReminderDays: r.renewalReminderDays, blockWhenExpired: r.blockWhenExpired, isActive: r.isActive })

function DocumentRules() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const rules = useQuery({ queryKey: queryKeys.masterData.documentRules, queryFn: () => masterDataApi.documentRules() })
  // Unsaved changes, by kind; a row is saved on its own.
  const [edits, setEdits] = useState<Record<string, RuleValues>>({})
  const save = useMutation({
    mutationFn: (v: { rule: DocumentRuleDto; values: RuleValues }) => masterDataApi.saveDocumentRule(v.rule.kind, v.values),
    onSuccess: async (_, v) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterData.documentRules })
      setEdits(({ [v.rule.kind]: _saved, ...rest }) => rest)
      void message.success(`${v.rule.label} rule saved`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  const current = (r: DocumentRuleDto): RuleValues => edits[r.kind] ?? valuesOf(r)
  const change = (r: DocumentRuleDto, patch: Partial<RuleValues>) => setEdits((all) => ({ ...all, [r.kind]: { ...current(r), ...patch } }))

  const columns: TableColumnsType<DocumentRuleDto> = [
    { title: 'Paper', key: 'label', render: (_, r) => <>{r.label}{r.isCustomised && <Tag style={{ marginLeft: 8 }}>Customised</Tag>}</> },
    { title: 'For', dataIndex: 'owner' },
    { title: 'Checked', key: 'on', render: (_, r) => <Switch aria-label={`${r.label} in use`} checked={current(r).isActive} onChange={(on) => change(r, { isActive: on, isMandatory: on ? current(r).isMandatory : false })} /> },
    { title: 'Mandatory', key: 'm', render: (_, r) => <Switch aria-label={`${r.label} mandatory`} checked={current(r).isMandatory} disabled={!current(r).isActive} onChange={(v) => change(r, { isMandatory: v })} /> },
    { title: 'Expiry date needed', key: 'e', render: (_, r) => <Switch aria-label={`${r.label} needs an expiry date`} checked={current(r).expiryRequired} onChange={(v) => change(r, { expiryRequired: v })} /> },
    { title: 'Flag before (days)', key: 'd', render: (_, r) => <InputNumber aria-label={`${r.label} reminder days`} min={0} max={365} style={{ width: 80 }} value={current(r).renewalReminderDays} onChange={(v) => change(r, { renewalReminderDays: v ?? 0 })} /> },
    { title: 'Expired blocks work', key: 'b', render: (_, r) => <Switch aria-label={`${r.label} blocks work when expired`} checked={current(r).blockWhenExpired} onChange={(v) => change(r, { blockWhenExpired: v })} /> },
    { title: '', key: 's', render: (_, r) => <Button size="small" type="primary" disabled={edits[r.kind] === undefined} loading={save.isPending && save.variables?.rule.kind === r.kind} onClick={() => save.mutate({ rule: r, values: current(r) })}>Save</Button> },
  ]

  return (
    <Card title="Compliance papers">
      <Typography.Paragraph type="secondary">
        Which papers are required, which need an expiry date, how early a renewal is flagged, and whether an expired paper stops a vehicle or driver being given work. Changes apply to all transporters straight away.
      </Typography.Paragraph>
      {rules.isError && <Alert type="error" showIcon title={rules.error.message} style={{ marginBottom: 12 }} />}
      <Table<DocumentRuleDto> size="small" rowKey="kind" pagination={false} loading={rules.isLoading} dataSource={rules.data} columns={columns} scroll={{ x: 'max-content' }} />
    </Card>
  )
}

export function TransporterSetupPage() {
  return (
    <>
      <PageHeader title="Transporter setup" description="Lists and rules that shape how transporters are onboarded and kept compliant." />
      <Tabs
        items={[
          { key: 'papers', label: 'Compliance papers', children: <DocumentRules /> },
          {
            key: 'types',
            label: 'Transporter types',
            children: <MasterList title="Types of operator" help="What sort of operator a transporter is. Switching one off stops new transporters using it; existing ones keep it." queryKey={queryKeys.masterData.types} load={masterDataApi.types} save={masterDataApi.saveType} />,
          },
          {
            key: 'caps',
            label: 'Capabilities',
            children: <MasterList title="Capabilities" help="What a transporter can be asked to carry. Loads that need one are only offered to transporters that hold it." queryKey={queryKeys.masterData.capabilityTypes} load={masterDataApi.capabilityTypes} save={masterDataApi.saveCapabilityType} />,
          },
        ]}
      />
    </>
  )
}
