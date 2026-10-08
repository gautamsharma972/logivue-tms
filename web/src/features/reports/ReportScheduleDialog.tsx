import { useMutation, useQueryClient } from '@tanstack/react-query'
import { App, Form, Input, InputNumber, Modal, Select, TimePicker } from 'antd'
import dayjs from 'dayjs'
import { applyFieldErrors } from '@/lib/formErrors'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import { reportsApi } from './api'

const ZONES = ['Asia/Kolkata', 'UTC', 'Asia/Dubai', 'Asia/Singapore', 'Europe/London', 'America/New_York']
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

interface FormValues {
  name: string
  scheduleType: 'Daily' | 'Weekly' | 'Monthly' | 'Custom'
  time: dayjs.Dayjs | null
  dayOfWeek: string
  dayOfMonth: number
  everyMinutes: number
  timeZone: string
  format: string
  recipients: string[]
}

interface Props {
  open: boolean
  onClose: () => void
  code: string
  reportName: string
  formats: string[]
  filters: Record<string, string>
}

/** Schedule the report as it is now (its filters included): when, in whose time zone, in which format and who is told. */
export function ReportScheduleDialog({ open, onClose, code, reportName, formats, filters }: Props) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<FormValues>()
  const type = Form.useWatch('scheduleType', form)
  const save = useMutation({
    mutationFn: (v: FormValues) =>
      reportsApi.createSubscription({
        reportCode: code,
        name: v.name || undefined,
        filters,
        scheduleType: v.scheduleType,
        schedule: { time: v.time?.format('HH:mm') ?? '08:00', dayOfWeek: v.scheduleType === 'Weekly' ? v.dayOfWeek : null, dayOfMonth: v.scheduleType === 'Monthly' ? v.dayOfMonth : null, everyMinutes: v.scheduleType === 'Custom' ? v.everyMinutes : null },
        format: v.format,
        timeZone: v.timeZone,
        recipients: v.recipients,
      }),
    onSuccess: async (s) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.reports.subscriptions })
      void message.success(`Scheduled: ${s.summary ?? s.name}`)
      onClose()
    },
    onError: (e) => {
      const error = toApiError(e)
      if (!applyFieldErrors(form, error)) void message.error(error.message)
    },
  })
  return (
    <Modal title={`Schedule “${reportName}”`} open={open} onCancel={onClose} okText="Schedule" confirmLoading={save.isPending} onOk={() => void form.validateFields().then((v) => save.mutate(v))} destroyOnHidden>
      <Form<FormValues>
        form={form}
        layout="vertical"
        initialValues={{ scheduleType: 'Daily', time: dayjs('08:00', 'HH:mm'), dayOfWeek: 'Monday', dayOfMonth: 1, everyMinutes: 60, timeZone: 'Asia/Kolkata', format: formats.includes('xlsx') ? 'xlsx' : formats[0], recipients: [] }}
      >
        <Form.Item name="name" label="Name (optional)">
          <Input maxLength={160} placeholder={reportName} />
        </Form.Item>
        <Form.Item name="scheduleType" label="How often" rules={[{ required: true }]}>
          <Select options={['Daily', 'Weekly', 'Monthly', 'Custom'].map((v) => ({ value: v, label: v }))} />
        </Form.Item>
        {type === 'Weekly' && (
          <Form.Item name="dayOfWeek" label="Day of the week">
            <Select options={DAYS.map((d) => ({ value: d, label: d }))} />
          </Form.Item>
        )}
        {type === 'Monthly' && (
          <Form.Item name="dayOfMonth" label="Day of the month" extra="0 means the last day of the month.">
            <InputNumber min={0} max={31} />
          </Form.Item>
        )}
        {type === 'Custom' ? (
          <Form.Item name="everyMinutes" label="Every (minutes)" extra="At least 15 minutes.">
            <InputNumber min={15} max={10080} />
          </Form.Item>
        ) : (
          <Form.Item name="time" label="At" rules={[{ required: true }]}>
            <TimePicker format="HH:mm" minuteStep={5} allowClear={false} />
          </Form.Item>
        )}
        <Form.Item name="timeZone" label="Time zone">
          <Select showSearch options={ZONES.map((z) => ({ value: z, label: z }))} />
        </Form.Item>
        <Form.Item name="format" label="Format">
          <Select options={formats.map((f) => ({ value: f, label: f.toUpperCase() }))} />
        </Form.Item>
        <Form.Item name="recipients" label="Tell these people (emails)" extra="They get a link to open the report with their own access; the data itself is never in the email.">
          <Select mode="tags" tokenSeparators={[',', ' ']} open={false} placeholder="name@company.com" />
        </Form.Item>
      </Form>
    </Modal>
  )
}
