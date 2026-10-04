import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Button, Checkbox, Drawer, Flex, Form, Input, Select } from 'antd'
import { useEffect } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ServiceMode, TransporterDto } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { INDIAN_STATES } from '@/lib/indiaStates'

interface FormValues {
  legalName: string
  tradeName?: string
  pan: string
  gstin?: string
  contactPerson: string
  phone: string
  email: string
  addressLine1: string
  addressLine2?: string
  city: string
  state: string
  pincode: string
  serviceModes: ServiceMode[]
}

interface Props {
  open: boolean
  /** The transporter being edited, or null to register a new one. */
  transporter: TransporterDto | null
  onClose: () => void
  onCreated?: (transporter: TransporterDto) => void
}

const upper = (value?: string) => value?.toUpperCase().replace(/\s+/g, '')

export function TransporterFormDrawer({ open, transporter, onClose, onCreated }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const editing = transporter !== null
  // Who the company *is* is fixed once approved, and is never in a vendor's hands.
  const identityLocked = editing && (transporter.status === 'Active' || transporter.status === 'Suspended' || user?.transporterId != null)

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      transporter
        ? {
            legalName: transporter.legalName,
            tradeName: transporter.tradeName ?? undefined,
            pan: transporter.pan,
            gstin: transporter.gstin ?? undefined,
            contactPerson: transporter.contactPerson,
            phone: transporter.phone,
            email: transporter.email,
            addressLine1: transporter.address.line1,
            addressLine2: transporter.address.line2 ?? undefined,
            city: transporter.address.city,
            state: transporter.address.state,
            pincode: transporter.address.pincode,
            serviceModes: transporter.serviceModes,
          }
        : { serviceModes: ['Ftl'] },
    )
  }, [open, transporter, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = {
        legalName: v.legalName,
        tradeName: v.tradeName?.trim() || null,
        pan: v.pan,
        gstin: v.gstin?.trim() || null,
        contactPerson: v.contactPerson,
        phone: v.phone,
        email: v.email,
        addressLine1: v.addressLine1,
        addressLine2: v.addressLine2?.trim() || null,
        city: v.city,
        state: v.state,
        pincode: v.pincode,
        serviceModes: v.serviceModes,
        version: transporter?.version ?? null,
      }
      return transporter ? transportersApi.update(transporter.id, body) : transportersApi.create(body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
      void message.success(editing ? 'Transporter updated' : `Transporter ${saved.code} created`)
      onClose()
      if (!editing) onCreated?.(saved)
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error)) return
      if (error.code === 'concurrency.conflict') {
        await queryClient.invalidateQueries({ queryKey: queryKeys.transporters.all })
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
      maskClosable={!save.isPending}
      title={editing ? 'Edit transporter' : 'New transporter'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>
            {editing ? 'Save changes' : 'Create transporter'}
          </Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" disabled={save.isPending} onFinish={(v) => save.mutate(v)}>
        <Form.Item label="Legal name" name="legalName" rules={[{ required: true, whitespace: true, message: 'Enter the registered legal name' }]} extra={identityLocked ? 'Fixed once approved.' : undefined}>
          <Input disabled={identityLocked} autoFocus />
        </Form.Item>
        <Form.Item label="Trade name" name="tradeName">
          <Input placeholder="If different from the legal name" />
        </Form.Item>

        <Flex gap={16} wrap>
          <Form.Item label="PAN" name="pan" normalize={upper} rules={[{ required: true, message: 'Enter the PAN' }, { pattern: /^[A-Z]{5}[0-9]{4}[A-Z]$/, message: 'Format: ABCDE1234F' }]} style={{ flex: '1 1 200px' }}>
            <Input disabled={identityLocked} maxLength={10} placeholder="ABCDE1234F" />
          </Form.Item>
          <Form.Item label="GSTIN" name="gstin" normalize={upper} rules={[{ pattern: /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$/, message: '15 characters, e.g. 27ABCDE1234F1Z5' }]} extra="Optional if unregistered." style={{ flex: '1 1 260px' }}>
            <Input disabled={identityLocked} maxLength={15} placeholder="27ABCDE1234F1Z5" />
          </Form.Item>
        </Flex>

        <Flex gap={16} wrap>
          <Form.Item label="Contact person" name="contactPerson" rules={[{ required: true, whitespace: true, message: 'Enter a contact person' }]} style={{ flex: '1 1 200px' }}>
            <Input />
          </Form.Item>
          <Form.Item label="Mobile" name="phone" rules={[{ required: true, message: 'Enter a mobile number' }]} style={{ flex: '1 1 200px' }}>
            <Input inputMode="tel" placeholder="98765 43210" />
          </Form.Item>
        </Flex>
        <Form.Item label="Email" name="email" rules={[{ required: true, message: 'Enter an email' }, { type: 'email', message: 'Enter a valid email' }]}>
          <Input inputMode="email" />
        </Form.Item>

        <Form.Item label="Address" name="addressLine1" rules={[{ required: true, whitespace: true, message: 'Enter the address' }]} style={{ marginBottom: 8 }}>
          <Input placeholder="Building, street" />
        </Form.Item>
        <Form.Item name="addressLine2">
          <Input placeholder="Area, landmark (optional)" />
        </Form.Item>
        <Flex gap={16} wrap>
          <Form.Item label="City" name="city" rules={[{ required: true, whitespace: true, message: 'Enter the city' }]} style={{ flex: '1 1 160px' }}>
            <Input />
          </Form.Item>
          <Form.Item label="State" name="state" rules={[{ required: true, message: 'Choose the state' }]} style={{ flex: '1 1 200px' }}>
            <Select showSearch optionFilterProp="label" options={INDIAN_STATES.map((s) => ({ value: s, label: s }))} />
          </Form.Item>
          <Form.Item label="Pincode" name="pincode" rules={[{ required: true, message: 'Enter the pincode' }, { pattern: /^[1-9][0-9]{5}$/, message: '6 digits' }]} style={{ flex: '1 1 120px' }}>
            <Input maxLength={6} inputMode="numeric" />
          </Form.Item>
        </Flex>

        <Form.Item label="Services offered" name="serviceModes" rules={[{ required: true, type: 'array', min: 1, message: 'Choose at least one' }]}>
          <Checkbox.Group
            options={[
              { value: 'Ftl', label: 'Full truck load (FTL)' },
              { value: 'Ptl', label: 'Part load (PTL)' },
              { value: 'Dedicated', label: 'Dedicated vehicle' },
            ]}
          />
        </Form.Item>
      </Form>
    </Drawer>
  )
}
