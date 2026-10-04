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

function RuleRow({ rule }: { rule: DocumentRuleDto }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [values, setValues] = useState({ isMandatory: rule.isMandatory, expiryRequired: rule.expiryRequired, renewalReminderDays: rule.renewalReminderDays, blockWhenExpired: rule.blockWhenExpired, isActive: rule.isActive })
  const dirty = JSON.stringify(values) !== JSON.stringify({ isMandatory: rule.isMandatory, expiryRequired: rule.expiryRequired, renewalReminderDays: rule.renewalReminderDays, blockWhenExpired: rule.blockWhenExpired, isActive: rule.isActive })
  const save = useMutation({
    mutationFn: () => masterDataApi.saveDocumentRule(rule.kind, values),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.masterData.documentRules })
      void message.success(`${rule.label} rule saved`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const set = <K extends keyof typeof values>(key: K, value: (typeof values)[K]) => setValues((v) => ({ ...v, [key]: value }))
  return (
    <Table.Summary.Row>
      <Table.Summary.Cell index={0}>{rule.label}{rule.isCustomised && <Tag style={{ marginLeft: 8 }}>Customised</Tag>}</Table.Summary.Cell>
      <Table.Summary.Cell index={1}>{rule.owner}</Table.Summary.Cell>
      <Table.Summary.Cell index={2}><Switch aria-label={`${rule.label} in use`} checked={values.isActive} onChange={(v) => setValues((c) => ({ ...c, isActive: v, isMandatory: v ? c.isMandatory : false }))} /></Table.Summary.Cell>
      <Table.Summary.Cell index={3}><Switch aria-label={`${rule.label} mandatory`} checked={values.isMandatory} disabled={!values.isActive} onChange={(v) => set('isMandatory', v)} /></Table.Summary.Cell>
      <Table.Summary.Cell index={4}><Switch aria-label={`${rule.label} needs an expiry date`} checked={values.expiryRequired} onChange={(v) => set('expiryRequired', v)} /></Table.Summary.Cell>
      <Table.Summary.Cell index={5}><InputNumber aria-label={`${rule.label} reminder days`} min={0} max={365} value={values.renewalReminderDays} onChange={(v) => set('renewalReminderDays', v ?? 0)} /></Table.Summary.Cell>
      <Table.Summary.Cell index={6}><Switch aria-label={`${rule.label} blocks work when expired`} checked={values.blockWhenExpired} onChange={(v) => set('blockWhenExpired', v)} /></Table.Summary.Cell>
      <Table.Summary.Cell index={7}><Button size="small" type="primary" disabled={!dirty} loading={save.isPending} onClick={() => save.mutate()}>Save</Button></Table.Summary.Cell>
    </Table.Summary.Row>
  )
}

function DocumentRules() {
  const rules = useQuery({ queryKey: queryKeys.masterData.documentRules, queryFn: () => masterDataApi.documentRules() })
  return (
    <Card title="Compliance papers">
      <Typography.Paragraph type="secondary">
        Which papers are required, which need an expiry date, how early a renewal is flagged, and whether an expired paper stops a vehicle or driver being given work. Changes apply to all transporters straight away.
      </Typography.Paragraph>
      {rules.isError && <Alert type="error" showIcon title={rules.error.message} style={{ marginBottom: 12 }} />}
      <Table
        size="small"
        rowKey="kind"
        pagination={false}
        loading={rules.isLoading}
        dataSource={[]}
        locale={{ emptyText: null }}
        scroll={{ x: 'max-content' }}
        columns={[
          { title: 'Paper', dataIndex: 'label' }, { title: 'For', dataIndex: 'owner' }, { title: 'Checked' }, { title: 'Mandatory' },
          { title: 'Expiry date needed' }, { title: 'Flag before (days)' }, { title: 'Expired blocks work' }, { title: '' },
        ]}
        summary={() => <>{rules.data?.map((r) => <RuleRow key={`${r.kind}:${JSON.stringify(r)}`} rule={r} />)}</>}
      />
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
