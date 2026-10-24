import { Alert, Button, Card, Descriptions, Flex, Result, Skeleton, Tag, Typography } from 'antd'
import { AppFooter } from '@/components/AppFooter'
import { PhotoPicker } from './PhotoPicker'
import { Logo } from '@/components/Logo'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { qrApi, QrError, type QrViewDto } from '@/lib/api/opsApi'
import { when } from './fmt'

type Notice = { kind: 'error' | 'success' | 'info'; text: string }

/** The phone's position, requested only when the driver taps Confirm Arrival. It is never asked for on opening the page, and the other steps never call it. */
function readPosition(timeoutMs = 15_000): Promise<{ latitude: number; longitude: number; accuracyM: number; capturedAt: string } | { error: 'denied' | 'unavailable' }> {
  if (typeof navigator === 'undefined' || !navigator.geolocation) return Promise.resolve({ error: 'unavailable' })
  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracyM: p.coords.accuracy, capturedAt: new Date(p.timestamp).toISOString() }),
      (e) => resolve({ error: e.code === 1 ? 'denied' : 'unavailable' }),
      { enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 0 },
    )
  })
}

const big = { height: 64, fontSize: 20, fontWeight: 600 } as const

/**
 * What a driver sees after scanning the QR code on the LR: no sign-in, no app. It shows the next step the server says is allowed, and nothing else. The server decides every step;
 * this page only asks.
 */
export function QrPage() {
  const { token = '' } = useParams()
  const [latest, setLatest] = useState<QrViewDto>()
  const [gone, setGone] = useState(false)
  const [busy, setBusy] = useState<string>()
  const [notice, setNotice] = useState<Notice>()
  const [files, setFiles] = useState<File[]>([])
  const [failed, setFailed] = useState(false)

  const first = useQuery({ queryKey: ['ops', 'qr', token], queryFn: () => qrApi.view(token), retry: false, refetchOnWindowFocus: false })
  const view = latest ?? first.data
  const invalid = gone || (first.error instanceof QrError && first.error.status === 404)
  const setView = setLatest
  const setInvalid = setGone

  const run = async (name: string, call: () => Promise<QrViewDto>, success?: string) => {
    setBusy(name); setNotice(undefined)
    try {
      const next = await call()
      setView(next)
      if (success) setNotice({ kind: 'success', text: success })
      return true
    } catch (e) {
      if (e instanceof QrError && e.status === 404) setInvalid(true)
      else setNotice({ kind: 'error', text: e instanceof QrError ? e.message : 'Could not reach the server. Check your signal and try again.' })
      return false
    } finally { setBusy(undefined) }
  }

  const confirmArrival = async () => {
    setBusy('arrival'); setNotice(undefined)
    const pos = await readPosition() // the one and only place a location is requested
    setBusy(undefined)
    if ('error' in pos) {
      await run('arrival', () => qrApi.arrive(token, { latitude: null, longitude: null, accuracyM: null, capturedAt: null, locationError: pos.error }))
      return
    }
    await run('arrival', () => qrApi.arrive(token, { latitude: pos.latitude, longitude: pos.longitude, accuracyM: pos.accuracyM, capturedAt: pos.capturedAt, locationError: null }), 'Arrival confirmed.')
  }

  const upload = async () => {
    const ok = await run('pod', () => qrApi.uploadPod(token, files), 'Thank you. Your proof of delivery has been received.')
    setFailed(!ok)
    if (ok) setFiles([])
  }

  if (invalid) return <Result status="404" title="This code is not valid" subTitle="It may have expired or been replaced. Please ask the office for a new LR." />
  if (first.isError && !view) return <Result status="warning" title="Could not load this page" subTitle="Check your signal and open the code again." extra={<Button onClick={() => void first.refetch()}>Try again</Button>} />
  if (!view) return <div style={{ maxWidth: 520, margin: '0 auto', padding: 16 }}><Skeleton active /></div>

  const action = view.nextAction
  return (
    <div style={{ maxWidth: 520, margin: '0 auto', padding: 16, paddingBottom: 48 }}>
      <Logo height={56} />
      <Typography.Title level={2} style={{ margin: '4px 0 12px' }}>LR {view.lrNumber}</Typography.Title>
      <Tag color={view.status === 'UnloadingCompleted' ? 'green' : view.status === 'Cancelled' ? 'default' : 'blue'} style={{ fontSize: 16, padding: '4px 12px' }}>{view.statusText}</Tag>

      <Card size="small" style={{ margin: '16px 0' }}>
        <Descriptions column={1} size="small" colon={false} labelStyle={{ width: 110 }}>
          <Descriptions.Item label="Customer">{view.customerName}</Descriptions.Item>
          <Descriptions.Item label="Vehicle">{view.vehicleRegistration ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="From">{view.cfaName}</Descriptions.Item>
          <Descriptions.Item label="Deliver to"><b>{view.destinationName}</b>, {view.destinationCity}</Descriptions.Item>
          <Descriptions.Item label="Goods">{view.material}</Descriptions.Item>
          {view.arrivedAt && <Descriptions.Item label="Arrived">{when(view.arrivedAt)}</Descriptions.Item>}
          {view.unloadedAt && <Descriptions.Item label="Unloaded">{when(view.unloadedAt)}</Descriptions.Item>}
        </Descriptions>
      </Card>

      {notice && <Alert style={{ marginBottom: 16 }} showIcon type={notice.kind} title={notice.text} />}
      {view.podRejectionReason && action === 'UploadPod' && <Alert style={{ marginBottom: 16 }} showIcon type="warning" title={`The proof of delivery was not accepted: ${view.podRejectionReason}`} />}
      {!notice && <Typography.Paragraph style={{ fontSize: 17 }}>{view.message}</Typography.Paragraph>}

      {action === 'ConfirmArrival' && (
        <Flex vertical gap={8}>
          <Button type="primary" block style={big} loading={busy === 'arrival'} onClick={() => void confirmArrival()}>Confirm Arrival</Button>
          <Typography.Text type="secondary">Tap this only when you are at the delivery point. Your phone will ask to share its location; this is the only step that needs it.</Typography.Text>
        </Flex>
      )}

      {action === 'ConfirmUnloading' && (
        <Button type="primary" block style={big} loading={busy === 'unloading'} onClick={() => void run('unloading', () => qrApi.unload(token), 'Unloading confirmed.')}>Confirm Unloading Completed</Button>
      )}

      {action === 'UploadPod' && (
        <Card size="small" title="Proof of delivery (POD)">
          <Flex vertical gap={10}>
            <PhotoPicker files={files} onChange={(next) => { setFiles(next); setFailed(false) }} max={view.maxFiles} />
            <Button type="primary" block style={big} disabled={files.length === 0} loading={busy === 'pod'} onClick={() => void upload()}>{failed ? 'Try again' : 'Upload POD'}</Button>
            {view.canMarkPodPending && <Button block style={{ height: 52, fontSize: 17 }} loading={busy === 'pending'} onClick={() => void run('pending', () => qrApi.podPending(token), 'Noted. The office will upload the POD later.')}>POD not available yet</Button>}
            <Typography.Text type="secondary">Photos or a PDF, up to {view.maxFiles} files of {view.maxFileMb} MB each.</Typography.Text>
          </Flex>
        </Card>
      )}

      {action === 'None' && view.podStatus === 'Accepted' && <Result status="success" title="All done" subTitle="Delivery complete and proof accepted." />}
      <AppFooter style={{ marginTop: 32 }} />
    </div>
  )
}
