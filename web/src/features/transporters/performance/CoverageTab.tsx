import { PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Form, Input, Modal, Popconfirm, Select, Table, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Can } from '@/features/auth/AuthContext'
import { performanceApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CapabilityDto, PlanningRuleDto, PlanningRuleType } from '@/lib/api/types'
import { Lanes } from './OperationsPanel'

export const ruleLabels: Record<PlanningRuleType, { label: string; help: string; color: string }> = {
  PreferredCarrier: { label: 'Preferred carrier', help: 'Wins ties on price, everywhere.', color: 'green' },
  PreferredLane: { label: 'Preferred on a lane', help: 'Wins ties on price on one lane.', color: 'green' },
  AvoidForUrgent: { label: 'Avoid for urgent loads', help: 'Not given urgent orders.', color: 'orange' },
  Restricted: { label: 'Restricted', help: 'Not given loads; planning explains why.', color: 'red' },
  DoNotAllocate: { label: 'Do not allocate', help: 'Not given any loads until the rule is ended.', color: 'red' },
}

function Capabilities({ transporterId }: { transporterId: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState<string>()
  const catalog = useQuery({ queryKey: queryKeys.performance.capabilityCatalog, queryFn: () => performanceApi.capabilityCatalog() })
  const held = useQuery({ queryKey: queryKeys.performance.capabilities(transporterId), queryFn: () => performanceApi.capabilities(transporterId) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.performance.capabilities(transporterId) })
  const add = useMutation({
    mutationFn: (code: string) => performanceApi.addCapability(transporterId, code, dayjs().format('YYYY-MM-DD')),
    onSuccess: async () => { await refresh(); setAdding(undefined); void message.success('Capability added') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const end = useMutation({
    mutationFn: (id: string) => performanceApi.endCapability(id),
    onSuccess: async () => { await refresh(); void message.success('Capability removed') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const active = (held.data ?? []).filter((c) => c.isActive)
  const options = (catalog.data ?? []).filter((c) => !active.some((a) => a.code === c.code)).map((c) => ({ value: c.code, label: c.name }))

  return (
    <Card
      title="Capabilities"
      extra={
        <Can permission="transporters.performance.manage">
          <Flex gap={8}>
            <Select aria-label="Capability to add" style={{ width: 220 }} placeholder="Add a capability" virtual={false} options={options} value={adding} onChange={setAdding} />
            <Button icon={<PlusOutlined />} disabled={!adding} loading={add.isPending} onClick={() => adding && add.mutate(adding)}>Add</Button>
          </Flex>
        </Can>
      }
    >
      <Typography.Paragraph type="secondary">What this transporter can be asked to carry. A load that needs one (hazardous, temperature controlled…) is only offered to carriers that hold it.</Typography.Paragraph>
      <Table<CapabilityDto>
        size="small"
        rowKey="id"
        loading={held.isLoading}
        pagination={false}
        dataSource={active}
        locale={{ emptyText: 'No special capabilities recorded.' }}
        columns={[
          { title: 'Capability', dataIndex: 'name' },
          { title: 'Since', dataIndex: 'effectiveFrom' },
          { title: '', key: 'a', align: 'right', render: (_, c) => (
            <Can permission="transporters.performance.manage">
              <Popconfirm title="Remove this capability?" description="It is ended, not deleted, so past decisions stay explainable." okText="Remove" onConfirm={() => end.mutate(c.id)}>
                <Button size="small" danger>Remove</Button>
              </Popconfirm>
            </Can>) },
        ]}
      />
    </Card>
  )
}

function AddRule({ transporterId, onClose }: { transporterId: string; onClose: () => void }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ ruleType: PlanningRuleType; laneId?: string; reason: string }>()
  const type = Form.useWatch('ruleType', form)
  const lanes = useQuery({ queryKey: queryKeys.performance.lanes(transporterId), queryFn: () => performanceApi.lanes(transporterId) })
  const save = useMutation({
    mutationFn: (v: { ruleType: PlanningRuleType; laneId?: string; reason: string }) =>
      performanceApi.addPlanningRule(transporterId, { ruleType: v.ruleType, laneId: v.laneId ?? null, reason: v.reason.trim(), effectiveFrom: dayjs().format('YYYY-MM-DD'), effectiveTo: null }),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.rules(transporterId) }); void message.success('Rule added'); onClose() },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Modal open title="Add a planning rule" okText="Add rule" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Form form={form} layout="vertical" requiredMark="optional" onFinish={(v) => save.mutate(v)}>
        <Form.Item name="ruleType" label="Rule" rules={[{ required: true, message: 'Choose a rule' }]} extra={type ? ruleLabels[type].help : undefined}>
          <Select aria-label="Rule" virtual={false} options={(Object.keys(ruleLabels) as PlanningRuleType[]).map((t) => ({ value: t, label: ruleLabels[t].label }))} />
        </Form.Item>
        <Form.Item
          name="laneId"
          label="Lane"
          extra={type === 'PreferredLane' ? undefined : 'Leave empty for every lane.'}
          rules={[{ required: type === 'PreferredLane', message: 'Choose the lane' }]}
        >
          <Select aria-label="Lane" allowClear virtual={false} placeholder="Every lane" options={(lanes.data ?? []).map((l) => ({ value: l.id, label: `${l.originCity ?? l.originState} → ${l.destinationCity ?? l.destinationState}` }))} />
        </Form.Item>
        <Form.Item name="reason" label="Why" rules={[{ required: true, whitespace: true, message: 'Say why' }]} extra="Kept with the rule and shown when planning leaves this transporter out.">
          <Input.TextArea aria-label="Why" rows={3} maxLength={300} showCount />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function PlanningRules({ transporterId }: { transporterId: string }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [ending, setEnding] = useState<PlanningRuleDto | null>(null)
  const [reason, setReason] = useState('')
  const rules = useQuery({ queryKey: queryKeys.performance.rules(transporterId), queryFn: () => performanceApi.planningRules(transporterId) })
  const end = useMutation({
    mutationFn: () => performanceApi.endPlanningRule(ending!.id, reason.trim()),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: queryKeys.performance.rules(transporterId) }); setEnding(null); setReason(''); void message.success('Rule ended') },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Card title="Planning rules" extra={<Can permission="transporters.performance.manage"><Button icon={<PlusOutlined />} onClick={() => setAdding(true)}>Add rule</Button></Can>}>
      <Typography.Paragraph type="secondary">Decisions about how this transporter is used in planning. Every rule has a reason and can be ended with one; nothing is deleted.</Typography.Paragraph>
      {rules.isError && <Alert type="error" showIcon title={rules.error.message} />}
      <Table<PlanningRuleDto>
        size="small"
        rowKey="id"
        loading={rules.isLoading}
        pagination={false}
        dataSource={rules.data ?? []}
        locale={{ emptyText: 'No rules: the transporter is treated like any other.' }}
        columns={[
          { title: 'Rule', dataIndex: 'ruleType', render: (t: PlanningRuleType) => <Tag color={ruleLabels[t].color}>{ruleLabels[t].label}</Tag> },
          { title: 'Why', dataIndex: 'reason' },
          { title: 'From', dataIndex: 'effectiveFrom' },
          { title: 'Status', key: 's', render: (_, r) => (r.isActive ? <Tag color="blue">In force</Tag> : <Typography.Text type="secondary">Ended {r.effectiveTo}{r.endedBecause ? `: ${r.endedBecause}` : ''}</Typography.Text>) },
          { title: '', key: 'a', align: 'right', render: (_, r) => (r.isActive ? <Can permission="transporters.performance.manage"><Button size="small" onClick={() => setEnding(r)}>End</Button></Can> : null) },
        ]}
      />
      {adding && <AddRule transporterId={transporterId} onClose={() => setAdding(false)} />}
      <Modal open={ending !== null} title="End this rule?" okText="End rule" okButtonProps={{ disabled: reason.trim() === '' }} confirmLoading={end.isPending} onCancel={() => setEnding(null)} onOk={() => end.mutate()} destroyOnHidden>
        <Typography.Paragraph type="secondary">Say why it is no longer needed; the reason is kept.</Typography.Paragraph>
        <Input.TextArea aria-label="Reason for ending" rows={3} maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />
      </Modal>
    </Card>
  )
}

/** Where a transporter operates, what it can carry, and how planning is told to use it. */
export function CoverageTab({ transporterId }: { transporterId: string }) {
  return (
    <Flex vertical gap={16}>
      <Lanes transporterId={transporterId} />
      <Capabilities transporterId={transporterId} />
      <Can permission="transporters.performance.read"><PlanningRules transporterId={transporterId} /></Can>
    </Flex>
  )
}
