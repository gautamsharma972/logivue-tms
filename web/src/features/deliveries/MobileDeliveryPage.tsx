import { CameraOutlined, DeleteOutlined } from '@ant-design/icons'
import { App, Alert, Button, Card, Checkbox, Descriptions, Empty, Flex, Input, InputNumber, Radio, Select, Tag, Typography } from 'antd'
import { useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import type { DeliveryDto, DeliveryOutcome, DeviceContext, EvidenceType, ItemQuantityRequest, ProofMethod, RemainingDisposition } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { currentFix } from './geo'
import { SyncBanner } from './MobileDeliveriesPage'
import { useOffline } from './offline/OfflineProvider'
import { localStatus } from './offline/sync'
import { DeliveryStatusTag, qty } from './shared'
import { QrScanButton, canScanQr } from './QrScanner'
import { SignaturePad } from './SignaturePad'

type Choice = DeliveryOutcome

const choices: { value: Choice; label: string }[] = [
  { value: 'Full', label: 'Delivered in full' },
  { value: 'Partial', label: 'Partially delivered' },
  { value: 'Shortage', label: 'Shortage' },
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Refused', label: 'Customer refused' },
  { value: 'Failed', label: 'Delivery failed' },
]

interface Line {
  delivered: number
  short: number
  damaged: number
  rejected: number
  shortReason?: string
  damageType?: string
  damageReason?: string
}

interface Photo {
  id: string
  type: EvidenceType
  blob: Blob
  preview: string
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return <Card size="small" title={title}>{children}</Card>
}

/**
 * The driver's screen for one delivery. Everything tapped here is saved on the phone first and sent when there is a signal; nothing waits for the network.
 * The server checks it all again when it arrives.
 */
export function MobileDeliveryPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { message } = App.useApp()
  const { bundle, commands, manager, deviceReference, refresh, sync, online } = useOffline()
  const entry = bundle?.deliveries.find((d) => d.delivery.summary.id === id)
  const mine = commands.filter((c) => c.deliveryId === id)
  const config = bundle?.config

  const [busy, setBusy] = useState(false)
  const [outcome, setOutcome] = useState<Choice>('Full')
  const [lines, setLines] = useState<Record<string, Line>>({})
  const [disposition, setDisposition] = useState<RemainingDisposition>()
  const [reason, setReason] = useState<string>()
  const [remarks, setRemarks] = useState('')
  const [method, setMethod] = useState<ProofMethod>('Photo')
  const [recipient, setRecipient] = useState('')
  const [designation, setDesignation] = useState('')
  const [phone, setPhone] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [acknowledged, setAcknowledged] = useState(false)
  const [photos, setPhotos] = useState<Photo[]>([])
  const [signature, setSignature] = useState<Blob | null>(null)
  const [code, setCode] = useState('')
  const [codeOk, setCodeOk] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const photoType = useRef<EvidenceType>('PackagePhoto')

  const delivery: DeliveryDto | undefined = entry?.delivery
  const status = delivery ? localStatus(delivery.summary.status, mine) : undefined
  const line = (itemId: string, dispatched: number): Line => lines[itemId] ?? { delivered: dispatched, short: 0, damaged: 0, rejected: 0 }
  const setLine = (itemId: string, dispatched: number, patch: Partial<Line>) => setLines((all) => ({ ...all, [itemId]: { ...line(itemId, dispatched), ...patch } }))

  const items = delivery?.items ?? []
  const totals = useMemo(() => items.map((i) => {
    const l = lines[i.id] ?? { delivered: i.dispatchedQuantity, short: 0, damaged: 0, rejected: 0 }
    return { id: i.id, sku: i.sku, left: i.dispatchedQuantity - (l.delivered + l.short + l.damaged + l.rejected) }
  }), [items, lines])
  const anyDamage = items.some((i) => (lines[i.id]?.damaged ?? 0) > 0)

  if (!bundle) return <Empty description="Connect once to download your deliveries" />
  if (!delivery || !config || !status) return <Alert type="warning" showIcon title="This delivery is not on this phone." action={<Link to="/driver">Back</Link>} />

  const context = async (): Promise<DeviceContext> => ({ fix: await currentFix(), deviceReference, at: new Date().toISOString() })

  const enqueue = async (type: Parameters<typeof manager.enqueue>[0], payload: unknown) => {
    await manager.enqueue(type, id, payload)
    await refresh()
    void sync()
  }

  const simple = (type: 'start' | 'arrive') => async () => {
    setBusy(true)
    try {
      await enqueue(type, await context())
      if (type === 'arrive' && config.pod.otpRequired) void message.info('A code has been sent to the customer when this reaches the server.')
    } finally {
      setBusy(false)
    }
  }

  const verifyCode = async () => {
    setBusy(true)
    try {
      if (online) {
        await deliveriesApi.verifyOtp(id, code.trim(), await context())
        setCodeOk(true)
        void message.success('Code confirmed')
      } else {
        await enqueue('otp-verify', { code: code.trim(), context: await context() })
        setCodeOk(true)
        void message.info('Code saved. It is checked when you are back online.')
      }
    } catch (e) {
      void message.error(toApiError(e).message)
    } finally {
      setBusy(false)
    }
  }

  const addPhoto = (type: EvidenceType) => {
    photoType.current = type
    fileInput.current?.click()
  }

  const onFile = (file: File | undefined) => {
    if (!file) return
    setPhotos((all) => [...all, { id: crypto.randomUUID(), type: photoType.current, blob: file, preview: URL.createObjectURL(file) }])
    if (fileInput.current) fileInput.current.value = ''
  }

  const reasonsFor = outcome === 'Refused' ? config.refusalReasons : config.attemptReasons

  const submit = async () => {
    setBusy(true)
    try {
      const ctx = await context()
      if (outcome === 'Failed' || outcome === 'Refused') {
        if (!reason) return void message.warning('Choose a reason')
        if (outcome === 'Failed') await enqueue('fail', { reasonCode: reason, remarks: remarks.trim() || null, context: ctx })
        else await enqueue('refuse', { reasonCode: reason, recipientName: recipient.trim() || null, remarks: remarks.trim() || null, customerAcknowledged: acknowledged, context: ctx })
        void message.success('Saved on this phone')
        return navigate('/driver')
      }

      if (!recipient.trim()) return void message.warning('Enter the name of the person who received the goods')
      if (method === 'Signature' && !signature) return void message.warning('The recipient needs to sign')
      if ((method === 'Otp' || method === 'Qr') && !codeOk && !delivery.otpVerified) return void message.warning('Enter and confirm the customer\'s code')
      if (config.pod.photoRequired && photos.filter((p) => p.type !== 'PodDocument').length < config.pod.minPhotos) return void message.warning('Take a photo of the delivery')
      if (anyDamage && !photos.some((p) => p.type === 'DamagePhoto')) return void message.warning('Take a photo of the damage')
      if (totals.some((t) => t.left !== 0)) return void message.warning('The quantities do not add up to what was dispatched')

      const quantities: ItemQuantityRequest[] = items.map((i) => {
        const l = line(i.id, i.dispatchedQuantity)
        return { itemId: i.id, deliveredQuantity: l.delivered, shortQuantity: l.short, damagedQuantity: l.damaged, rejectedQuantity: l.rejected, shortageReasonCode: l.short > 0 ? (l.shortReason ?? null) : null, damageType: l.damaged > 0 ? (l.damageType ?? null) : null, damageReason: l.damaged > 0 ? (l.damageReason ?? null) : null, damageDescription: null, remarks: null }
      })
      await enqueue('complete', {
        outcome, items: quantities, remainingDisposition: outcome === 'Partial' ? (disposition ?? null) : null, driverRemarks: remarks.trim() || null,
        proof: { method, recipientName: recipient.trim(), recipientDesignation: designation.trim() || null, recipientPhone: phone.trim() || null, recipientRemarks: null, driverConfirmed: confirmed, customerAcknowledged: acknowledged }, context: ctx,
      })
      for (const p of photos) await manager.enqueueUpload({ deliveryId: id, kind: 'evidence', evidenceType: p.type, signerName: null, blob: p.blob, fix: ctx.fix })
      if (signature) await manager.enqueueUpload({ deliveryId: id, kind: 'signature', evidenceType: null, signerName: recipient.trim(), blob: signature, fix: ctx.fix })
      await manager.enqueueSubmit(id)
      await refresh()
      void sync()
      void message.success('Saved on this phone')
      navigate('/driver')
    } catch (e) {
      void message.error(toApiError(e).message)
    } finally {
      setBusy(false)
    }
  }

  const open = status === 'Arrived' || status === 'Attempted'
  const troubled = mine.filter((c) => c.status === 'Failed' || c.status === 'Conflict')

  return (
    <Flex vertical gap={12} style={{ maxWidth: 560, margin: '0 auto' }}>
      <Link to="/driver">← My deliveries</Link>
      <Typography.Title level={3} style={{ margin: 0 }}>{delivery.summary.number}</Typography.Title>
      <SyncBanner />
      <Card size="small">
        <Flex justify="space-between" align="start">
          <div>
            <Typography.Text strong style={{ fontSize: 16 }}>{delivery.summary.customerName}</Typography.Text>
            <br />
            <Typography.Text type="secondary">{delivery.destinationAddress ?? ''}</Typography.Text>
            <br />
            <Typography.Text type="secondary">Window {formatDateTime(delivery.windowStart)} – {formatDateTime(delivery.windowEnd ?? delivery.summary.plannedDeliveryAt)}</Typography.Text>
          </div>
          <DeliveryStatusTag status={status} />
        </Flex>
      </Card>

      {troubled.map((c) => (
        <Alert key={c.id} type="error" showIcon title={`⚠ ${c.type}: ${c.error ?? 'could not be sent'}`}
          action={<Flex gap={4}><Button size="small" onClick={() => void manager.retry(c.id).then(refresh).then(() => sync())}>Try again</Button>{c.status === 'Conflict' && <Button size="small" danger onClick={() => void manager.discard(c.id).then(refresh)}>Discard</Button>}</Flex>} />
      ))}

      {status === 'Assigned' && <Button type="primary" size="large" block loading={busy} onClick={simple('start')}>Start delivery</Button>}
      {status === 'EnRoute' && <Button type="primary" size="large" block loading={busy} onClick={simple('arrive')}>I have arrived</Button>}
      {status === 'Attempted' && <Button size="large" block loading={busy} onClick={simple('arrive')}>I am back at the customer</Button>}

      {open && (
        <>
          <Section title="What happened?">
            <Radio.Group value={outcome} onChange={(e) => setOutcome(e.target.value)} style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {choices.map((c) => <Radio key={c.value} value={c.value} style={{ fontSize: 16 }}>{c.label}</Radio>)}
            </Radio.Group>
          </Section>

          {(outcome === 'Failed' || outcome === 'Refused') ? (
            <Section title={outcome === 'Failed' ? 'Why could it not be delivered?' : 'Why did the customer refuse?'}>
              <Flex vertical gap={12}>
                <Select size="large" aria-label="Reason" placeholder="Choose a reason" value={reason} onChange={setReason} options={reasonsFor.map((r) => ({ value: r.code, label: r.name }))} />
                {outcome === 'Refused' && <Input size="large" aria-label="Who refused" placeholder="Name of the person who refused" value={recipient} onChange={(e) => setRecipient(e.target.value)} />}
                <Input.TextArea rows={2} aria-label="Remarks" placeholder="Remarks" value={remarks} onChange={(e) => setRemarks(e.target.value)} />
                {outcome === 'Refused' && <Checkbox checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)}>The customer acknowledges the refusal</Checkbox>}
                <Button danger size="large" block loading={busy} onClick={() => void submit()}>{outcome === 'Failed' ? 'Mark as failed' : 'Record the refusal'}</Button>
                {outcome === 'Failed' && <Button size="large" block loading={busy} onClick={async () => { if (!reason) return void message.warning('Choose a reason'); setBusy(true); try { await enqueue('attempt', { reasonCode: reason, driverRemarks: remarks.trim() || null, customerRemarks: null, recipientName: null, context: await context() }); void message.success('Attempt saved. You can try again.') } finally { setBusy(false) } }}>Record a failed attempt and try again</Button>}
              </Flex>
            </Section>
          ) : (
            <>
              <Section title="Quantities">
                <Flex vertical gap={14}>
                  {items.map((i) => {
                    const l = line(i.id, i.dispatchedQuantity)
                    const left = totals.find((t) => t.id === i.id)!.left
                    return (
                      <div key={i.id}>
                        <Typography.Text strong>{i.sku}</Typography.Text> <Typography.Text type="secondary">{i.description} · sent {qty(i.dispatchedQuantity)} {i.unitOfMeasure}</Typography.Text>
                        <Flex gap={8} wrap style={{ marginTop: 6 }}>
                          <InputNumber aria-label={`Delivered ${i.sku}`} addonBefore="Delivered" min={0} value={l.delivered} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { delivered: v ?? 0 })} />
                          {(outcome === 'Shortage' || l.short > 0) && <InputNumber aria-label={`Short ${i.sku}`} addonBefore="Short" min={0} value={l.short} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { short: v ?? 0 })} />}
                          {(outcome === 'Damaged' || l.damaged > 0) && <InputNumber aria-label={`Damaged ${i.sku}`} addonBefore="Damaged" min={0} value={l.damaged} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { damaged: v ?? 0 })} />}
                          {(outcome === 'Partial' || l.rejected > 0) && <InputNumber aria-label={`Not delivered ${i.sku}`} addonBefore="Not delivered" min={0} value={l.rejected} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { rejected: v ?? 0 })} />}
                        </Flex>
                        {l.short > 0 && <Select style={{ marginTop: 6, width: '100%' }} aria-label={`Shortage reason ${i.sku}`} placeholder="Why is it short?" value={l.shortReason} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { shortReason: v })} options={config.shortageReasons.map((r) => ({ value: r.code, label: r.name }))} />}
                        {l.damaged > 0 && (
                          <Flex vertical gap={6} style={{ marginTop: 6 }}>
                            <Select aria-label={`Damage type ${i.sku}`} placeholder="Type of damage" value={l.damageType} onChange={(v) => setLine(i.id, i.dispatchedQuantity, { damageType: v })} options={config.damageTypes.map((r) => ({ value: r.code, label: r.evidenceRequired ? `${r.name} (photo needed)` : r.name }))} />
                            <Input aria-label={`Damage reason ${i.sku}`} placeholder="What happened?" value={l.damageReason} onChange={(e) => setLine(i.id, i.dispatchedQuantity, { damageReason: e.target.value })} />
                          </Flex>
                        )}
                        {left !== 0 && <Typography.Text type="danger" style={{ display: 'block' }}>{left > 0 ? `${qty(left)} not accounted for` : `${qty(-left)} more than was sent`}</Typography.Text>}
                      </div>
                    )
                  })}
                  {outcome === 'Partial' && (
                    <Select size="large" aria-label="What happens to the rest" placeholder="What happens to the goods not delivered?" value={disposition} onChange={setDisposition}
                      options={(['Backorder', 'Reschedule', 'Return', 'Cancel', 'Exception'] as RemainingDisposition[]).map((v) => ({ value: v, label: v === 'Exception' ? 'Raise it as an exception' : v }))} />
                  )}
                </Flex>
              </Section>

              <Section title="Who received it?">
                <Flex vertical gap={10}>
                  <Input size="large" aria-label="Recipient name" placeholder="Name" value={recipient} onChange={(e) => setRecipient(e.target.value)} />
                  <Flex gap={8}>
                    <Input aria-label="Designation" placeholder="Designation" value={designation} onChange={(e) => setDesignation(e.target.value)} />
                    <Input aria-label="Mobile" inputMode="tel" placeholder="Mobile" value={phone} onChange={(e) => setPhone(e.target.value)} />
                  </Flex>
                  <Select size="large" aria-label="How was it confirmed" value={method} onChange={setMethod} options={[
                    { value: 'Photo', label: 'Photo' }, { value: 'Signature', label: 'Signature' }, { value: 'Otp', label: 'Customer\'s code' }, { value: 'Qr', label: 'Customer\'s QR code' },
                    ...(config.pod.contactlessAllowed ? [{ value: 'Contactless' as const, label: 'Contactless (nobody to sign)' }] : []),
                  ]} />
                  {method === 'Signature' && <SignaturePad onChange={setSignature} />}
                  {(method === 'Otp' || method === 'Qr') && (
                    <Flex gap={8}>
                      {method === 'Qr' && canScanQr() && !codeOk && !delivery.otpVerified && <QrScanButton onCode={setCode} />}
                      <Input size="large" aria-label="Customer's code" inputMode="numeric" maxLength={8} placeholder="Code from the customer" value={code} onChange={(e) => setCode(e.target.value)} disabled={codeOk || delivery.otpVerified} />
                      <Button size="large" type="primary" disabled={code.trim().length < 4 || codeOk || delivery.otpVerified} loading={busy} onClick={() => void verifyCode()}>{codeOk || delivery.otpVerified ? 'Confirmed' : 'Confirm'}</Button>
                    </Flex>
                  )}
                  {method === 'Contactless' && <Checkbox checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)}>I confirm the goods were left safely and nobody could sign</Checkbox>}
                  {(outcome !== 'Full' || anyDamage) && <Checkbox checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)}>The customer acknowledges the shortage or damage</Checkbox>}
                </Flex>
              </Section>

              <Section title="Photos">
                <input ref={fileInput} type="file" accept="image/*" capture="environment" hidden aria-label="Take a photo" onChange={(e) => onFile(e.target.files?.[0])} />
                <Flex gap={8} wrap>
                  <Button icon={<CameraOutlined />} size="large" onClick={() => addPhoto('PackagePhoto')}>Goods</Button>
                  {anyDamage && <Button icon={<CameraOutlined />} size="large" danger onClick={() => addPhoto('DamagePhoto')}>Damage</Button>}
                  <Button icon={<CameraOutlined />} size="large" onClick={() => addPhoto('SitePhoto')}>Place</Button>
                  <Button icon={<CameraOutlined />} size="large" onClick={() => addPhoto('PodDocument')}>Paper POD</Button>
                </Flex>
                <Flex gap={8} wrap style={{ marginTop: 10 }}>
                  {photos.map((p) => (
                    <div key={p.id} style={{ position: 'relative' }}>
                      <img src={p.preview} alt={p.type} style={{ width: 96, height: 96, objectFit: 'cover', borderRadius: 8 }} />
                      <Tag style={{ position: 'absolute', left: 4, bottom: 4, margin: 0 }}>{p.type.replace('Photo', '')}</Tag>
                      <Button aria-label="Retake" size="small" shape="circle" icon={<DeleteOutlined />} style={{ position: 'absolute', right: 2, top: 2 }} onClick={() => setPhotos((all) => all.filter((x) => x.id !== p.id))} />
                    </div>
                  ))}
                </Flex>
              </Section>

              <Section title="Remarks"><Input.TextArea rows={2} aria-label="Driver remarks" placeholder="Anything to add" value={remarks} onChange={(e) => setRemarks(e.target.value)} /></Section>
              <Button type="primary" size="large" block loading={busy} onClick={() => void submit()}>Confirm delivery</Button>
              <Typography.Text type="secondary" style={{ textAlign: 'center' }}>{online ? 'It is sent as soon as you confirm.' : 'No signal: it is saved on this phone and sent when you are back online.'}</Typography.Text>
            </>
          )}
        </>
      )}

      {(status === 'Delivered' || status === 'PartiallyDelivered' || status === 'Failed' || status === 'Refused') && (
        <Card size="small" title="Done">
          <Descriptions column={1} size="small">
            <Descriptions.Item label="Status">{mine.some((c) => c.status === 'Pending') ? '⟳ Pending sync' : '✓ Synced'}</Descriptions.Item>
            {entry?.pod && <Descriptions.Item label="Proof">{entry.pod.summary.status}</Descriptions.Item>}
          </Descriptions>
          {entry?.pod && <Link to={`/delivery/pods/${entry.pod.summary.id}`}>Open the proof</Link>}
        </Card>
      )}
    </Flex>
  )
}
