import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Collapse, Flex, Input, InputNumber, Modal, Select, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { shipmentsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { FleetOptionDto, InviteeStatus, ShipmentDto, TenderDto, TenderInviteeDto } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'

const inviteeColor: Record<InviteeStatus, string> = {
  Waiting: 'default', Sent: 'gold', Bid: 'purple', Accepted: 'green', Rejected: 'red', Expired: 'volcano', Superseded: 'default', Cancelled: 'default',
}

const inviteeLabel: Record<InviteeStatus, string> = {
  Waiting: 'Waiting its turn', Sent: 'Awaiting answer', Bid: 'Bid placed', Accepted: 'Accepted', Rejected: 'Declined', Expired: 'No answer in time', Superseded: 'Another carrier awarded', Cancelled: 'Cancelled',
}

function useTenderMutation<T>(fn: (v: T) => Promise<TenderDto>, success: string, onDone?: () => void) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.shipments.all })
      void message.success(success)
      onDone?.()
    },
    onError: (e) => void message.error(toApiError(e).message),
  })
}

function ReasonModal({ title, label, okText, pending, onOk, onClose }: { title: string; label: string; okText: string; pending: boolean; onOk: (reason: string) => void; onClose: () => void }) {
  const [reason, setReason] = useState('')
  return (
    <Modal open title={title} okText={okText} okButtonProps={{ disabled: reason.trim().length === 0 }} confirmLoading={pending} onOk={() => onOk(reason.trim())} onCancel={onClose} destroyOnHidden>
      <Input.TextArea rows={3} maxLength={500} aria-label={label} placeholder={label} value={reason} onChange={(e) => setReason(e.target.value)} />
    </Modal>
  )
}

function BidModal({ shipment, onClose, onBid }: { shipment: ShipmentDto; onClose: () => void; onBid: (v: { vehicleId: string; driverId: string; counterRate: number | null; comments: string | null }) => void }) {
  const id = shipment.summary.id
  const options = useQuery({ queryKey: queryKeys.shipments.fleet(id), queryFn: () => shipmentsApi.fleetOptions(id) })
  const [vehicleId, setVehicleId] = useState<string>()
  const [driverId, setDriverId] = useState<string>()
  const [rate, setRate] = useState<number | null>(null)
  const toOption = (o: FleetOptionDto) => ({ value: o.id, disabled: !o.isOk, label: `${o.label}${o.issues.length ? ` — ${o.issues.join('; ')}` : ''}` })
  return (
    <Modal open title={`Bid for ${shipment.summary.number}`} okText="Place bid" okButtonProps={{ disabled: !vehicleId || !driverId }} onOk={() => vehicleId && driverId && onBid({ vehicleId, driverId, counterRate: rate, comments: null })} onCancel={onClose} destroyOnHidden>
      <p>Offer a vehicle and driver for this load. A planner compares the bids and awards one.</p>
      {options.isError && <Alert type="error" showIcon title={options.error.message} />}
      <Flex vertical gap={12}>
        <Select aria-label="Vehicle" placeholder="Vehicle" virtual={false} loading={options.isLoading} options={options.data?.vehicles.map(toOption)} value={vehicleId} onChange={setVehicleId} />
        <Select aria-label="Driver" placeholder="Driver" virtual={false} loading={options.isLoading} options={options.data?.drivers.map(toOption)} value={driverId} onChange={setDriverId} />
        <InputNumber aria-label="Your rate (optional)" placeholder="Your rate, if different from the contract (optional)" style={{ width: '100%' }} min={1} value={rate} onChange={setRate} />
      </Flex>
    </Modal>
  )
}

function CounterModal({ pending, onOk, onClose }: { pending: boolean; onOk: (rate: number, comments: string | null) => void; onClose: () => void }) {
  const [rate, setRate] = useState<number | null>(null)
  const [comments, setComments] = useState('')
  return (
    <Modal open title="Propose a different rate" okText="Send counter-offer" okButtonProps={{ disabled: !rate || rate <= 0 }} confirmLoading={pending} onOk={() => rate && onOk(rate, comments.trim() || null)} onCancel={onClose} destroyOnHidden>
      <Flex vertical gap={12}>
        <InputNumber aria-label="Proposed rate" placeholder="Proposed rate (₹)" style={{ width: '100%' }} min={1} value={rate} onChange={setRate} />
        <Input.TextArea rows={2} maxLength={500} aria-label="Comment" placeholder="Why? (optional)" value={comments} onChange={(e) => setComments(e.target.value)} />
      </Flex>
    </Modal>
  )
}

type Dialog = { kind: 'bid' } | { kind: 'counter' } | { kind: 'decline' } | { kind: 'cancel' } | { kind: 'decide'; invitee: TenderInviteeDto; agree: boolean } | null

/** The latest tender of a shipment: who was invited, what each answered, and the buttons for whoever can act. */
export function TenderPanel({ shipment, canPlan, canRespond, isVendor }: { shipment: ShipmentDto; canPlan: boolean; canRespond: boolean; isVendor: boolean }) {
  const id = shipment.summary.id
  const tenders = useQuery({ queryKey: queryKeys.shipments.tenders(id), queryFn: () => shipmentsApi.tenders(id) })
  const [dialog, setDialog] = useState<Dialog>(null)
  const close = () => setDialog(null)

  const award = useTenderMutation((inviteeId: string) => shipmentsApi.awardTender(id, inviteeId), 'Load awarded')
  const decide = useTenderMutation((v: { inviteeId: string; agree: boolean; comments: string | null }) => shipmentsApi.decideCounter(id, v.inviteeId, v.agree, v.comments), 'Counter-offer decided', close)
  const cancel = useTenderMutation((reason: string) => shipmentsApi.cancelTender(id, reason), 'Tender cancelled', close)
  const bid = useTenderMutation((v: { vehicleId: string; driverId: string; counterRate: number | null; comments: string | null }) => shipmentsApi.bid(id, v), 'Bid placed', close)
  const counter = useTenderMutation((v: { rate: number; comments: string | null }) => shipmentsApi.counterOffer(id, v.rate, v.comments), 'Counter-offer sent', close)
  const decline = useTenderMutation((reason: string) => shipmentsApi.declineTender(id, reason), 'Load declined', close)

  const tender = tenders.data?.[0]
  if (!tender) return null
  const open = tender.status === 'Open'
  const broadcast = tender.mode === 'Broadcast'
  const mine = isVendor ? tender.invitees[0] : undefined

  const columns = [
    ...(isVendor ? [] : [{ title: '#', dataIndex: 'sequence', width: 40 }]),
    ...(isVendor ? [] : [{ title: 'Transporter', key: 't', render: (_: unknown, i: TenderInviteeDto) => <><Typography.Text strong>{i.transporterName}</Typography.Text><br /><Typography.Text type="secondary">{i.contractReference}</Typography.Text></> }]),
    { title: 'Status', key: 's', render: (_: unknown, i: TenderInviteeDto) => <><Tag color={inviteeColor[i.status]}>{inviteeLabel[i.status]}</Tag>{i.reason && <Typography.Text type="secondary" style={{ display: 'block' }}>{i.reason}</Typography.Text>}</> },
    { title: 'Answer by', dataIndex: 'deadline', render: (v: string | null, i: TenderInviteeDto) => (i.status === 'Sent' ? formatDateTime(v) : '—') },
    ...(isVendor ? [] : [{ title: 'Contract price', dataIndex: 'quotedTotal', align: 'right' as const, render: (v: number | null) => (v === null ? '—' : formatInrExact(v)) }]),
    { title: 'Bid / counter', key: 'b', render: (_: unknown, i: TenderInviteeDto) => (
      <Flex vertical gap={2}>
        {i.bidVehicleRegistration && <span>{i.bidVehicleRegistration} · {i.bidDriverName}</span>}
        {i.counterRate !== null && <span>Proposes {formatInrExact(i.counterRate)} {i.counterStatus === 'Pending' ? <Tag color="gold">Awaiting decision</Tag> : <Tag color={i.counterStatus === 'Agreed' ? 'green' : 'red'}>{i.counterStatus}</Tag>}</span>}
        {i.counterComment && <Typography.Text type="secondary">{i.counterComment}</Typography.Text>}
      </Flex>
    ) },
    ...(canPlan && open ? [{ title: '', key: 'a', align: 'right' as const, render: (_: unknown, i: TenderInviteeDto) => (
      <Flex gap={4} justify="flex-end" wrap>
        {i.counterStatus === 'Pending' && (i.status === 'Sent' || i.status === 'Bid') && (
          <>
            <Button size="small" onClick={() => setDialog({ kind: 'decide', invitee: i, agree: true })}>Agree rate</Button>
            <Button size="small" danger onClick={() => setDialog({ kind: 'decide', invitee: i, agree: false })}>Decline rate</Button>
          </>
        )}
        {broadcast && i.status === 'Bid' && i.counterStatus !== 'Pending' && <Button size="small" type="primary" loading={award.isPending} onClick={() => award.mutate(i.id)}>Award</Button>}
      </Flex>
    ) }] : []),
  ]

  return (
    <Card
      title={`Tender ${tender.number}`}
      extra={
        <Flex gap={8} align="center">
          <Tag>{tender.mode === 'Sequential' ? 'One after another' : 'All at once'}</Tag>
          <Tag color={tender.status === 'Open' ? 'gold' : tender.status === 'Awarded' ? 'green' : 'default'}>{tender.status}</Tag>
          {canPlan && open && <Button size="small" danger onClick={() => setDialog({ kind: 'cancel' })}>Cancel tender</Button>}
        </Flex>
      }
    >
      {tender.status === 'Exhausted' && <Alert type="warning" showIcon style={{ marginBottom: 12 }} title={tender.closeReason ?? 'Nobody took the load.'} />}
      {tender.status === 'Cancelled' && <Alert type="info" showIcon style={{ marginBottom: 12 }} title={`Cancelled: ${tender.closeReason ?? ''}`} />}
      {isVendor && open && mine && ['Sent', 'Bid'].includes(mine.status) && canRespond && (
        <Flex gap={8} style={{ marginBottom: 12 }} wrap>
          {broadcast && mine.status === 'Sent' && <Button type="primary" onClick={() => setDialog({ kind: 'bid' })}>Bid…</Button>}
          {mine.status === 'Sent' && mine.counterStatus !== 'Pending' && <Button onClick={() => setDialog({ kind: 'counter' })}>Propose a different rate…</Button>}
          {broadcast && mine.status === 'Sent' && <Button danger onClick={() => setDialog({ kind: 'decline' })}>Decline</Button>}
        </Flex>
      )}
      <Table<TenderInviteeDto> size="small" rowKey="id" pagination={false} dataSource={tender.invitees} columns={columns} scroll={{ x: 'max-content' }} />
      {tender.events.length > 0 && (
        <Collapse ghost size="small" style={{ marginTop: 8 }} items={[{
          key: 'log',
          label: 'History',
          children: (
            <Flex vertical gap={2}>
              {tender.events.map((e, n) => (
                <Typography.Text key={n} type="secondary">{formatDateTime(e.at)} · {e.type}{e.transporterName ? ` · ${e.transporterName}` : ''}{e.comments ? ` · ${e.comments}` : ''}</Typography.Text>
              ))}
            </Flex>
          ),
        }]} />
      )}

      {dialog?.kind === 'bid' && <BidModal shipment={shipment} onClose={close} onBid={(v) => bid.mutate(v)} />}
      {dialog?.kind === 'counter' && <CounterModal pending={counter.isPending} onOk={(rate, comments) => counter.mutate({ rate, comments })} onClose={close} />}
      {dialog?.kind === 'decline' && <ReasonModal title="Decline this load?" label="Why are you declining?" okText="Decline" pending={decline.isPending} onOk={(r) => decline.mutate(r)} onClose={close} />}
      {dialog?.kind === 'cancel' && <ReasonModal title="Cancel this tender?" label="Why is the tender being cancelled?" okText="Cancel tender" pending={cancel.isPending} onOk={(r) => cancel.mutate(r)} onClose={close} />}
      {dialog?.kind === 'decide' && (
        <ReasonModal
          title={dialog.agree ? `Agree ${dialog.invitee.transporterName}'s rate?` : `Decline ${dialog.invitee.transporterName}'s rate?`}
          label="Comment" okText={dialog.agree ? 'Agree' : 'Decline'} pending={decide.isPending}
          onOk={(r) => decide.mutate({ inviteeId: dialog.invitee.id, agree: dialog.agree, comments: r })} onClose={close}
        />
      )}
    </Card>
  )
}
