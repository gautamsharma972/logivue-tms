import { DeleteOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Form, Input, Popconfirm, Table, Typography } from 'antd'
import { Can } from '@/features/auth/AuthContext'
import { planningApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { CompatibilityRuleDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

interface FormValues {
  categoryA: string
  categoryB: string
  reason?: string
}

/** Pairs of product categories that must never share a vehicle. The planner keeps them apart and says so when it has to. */
export function CompatibilityTab() {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<FormValues>()
  const rules = useQuery({ queryKey: queryKeys.planning.compatibility, queryFn: () => planningApi.compatibilityRules() })

  const add = useMutation({
    mutationFn: (v: FormValues) => planningApi.addCompatibilityRule({ categoryA: v.categoryA, categoryB: v.categoryB, reason: v.reason?.trim() || null }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.planning.compatibility })
      form.resetFields()
      void message.success('Rule added')
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => planningApi.deleteCompatibilityRule(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.planning.compatibility })
      void message.success('Rule removed')
    },
    onError: (e) => void message.error(toApiError(e).message),
  })

  return (
    <Flex vertical gap={16}>
      <Alert
        type="info"
        showIcon
        title="Incompatible products"
        description="Orders whose product categories appear together here are never put on the same vehicle. Hazardous goods are also kept apart from other goods unless you switch that off for a run."
      />
      <Can permission="shipments.plan">
        <Card size="small" title="Add a rule">
          <Form<FormValues> form={form} layout="inline" onFinish={(v) => add.mutate(v)}>
            <Form.Item name="categoryA" rules={[{ required: true, message: 'Enter a category' }]}><Input aria-label="First category" placeholder="e.g. FOOD" maxLength={50} /></Form.Item>
            <Typography.Text type="secondary" style={{ alignSelf: 'center', marginInlineEnd: 16 }}>never with</Typography.Text>
            <Form.Item name="categoryB" rules={[{ required: true, message: 'Enter a category' }]}><Input aria-label="Second category" placeholder="e.g. CHEMICALS" maxLength={50} /></Form.Item>
            <Form.Item name="reason"><Input aria-label="Reason" placeholder="Reason (optional)" maxLength={300} style={{ width: 260 }} /></Form.Item>
            <Button type="primary" htmlType="submit" loading={add.isPending}>Add rule</Button>
          </Form>
        </Card>
      </Can>
      <Table<CompatibilityRuleDto>
        size="small"
        rowKey="id"
        loading={rules.isLoading}
        pagination={false}
        dataSource={rules.data ?? []}
        locale={{ emptyText: 'No rules yet: any products may share a vehicle.' }}
        columns={[
          { title: 'Category', dataIndex: 'categoryA' },
          { title: 'Must not share a vehicle with', dataIndex: 'categoryB' },
          { title: 'Reason', dataIndex: 'reason', render: (r: string | null) => r ?? '—' },
          {
            title: '',
            key: 'a',
            align: 'right',
            render: (_, r) => (
              <Can permission="shipments.plan">
                <Popconfirm title="Remove this rule?" okText="Remove" onConfirm={() => remove.mutate(r.id)}>
                  <Button size="small" danger icon={<DeleteOutlined />} aria-label={`Remove rule ${r.categoryA} / ${r.categoryB}`} />
                </Popconfirm>
              </Can>
            ),
          },
        ]}
      />
    </Flex>
  )
}
