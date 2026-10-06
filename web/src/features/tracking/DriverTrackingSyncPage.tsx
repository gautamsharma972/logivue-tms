import { App, Alert, Button, Descriptions, Table } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useDriverTracking } from './driverTracking'
import { clock } from './shared'

/** What the phone is holding because there was no signal, and a way to send it now. Nothing is dropped until the server has it. */
export function DriverTrackingSyncPage() {
  const t = useDriverTracking()
  const { message } = App.useApp()
  const [busy, setBusy] = useState(false)
  const send = async () => {
    setBusy(true)
    try {
      const r = await t.sync()
      if (r.failed) void message.warning('Could not reach the server. Your locations are safe on this phone and will be sent later.')
      else void message.success(r.sent === 0 ? 'Nothing was waiting' : `Sent ${r.sent} location(s)`)
    } finally {
      setBusy(false)
    }
  }
  const oldest = t.pending.length ? t.pending.map((p) => p.capturedAtUtc).sort()[0] : null
  return (
    <>
      <PageHeader title="Unsent locations" description="Locations captured while there was no signal. They are sent by themselves; you can also send them now." actions={<Link to="/tracking/drive">Back to my trips</Link>} />
      <Descriptions bordered size="small" column={1} style={{ marginBottom: 12 }}>
        <Descriptions.Item label="Waiting">{t.waiting}</Descriptions.Item>
        <Descriptions.Item label="Oldest">{oldest ? clock(oldest) : '—'}</Descriptions.Item>
        <Descriptions.Item label="Network">{t.network}</Descriptions.Item>
        <Descriptions.Item label="Last sent">{t.lastSentAt ? clock(new Date(t.lastSentAt).toISOString()) : 'Not since this page opened'}</Descriptions.Item>
      </Descriptions>
      <Button type="primary" onClick={() => void send()} loading={busy} disabled={t.waiting === 0} style={{ marginBottom: 12 }}>Send now</Button>
      {t.waiting === 0 && <Alert type="success" showIcon message="Everything has been sent." />}
      {t.waiting > 0 && (
        <Table size="small" rowKey="id" pagination={{ pageSize: 10 }} dataSource={t.pending} columns={[
          { title: 'Captured', dataIndex: 'capturedAtUtc', render: (v: string) => dayjs(v).format('DD MMM HH:mm:ss') },
          { title: 'Trip', dataIndex: 'tripReference' },
          { title: 'Position', render: (_, r) => `${r.latitude.toFixed(5)}, ${r.longitude.toFixed(5)}` },
        ]} />
      )}
    </>
  )
}
