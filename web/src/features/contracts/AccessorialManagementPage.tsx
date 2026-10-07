import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { App, Button, Flex, Form, Input, Modal, Select, Switch, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AccessorialCalc, AccessorialTypeDto } from '@/lib/api/types'

const calcLabel: Record<AccessorialCalc, string> = { Fixed: 'One amount', PerUnit: 'A rate per unit', Tiered: 'Bands of quantity', PercentOfFreight: 'A share of the freight', Reimbursed: 'Passed through at cost' }

/** The catalogue of extra charges (detention, toll, night halt…). Contracts price the ones they allow; the catalogue says what each one is and how it is worked out. */
export function AccessorialManagementPage() {
  const { can } = useAuth()
  const { message } = App.useApp()
  const client = useQueryClient()
  const [editing, setEditing] = useState<AccessorialTypeDto | 'new' | null>(null)
  const types = useQuery({ queryKey: queryKeys.freight.accessorials, queryFn: freightApi.accessorials })
  const save = useMutation({
    mutationFn: (v: { code: string; name: string; description?: string; calc: AccessorialCalc; unit: string; isActive?: boolean }) => {
      const body = { code: v.code, name: v.name, description: v.description ?? null, calc: v.calc, unit: v.unit, isActive: v.isActive ?? true }
      return editing && editing !== 'new' ? freightApi.updateAccessorial(editing.id, body) : freightApi.createAccessorial(body)
    },
    onSuccess: () => { setEditing(null); void client.invalidateQueries({ queryKey: queryKeys.freight.accessorials }) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const manage = can('contracts.manage')
  return (
    <>
      <PageHeader title="Extra charges" description="Detention, waiting, extra stops, toll and the rest. Each contract sets its own rates, included quantities and triggers for the charges it allows."
        actions={manage ? <Button type="primary" onClick={() => setEditing('new')}>New charge</Button> : undefined} />
      <Table<AccessorialTypeDto> size="small" rowKey="id" loading={types.isLoading} dataSource={types.data ?? []} pagination={{ pageSize: 20, hideOnSinglePage: true }} columns={[
        { title: 'Code', dataIndex: 'code' }, { title: 'Name', dataIndex: 'name' }, { title: 'Worked out as', dataIndex: 'calc', render: (c: AccessorialCalc) => calcLabel[c] }, { title: 'Unit', dataIndex: 'unit' },
        { title: 'Status', dataIndex: 'isActive', render: (a: boolean) => (a ? <Tag color="green">Active</Tag> : <Tag>Off</Tag>) },
        { title: '', render: (_, r) => manage && <Button size="small" onClick={() => setEditing(r)}>Edit</Button> },
      ]} />
      <Typography.Paragraph type="secondary" style={{ marginTop: 12 }}>The extra charges a shipment actually incurred (hours of detention, kilometres over, a toll amount) are given when it is rated, or taken from delivery and tracking once those supply them.</Typography.Paragraph>
      <Modal open={!!editing} title={editing === 'new' ? 'New charge' : 'Edit charge'} footer={null} onCancel={() => setEditing(null)} destroyOnHidden>
        <Form layout="vertical" initialValues={editing && editing !== 'new' ? editing : { calc: 'PerUnit', isActive: true }} onFinish={(v) => save.mutate(v as never)}>
          <Form.Item name="code" label="Code" rules={[{ required: true }]}><Input disabled={editing !== 'new'} /></Form.Item>
          <Form.Item name="name" label="Name" rules={[{ required: true }]}><Input /></Form.Item>
          <Form.Item name="description" label="Description"><Input.TextArea rows={2} /></Form.Item>
          <Flex gap={8}>
            <Form.Item name="calc" label="Worked out as" style={{ flex: 1 }}><Select options={Object.entries(calcLabel).map(([value, label]) => ({ value, label }))} /></Form.Item>
            <Form.Item name="unit" label="Unit" rules={[{ required: true }]} style={{ flex: 1 }}><Input placeholder="HOUR, KM, STOP…" /></Form.Item>
          </Flex>
          {editing !== 'new' && <Form.Item name="isActive" label="Active" valuePropName="checked"><Switch /></Form.Item>}
          <Button type="primary" htmlType="submit" loading={save.isPending}>Save</Button>
        </Form>
      </Modal>
    </>
  )
}
