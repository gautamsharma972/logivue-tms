import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Form, Input, Modal } from 'antd'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { TransporterDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'

interface FormValues {
  accountHolder: string
  accountNumber: string
  ifsc: string
  bankName: string
}

export function BankModal({ transporter, open, onClose }: { transporter: TransporterDto; open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  const save = useMutation({
    mutationFn: (v: FormValues) => transportersApi.updateBank(transporter.id, { ...v, version: transporter.version }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success('Bank details saved')
      form.resetFields()
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })

  return (
    <Modal open={open} title="Bank details" okText="Save bank details" confirmLoading={save.isPending} onCancel={onClose} onOk={() => form.submit()} destroyOnHidden>
      <Alert
        type="warning"
        showIcon
        style={{ marginBottom: 16 }}
        title="Payments go to this account. Check the details against the cancelled cheque; every change is recorded in the audit trail."
      />
      <Form<FormValues>
        form={form}
        layout="vertical"
        requiredMark="optional"
        initialValues={{ accountHolder: transporter.bank?.accountHolder ?? transporter.legalName, bankName: transporter.bank?.bankName, ifsc: transporter.bank?.ifsc }}
        onFinish={(v) => save.mutate(v)}
      >
        <Form.Item label="Account holder name" name="accountHolder" rules={[{ required: true, whitespace: true, message: 'Enter the account holder name' }]}>
          <Input />
        </Form.Item>
        <Form.Item
          label="Account number"
          name="accountNumber"
          extra={transporter.bank ? `Current: ${transporter.bank.accountNumberMasked}. Re-enter the full number to change it.` : undefined}
          rules={[{ required: true, message: 'Enter the account number' }, { pattern: /^[0-9A-Za-z ]{6,30}$/, message: '6 to 24 characters' }]}
        >
          <Input autoComplete="off" inputMode="numeric" />
        </Form.Item>
        <Form.Item label="IFSC" name="ifsc" normalize={(v?: string) => v?.toUpperCase().replace(/\s+/g, '')} rules={[{ required: true, message: 'Enter the IFSC' }, { pattern: /^[A-Z]{4}0[A-Z0-9]{6}$/, message: 'Format: HDFC0001234' }]}>
          <Input maxLength={11} />
        </Form.Item>
        <Form.Item label="Bank name" name="bankName" rules={[{ required: true, whitespace: true, message: 'Enter the bank name' }]}>
          <Input />
        </Form.Item>
      </Form>
    </Modal>
  )
}
