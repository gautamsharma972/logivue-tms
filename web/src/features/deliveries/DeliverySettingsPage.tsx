import { MinusCircleOutlined, PlusOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Flex, Form, Input, InputNumber, Select, Skeleton, Switch, Tabs, Tag, Typography } from 'antd'
import type { ReactNode } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DeliverySettingDto } from '@/lib/api/types'
import { exceptionTypeLabel } from './shared'

function SettingCard<T extends object>({ setting, title, help, toForm, fromForm, children }: {
  setting: DeliverySettingDto
  title: string
  help: string
  toForm?: (value: Record<string, unknown>) => T
  fromForm?: (values: T) => unknown
  children: ReactNode
}) {
  const [form] = Form.useForm<T>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const initial = (toForm ? toForm(setting.value as Record<string, unknown>) : setting.value) as T
  const save = useMutation({
    mutationFn: (values: T) => deliveriesApi.saveSetting(setting.key, fromForm ? fromForm(values) : values),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.settings })
      void message.success(`${title} saved`)
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
  return (
    <Card title={title} extra={setting.isCustomised ? <Tag color="blue">Customised</Tag> : <Tag>Default</Tag>}>
      <Typography.Paragraph type="secondary">{help}</Typography.Paragraph>
      <Form<T> form={form} layout="vertical" initialValues={initial} onFinish={(v) => save.mutate(v)}>
        {children}
        <Button type="primary" htmlType="submit" loading={save.isPending}>Save</Button>
      </Form>
    </Card>
  )
}

const percent = (v: unknown) => Math.round(Number(v) * 100)

function ReasonList({ setting, title, help, withEvidence }: { setting: DeliverySettingDto; title: string; help: string; withEvidence?: boolean }) {
  return (
    <SettingCard setting={setting} title={title} help={help} toForm={(v) => ({ reasons: v as unknown as { code: string; name: string; evidenceRequired: boolean }[] })} fromForm={(v: { reasons: unknown[] }) => v.reasons}>
      <Form.List name="reasons">
        {(fields, { add, remove }) => (
          <>
            {fields.map((field) => (
              <Flex key={field.key} gap={8} wrap align="start">
                <Form.Item name={[field.name, 'code']} rules={[{ required: true, message: 'Code' }]}><Input aria-label="Code" placeholder="CODE" style={{ width: 200 }} /></Form.Item>
                <Form.Item name={[field.name, 'name']} rules={[{ required: true, message: 'Name' }]}><Input aria-label="Name" placeholder="Name" style={{ width: 260 }} /></Form.Item>
                {withEvidence && <Form.Item name={[field.name, 'evidenceRequired']} valuePropName="checked"><Checkbox>Photo required</Checkbox></Form.Item>}
                <Button aria-label="Remove" type="text" icon={<MinusCircleOutlined />} onClick={() => remove(field.name)} />
              </Flex>
            ))}
            <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ code: '', name: '', evidenceRequired: false })} style={{ marginBottom: 16 }}>Add</Button>
          </>
        )}
      </Form.List>
    </SettingCard>
  )
}

export function DeliverySettingsPage() {
  const settings = useQuery({ queryKey: queryKeys.deliveries.settings, queryFn: () => deliveriesApi.settings() })
  if (settings.isLoading) return <Skeleton active />
  if (settings.isError) return <Alert type="error" showIcon title={settings.error.message} />
  const by = (key: string) => settings.data!.find((s) => s.key === key)!

  const sw = (name: string, label: string, extra?: string) => <Form.Item name={name} label={label} valuePropName="checked" extra={extra}><Switch /></Form.Item>

  return (
    <>
      <PageHeader title="Delivery rules" description="What proof a delivery needs, what is accepted without a person, how fast things must happen, and the reasons drivers can choose from." />
      <Tabs
        items={[
          {
            key: 'proof',
            label: 'Proof of delivery',
            children: (
              <Flex vertical gap={16}>
                <SettingCard setting={by('pod.rules')} title="Evidence required" help="What a driver must capture before a proof can be submitted. A tick means required for every delivery.">
                  {sw('signatureRequired', 'Signature')}
                  {sw('otpRequired', 'One-time code from the customer', 'The code is sent to the customer\'s email when the vehicle arrives.')}
                  {sw('gpsRequired', 'Location (GPS)')}
                  {sw('photoRequired', 'Photo')}
                  <Form.Item name="minPhotos" label="Photos needed"><InputNumber min={0} max={20} /></Form.Item>
                  {sw('geofenceRequired', 'A usable location at the customer', 'A delivery outside the customer\'s geofence is always flagged for review; this also flags one with no usable fix.')}
                  {sw('contactlessAllowed', 'Allow contactless delivery', 'The driver confirms, with a photo and the location, when nobody is there to sign.')}
                  {sw('galleryAllowed', 'Allow photos from the gallery')}
                  <Flex gap={16} wrap>
                    <Form.Item name="maxGpsAccuracyM" label="Location is vague beyond (metres)"><InputNumber min={1} max={10000} /></Form.Item>
                    <Form.Item name="otpValidityMinutes" label="Code valid for (minutes)"><InputNumber min={1} max={1440} /></Form.Item>
                    <Form.Item name="otpMaxAttempts" label="Wrong tries allowed"><InputNumber min={1} max={20} /></Form.Item>
                  </Flex>
                </SettingCard>
                <SettingCard setting={by('pod.autoAccept')} title="Accepting without a reviewer" help="Off by default for any shortage or damage: those always go to a person unless you allow otherwise here.">
                  {sw('enabled', 'Accept a clean proof automatically')}
                  {sw('requireOcr', 'Only if the paper POD was read and agrees')}
                  {sw('allowWithDiscrepancy', 'Also when there is a shortage or damage')}
                </SettingCard>
                <SettingCard setting={by('pod.images')} title="Photo quality" help="Photos that are corrupted are always refused. Small ones are kept with a warning unless you choose to refuse them.">
                  <Flex gap={16} wrap>
                    <Form.Item name="minWidth" label="Minimum width (px)"><InputNumber min={0} /></Form.Item>
                    <Form.Item name="minHeight" label="Minimum height (px)"><InputNumber min={0} /></Form.Item>
                    <Form.Item name="maxBytes" label="Largest file (bytes)"><InputNumber min={10000} max={10485760} /></Form.Item>
                  </Flex>
                  {sw('rejectLowResolution', 'Refuse low-resolution photos')}
                </SettingCard>
              </Flex>
            ),
          },
          {
            key: 'quantities',
            label: 'Quantities and discrepancies',
            children: (
              <Flex vertical gap={16}>
                <SettingCard setting={by('delivery.quantity')} title="Quantity reconciliation" help="Delivered + short + damaged + rejected should equal what was dispatched. A difference is always shown exactly as reported.">
                  <Form.Item name="overDeliveryPct" label="Delivered may exceed dispatched by (%)"><InputNumber min={0} max={100} /></Form.Item>
                  {sw('blockUnreconciledCompletion', 'Do not allow a delivery whose quantities do not add up', 'Otherwise it completes and raises a quantity-mismatch exception.')}
                </SettingCard>
                <SettingCard setting={by('delivery.discrepancy')} title="When something is short, damaged or refused" help="">
                  {sw('shortageAcknowledgementRequired', 'Customer must acknowledge a shortage')}
                  {sw('damageAcknowledgementRequired', 'Customer must acknowledge damage')}
                  {sw('refusalAcknowledgementRequired', 'Customer must acknowledge a refusal')}
                  {sw('autoCreateClaim', 'Raise a claim automatically', 'The claim is handed to the claims connection with the evidence. Until the claims module is connected, it is recorded here.')}
                </SettingCard>
              </Flex>
            ),
          },
          {
            key: 'ocr',
            label: 'Paper POD (OCR)',
            children: (
              <SettingCard
                setting={by('pod.ocr')} title="Reading the paper POD" help="Confidence is judged per kind of field: the fields that identify the delivery are held to a higher standard than free text."
                toForm={(v) => ({ ...v, criticalThreshold: percent(v.criticalThreshold), standardThreshold: percent(v.standardThreshold), optionalThreshold: percent(v.optionalThreshold), reviewBelow: percent(v.reviewBelow) })}
                fromForm={(v: Record<string, unknown>) => ({ ...v, criticalThreshold: Number(v.criticalThreshold) / 100, standardThreshold: Number(v.standardThreshold) / 100, optionalThreshold: Number(v.optionalThreshold) / 100, reviewBelow: Number(v.reviewBelow) / 100 })}
              >
                {sw('enabled', 'Read uploaded paper PODs')}
                <Flex gap={16} wrap>
                  <Form.Item name="criticalThreshold" label="Identifying fields (%)"><InputNumber min={0} max={100} /></Form.Item>
                  <Form.Item name="standardThreshold" label="Other fields (%)"><InputNumber min={0} max={100} /></Form.Item>
                  <Form.Item name="optionalThreshold" label="Remarks (%)"><InputNumber min={0} max={100} /></Form.Item>
                  <Form.Item name="reviewBelow" label="Whole document below (%)"><InputNumber min={0} max={100} /></Form.Item>
                </Flex>
                <Form.Item name="criticalFields" label="Identifying fields"><Select mode="tags" /></Form.Item>
                <Form.Item name="optionalFields" label="Remark fields"><Select mode="tags" /></Form.Item>
              </SettingCard>
            ),
          },
          {
            key: 'sla',
            label: 'Targets and exceptions',
            children: (
              <Flex vertical gap={16}>
                <SettingCard setting={by('pod.sla')} title="Proof targets" help="Hours from delivery to submission, from submission to a decision, and from rejection to a corrected proof.">
                  <Flex gap={16} wrap>
                    <Form.Item name="podSubmissionHours" label="Submit within (hours)"><InputNumber min={1} /></Form.Item>
                    <Form.Item name="podReviewHours" label="Review within (hours)"><InputNumber min={1} /></Form.Item>
                    <Form.Item name="resubmissionHours" label="Correct within (hours)"><InputNumber min={1} /></Form.Item>
                  </Flex>
                </SettingCard>
                <SettingCard
                  setting={by('pod.ageing')} title="Ageing buckets" help="The top of each bucket in whole days, in increasing order. Anything older than the last one is shown as one more bucket."
                  toForm={(v) => ({ upperDays: ((v.upperDays as number[] | undefined) ?? []).map(String) })}
                  fromForm={(v: { upperDays: (string | number)[] }) => ({ upperDays: v.upperDays.map(Number).filter((n) => Number.isInteger(n) && n > 0).sort((a, b) => a - b) })}
                >
                  <Form.Item name="upperDays" label="Days"><Select mode="tags" tokenSeparators={[',', ' ']} placeholder="1, 3, 7, 15, 30" /></Form.Item>
                </SettingCard>
                <SettingCard setting={by('pod.billing')} title="Billing" help="How the proof status reaches freight audit.">
                  {sw('holdInvoiceUntilAccepted', 'Hold the invoice until the proof is accepted', 'Off: a completed delivery can be billed straight away.')}
                </SettingCard>
                <SettingCard setting={by('delivery.exceptions')} title="Exceptions" help="How serious each kind is when it is raised, and how long before it is due or should be escalated.">
                  <Flex gap={16} wrap>
                    <Form.Item name="dueHours" label="Due after (hours)"><InputNumber min={1} /></Form.Item>
                    <Form.Item name="escalateAfterHours" label="Escalate after (hours)"><InputNumber min={1} /></Form.Item>
                  </Flex>
                  <Flex gap={12} wrap>
                    {Object.entries(exceptionTypeLabel).map(([type, label]) => (
                      <Form.Item key={type} name={['severity', type]} label={label}><Select style={{ width: 130 }} options={['Low', 'Medium', 'High', 'Critical'].map((v) => ({ value: v, label: v }))} /></Form.Item>
                    ))}
                  </Flex>
                </SettingCard>
              </Flex>
            ),
          },
          {
            key: 'reasons',
            label: 'Reasons',
            children: (
              <Flex vertical gap={16}>
                <ReasonList setting={by('delivery.attemptReasons')} title="Why a delivery could not be made" help="Offered to the driver for a failed attempt or a failed delivery." />
                <ReasonList setting={by('delivery.shortageReasons')} title="Shortage reasons" help="Never assigns blame by itself: the cause is established later." />
                <ReasonList setting={by('delivery.damageTypes')} title="Damage types" help="A damage type marked 'photo required' needs a photo of the damage before the proof can be submitted." withEvidence />
                <ReasonList setting={by('delivery.refusalReasons')} title="Why a customer refuses" help="Offered to the driver when a customer will not accept the goods." />
              </Flex>
            ),
          },
        ]}
      />
    </>
  )
}
