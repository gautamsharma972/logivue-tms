import { DownloadOutlined } from '@ant-design/icons'
import { App, Button, Dropdown } from 'antd'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { toApiError } from '@/lib/api/errors'
import { reportsApi } from './api'

const LABEL: Record<string, string> = { csv: 'CSV (plain table)', xlsx: 'Excel workbook', pdf: 'PDF document' }

interface Props {
  code: string
  formats: string[]
  filters: Record<string, string>
  groupBy?: string[]
  sort: { field: string; direction: 'ASC' | 'DESC' } | null
}

/** Export in a format the report offers. A small one downloads at once; a big one is built in the background and shows up under My exports. */
export function ReportExportMenu({ code, formats, filters, groupBy, sort }: Props) {
  const { message } = App.useApp()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const run = async (format: string) => {
    setBusy(true)
    try {
      const outcome = await reportsApi.exportReport(code, { filters, groupBy, sort: sort ? [sort] : undefined, format })
      if (outcome.immediate) {
        await reportsApi.download(outcome.job)
        void message.success(`Exported ${outcome.job.rowCount?.toLocaleString('en-IN') ?? ''} rows`)
      } else {
        void message.info({ content: 'This export is large, so it is being prepared in the background. You will find it under My exports.', duration: 6, onClick: () => navigate('/reports/exports') })
      }
    } catch (e) {
      void message.error(toApiError(e).message)
    } finally {
      setBusy(false)
    }
  }
  return (
    <Dropdown
      menu={{ items: formats.map((f) => ({ key: f, label: LABEL[f] ?? f.toUpperCase(), onClick: () => void run(f) })) }}
      trigger={['click']}
    >
      <Button icon={<DownloadOutlined />} loading={busy}>
        Export
      </Button>
    </Dropdown>
  )
}
