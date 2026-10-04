import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Drawer, Flex, Form, Input, InputNumber, Select, Switch, Typography } from 'antd'
import { useEffect, useMemo } from 'react'
import { approvalsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { PolicyDto, PolicyStepDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

const MAX_STEPS = 10

interface FormValues {
  isActive: boolean
  steps: Array<{ name?: string; requiredPermission?: string; minAmount?: number | null }>
}

interface Props {
  open: boolean
  policy: PolicyDto | null
  onClose: () => void
}

export function PolicyDrawer({ open, policy, onClose }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  const permissions = useQuery({ queryKey: queryKeys.approvals.stepPermissions, queryFn: approvalsApi.stepPermissions, enabled: open })
  const options = useMemo(() => {
    const groups = new Map<string, { value: string; label: string }[]>()
    permissions.data?.forEach((p) => groups.set(p.module, [...(groups.get(p.module) ?? []), { value: p.code, label: `${p.description} (${p.code})` }]))
    return [...groups.entries()].map(([label, opts]) => ({ label, options: opts }))
  }, [permissions.data])

  useEffect(() => {
    if (!open || !policy) return
    form.resetFields()
    form.setFieldsValue({
      isActive: policy.isConfigured ? policy.isActive : true,
      steps: policy.steps.length ? policy.steps : [{ name: '', requiredPermission: undefined, minAmount: null }],
    })
  }, [open, policy, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const steps: PolicyStepDto[] = v.steps.map((s) => ({
        name: (s.name ?? '').trim(),
        requiredPermission: s.requiredPermission ?? '',
        minAmount: s.minAmount ?? null,
      }))
      return approvalsApi.savePolicy(policy!.documentType, { isActive: v.isActive, steps, version: policy!.version })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.policies })
      void message.success('Approval policy saved')
      onClose()
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      if (error.code === 'concurrency.conflict') {
        await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.policies })
        onClose()
      }
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={640}
      destroyOnHidden
      title={policy ? `${policy.documentTypeName} approval` : 'Approval policy'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>Save policy</Button>
        </Flex>
      }
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        title="Steps run in order. A step applies when the document amount reaches its threshold; a step without a threshold always applies. Anyone holding the step's permission can decide it — except the requester, and nobody can decide two steps of one request."
      />
      <Form<FormValues> form={form} layout="vertical" requiredMark={false} onFinish={(v) => save.mutate(v)} disabled={save.isPending}>
        <Form.Item label="Policy active" name="isActive" valuePropName="checked" extra="While inactive, documents of this type cannot be submitted for approval.">
          <Switch />
        </Form.Item>

        <Form.List name="steps">
          {(fields, { add, remove, move }) => (
            <Flex vertical gap={12}>
              {fields.map((field, index) => (
                <Card
                  key={field.key}
                  size="small"
                  title={`Step ${index + 1}`}
                  extra={
                    <Flex gap={4}>
                      <Button type="text" size="small" aria-label="Move step up" icon={<ArrowUpOutlined />} disabled={index === 0} onClick={() => move(index, index - 1)} />
                      <Button type="text" size="small" aria-label="Move step down" icon={<ArrowDownOutlined />} disabled={index === fields.length - 1} onClick={() => move(index, index + 1)} />
                      <Button type="text" size="small" danger aria-label="Remove step" icon={<DeleteOutlined />} disabled={fields.length === 1} onClick={() => remove(field.name)} />
                    </Flex>
                  }
                >
                  <Form.Item label="Step name" name={[field.name, 'name']} rules={[{ required: true, whitespace: true, message: 'Name this step' }, { max: 100 }]}>
                    <Input placeholder="e.g. Regional manager" />
                  </Form.Item>
                  <Form.Item label="Who can decide" name={[field.name, 'requiredPermission']} rules={[{ required: true, message: 'Choose the permission required' }]}>
                    <Select showSearch optionFilterProp="label" loading={permissions.isLoading} options={options} placeholder="Select a permission" />
                  </Form.Item>
                  <Form.Item
                    label="Applies from amount"
                    name={[field.name, 'minAmount']}
                    extra="Leave empty to always apply."
                    style={{ marginBottom: 0 }}
                  >
                    <InputNumber<number> min={0} precision={2} style={{ width: 240 }} prefix="₹" controls={false} formatter={(v) => (v === undefined || v === null ? '' : `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ','))} parser={(v) => Number((v ?? '').replace(/,/g, ''))} />
                  </Form.Item>
                </Card>
              ))}
              <Button type="dashed" icon={<PlusOutlined />} disabled={fields.length >= MAX_STEPS} onClick={() => add({ name: '', minAmount: null })}>
                Add step
              </Button>
              {fields.length >= MAX_STEPS && <Typography.Text type="secondary">A policy can have at most {MAX_STEPS} steps.</Typography.Text>}
            </Flex>
          )}
        </Form.List>
      </Form>
    </Drawer>
  )
}
