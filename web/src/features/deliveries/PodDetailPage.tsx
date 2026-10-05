import { EditOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, Descriptions, Empty, Flex, Input, Modal, Row, Select, Skeleton, Table, Tag, Typography } from 'antd'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { AuditPanel } from '@/features/audit/AuditPanel'
import { useAuth } from '@/features/auth/AuthContext'
import { deliveriesApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { DeliveryDto, OcrFieldDto, OcrResultDto, PodDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { AuthFile } from './AuthFile'
import { OcrFieldMark, ProofStatusTag, ValidationMark, exceptionTypeOptions, geofenceLabel, ocrStatusLabel, pct, qty } from './shared'

interface PodView {
  pod: PodDto
  delivery: DeliveryDto | null
  documentEvidenceId: string | null
  ocr: OcrResultDto | null
}

type Dialog = { kind: 'reject' } | { kind: 'resubmission' } | { kind: 'correction' } | { kind: 'exception' } | { kind: 'edit'; field: OcrFieldDto } | null

/** The reviewer's workbench: the original paper beside what was read from it and how that compares with the system. */
export function PodDetailPage() {
  const { id = '' } = useParams()
  const { user, can } = useAuth()
  const isVendor = user?.transporterId != null
  const canReview = !isVendor && can('deliveries.pod.review')
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<Dialog>(null)
  const [text, setText] = useState('')
  const [reason, setReason] = useState('')
  const [exceptionType, setExceptionType] = useState('QuantityMismatch')

  const review = useQuery({ queryKey: queryKeys.deliveries.review(id), queryFn: async (): Promise<PodView> => (canReview ? deliveriesApi.podForReview(id) : { pod: await deliveriesApi.pod(id), delivery: null, documentEvidenceId: null, ocr: null }), refetchInterval: (q) => (q.state.data?.pod.ocr.some((o) => o.status === 'Queued' || o.status === 'Processing') ? 2000 : false) })

  const refresh = async (pod?: PodDto) => {
    if (pod) queryClient.setQueryData(queryKeys.deliveries.review(id), (old: unknown) => (old ? { ...(old as object), pod } : old))
    await queryClient.invalidateQueries({ queryKey: queryKeys.deliveries.all })
  }
  const done = (success: string) => async (result?: unknown) => {
    await refresh(result && typeof result === 'object' && 'items' in result && 'evidence' in result ? (result as PodDto) : undefined)
    setDialog(null)
    setText('')
    setReason('')
    void message.success(success)
  }
  const fail = (e: unknown) => void message.error(toApiError(e).message)

  const claim = useMutation({
    mutationFn: () => deliveriesApi.createClaims(review.data!.pod.summary.deliveryId, null),
    onSuccess: done('Claim sent with the evidence'),
    onError: fail,
  })
  const accept = useMutation({ mutationFn: () => deliveriesApi.reviewPod(id, 'accept', null), onSuccess: done('Proof accepted'), onError: fail })
  const reject = useMutation({ mutationFn: () => deliveriesApi.reviewPod(id, 'reject', text.trim()), onSuccess: done('Proof rejected'), onError: fail })
  const resubmission = useMutation({ mutationFn: () => deliveriesApi.reviewPod(id, 'resubmission', text.trim()), onSuccess: done('Sent back for more evidence'), onError: fail })
  const correction = useMutation({ mutationFn: () => deliveriesApi.requestCorrection(id, text.trim()), onSuccess: done('A new version has been started'), onError: fail })
  const edit = useMutation({ mutationFn: (field: string) => deliveriesApi.reviewOcrField(id, field, text.trim(), reason.trim()), onSuccess: done('Value corrected'), onError: fail })
  const raise = useMutation({
    mutationFn: () => deliveriesApi.raiseException({ deliveryId: review.data!.pod.summary.deliveryId, type: exceptionType, description: text.trim(), severity: null }),
    onSuccess: done('Exception raised'), onError: fail,
  })

  if (review.isLoading) return <Skeleton active />
  if (review.isError) return <Alert type="error" showIcon title={review.error.message} action={<Link to="/delivery/pods">Back to proofs</Link>} />
  const { pod, delivery, documentEvidenceId, ocr } = review.data!
  const s = pod.summary
  const waiting = s.status === 'Submitted' || s.status === 'UnderReview'
  const document = pod.evidence.find((e) => e.id === documentEvidenceId)
  const photos = pod.evidence.filter((e) => e.type !== 'PodDocument' && !e.removed)
  const blocked = pod.validations.some((v) => v.status === 'Invalid')
  const reading = ocr ?? pod.ocr.at(-1) ?? null

  return (
    <>
      <PageHeader
        title={`${s.podNumber}${s.version > 1 ? ` · version ${s.version}` : ''}`}
        description={`${s.customerName} · ${s.deliveryNumber}`}
        actions={
          <>
            <ProofStatusTag status={s.status} />
            {!s.isCurrent && <Tag>Superseded</Tag>}
            <Link to={`/delivery/${s.deliveryId}`}><Button>Delivery</Button></Link>
            {isVendor && ['Draft', 'Captured', 'Rejected', 'ResubmissionRequired'].includes(s.status) && <Link to={`/driver/${s.deliveryId}`}><Button type="primary">Add evidence and submit</Button></Link>}
            {canReview && waiting && (
              <>
                <Button type="primary" disabled={blocked} loading={accept.isPending} onClick={() => accept.mutate()}>Accept</Button>
                <Button onClick={() => { setText(''); setDialog({ kind: 'resubmission' }) }}>Request resubmission</Button>
                <Button danger onClick={() => { setText(''); setDialog({ kind: 'reject' }) }}>Reject</Button>
              </>
            )}
            {canReview && delivery && delivery.discrepancies.length > 0 && (
              <Button loading={claim.isPending} onClick={() => claim.mutate()}>Create claim</Button>
            )}
            {canReview && <Button onClick={() => { setText(''); setDialog({ kind: 'exception' }) }}>Create exception</Button>}
            {canReview && s.status === 'Accepted' && s.isCurrent && <Button onClick={() => { setText(''); setDialog({ kind: 'correction' }) }}>Request correction</Button>}
          </>
        }
      />

      {pod.rejectionReason && ['Rejected', 'ResubmissionRequired'].includes(s.status) && <Alert type="warning" showIcon style={{ marginBottom: 16 }} title={`Sent back: ${pod.rejectionReason}`} />}
      {blocked && waiting && canReview && <Alert type="error" showIcon style={{ marginBottom: 16 }} title="A mandatory check fails, so this proof cannot be accepted. Reject it, or ask for more evidence." />}
      {pod.missing.length > 0 && <Alert type="info" showIcon style={{ marginBottom: 16 }} title={`Still needed before it can be submitted: ${pod.missing.join('; ')}.`} />}

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={9}>
          <Card title={document ? `Original POD · ${document.fileName}` : 'Photos'} data-testid="original">
            {document ? (
              <AuthFile url={deliveriesApi.evidenceUrl(document.id)} name={document.fileName} contentType={document.contentType} />
            ) : photos.length === 0 ? (
              <Empty description="No paper POD or photos" image={Empty.PRESENTED_IMAGE_SIMPLE} />
            ) : null}
            {photos.length > 0 && (
              <Flex gap={8} wrap style={{ marginTop: document ? 12 : 0 }}>
                {photos.map((p) => (
                  <div key={p.id} style={{ width: 140 }}>
                    <AuthFile url={deliveriesApi.evidenceUrl(p.id)} name={p.fileName} contentType={p.contentType} height={110} />
                    <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>{p.type}{p.warnings ? ` · ${p.warnings}` : ''}</Typography.Text>
                  </div>
                ))}
              </Flex>
            )}
            {pod.signatures.map((sig) => (
              <div key={sig.id} style={{ marginTop: 12 }}>
                <AuthFile url={deliveriesApi.signatureUrl(sig.id)} name={`Signature of ${sig.signerName}`} contentType="image/png" height={90} />
                <Typography.Text type="secondary">Signed by {sig.signerName}, {formatDateTime(sig.capturedAt)}</Typography.Text>
              </div>
            ))}
          </Card>
        </Col>

        <Col xs={24} xl={9}>
          <Card title="Read from the paper" extra={reading && <Tag>{ocrStatusLabel[reading.status]}{reading.overallConfidence != null ? ` · ${pct(reading.overallConfidence)}` : ''}</Tag>}>
            {!reading ? (
              <Empty description="No paper POD has been read" image={Empty.PRESENTED_IMAGE_SIMPLE} />
            ) : reading.status === 'Failed' ? (
              <Alert type="warning" showIcon title={reading.error ?? 'It could not be read.'} description="Read the original beside this and decide." />
            ) : reading.status !== 'Completed' ? (
              <Skeleton active />
            ) : (
              <Table
                size="small" pagination={false} rowKey="name" dataSource={reading.fields} scroll={{ x: 'max-content' }}
                columns={[
                  { title: 'Field', dataIndex: 'name' },
                  {
                    title: 'Read', key: 'v',
                    render: (_, f: OcrFieldDto) => (
                      <div>
                        <span>{f.effectiveValue ?? '—'}</span>
                        {f.reviewedValue && <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>Paper said {f.rawValue}</Typography.Text>}
                        {f.expected && f.status === 'Mismatch' && <Typography.Text type="danger" style={{ display: 'block', fontSize: 12 }}>System: {f.expected}</Typography.Text>}
                      </div>
                    ),
                  },
                  { title: 'Confidence', key: 'c', align: 'right', render: (_, f: OcrFieldDto) => <span title={`Needs ${pct(f.threshold)}`}>{pct(f.confidence)}</span> },
                  { title: 'Check', key: 's', render: (_, f: OcrFieldDto) => <OcrFieldMark status={f.status} /> },
                  ...(canReview && waiting ? [{ title: '', key: 'e', render: (_: unknown, f: OcrFieldDto) => <Button size="small" aria-label={`Edit ${f.name}`} icon={<EditOutlined />} onClick={() => { setText(f.effectiveValue ?? ''); setReason(''); setDialog({ kind: 'edit', field: f }) }} /> }] : []),
                ]}
              />
            )}
          </Card>
          <Card title="Quantities" style={{ marginTop: 16 }}>
            <Table
              size="small" pagination={false} rowKey="deliveryItemId" dataSource={pod.items} scroll={{ x: 'max-content' }}
              columns={[
                { title: 'SKU', dataIndex: 'sku' }, { title: 'Dispatched', dataIndex: 'dispatchedQuantity', align: 'right', render: qty }, { title: 'Delivered', dataIndex: 'deliveredQuantity', align: 'right', render: qty },
                { title: 'Short', dataIndex: 'shortQuantity', align: 'right', render: qty }, { title: 'Damaged', dataIndex: 'damagedQuantity', align: 'right', render: qty }, { title: 'Rejected', dataIndex: 'rejectedQuantity', align: 'right', render: qty },
              ]}
            />
          </Card>
        </Col>

        <Col xs={24} xl={6}>
          <Flex vertical gap={16}>
            <Card title="Checks">
              {pod.validations.length === 0 ? (
                <Typography.Text type="secondary">Run when the proof is submitted.</Typography.Text>
              ) : (
                <Flex vertical gap={6}>
                  {pod.validations.map((v) => (
                    <div key={`${v.type}:${v.check}`}>
                      <ValidationMark status={v.status} /> <strong>{v.check}</strong>
                      <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>{v.message}</Typography.Text>
                    </div>
                  ))}
                </Flex>
              )}
            </Card>
            <Card title="Proof">
              <Descriptions column={1} size="small">
                <Descriptions.Item label="Method">{pod.method ?? '—'}</Descriptions.Item>
                <Descriptions.Item label="Received by">{pod.recipientName ?? '—'}{pod.recipientDesignation ? `, ${pod.recipientDesignation}` : ''}</Descriptions.Item>
                <Descriptions.Item label="Code">{pod.otpVerified ? 'Verified' : 'Not used'}</Descriptions.Item>
                <Descriptions.Item label="Location">{pod.latitude != null ? `${pod.latitude.toFixed(5)}, ${pod.longitude?.toFixed(5)}` : '—'}</Descriptions.Item>
                <Descriptions.Item label="Geofence">{geofenceLabel[pod.geofence]}</Descriptions.Item>
                <Descriptions.Item label="Customer acknowledged">{pod.customerAcknowledged ? 'Yes' : 'No'}</Descriptions.Item>
                <Descriptions.Item label="Captured">{formatDateTime(pod.capturedAt)}</Descriptions.Item>
                <Descriptions.Item label="Submitted">{formatDateTime(s.submittedAt)}</Descriptions.Item>
                <Descriptions.Item label="Accepted">{formatDateTime(s.approvedAt)}{pod.autoAccepted ? ' (automatically)' : ''}</Descriptions.Item>
                {pod.driverRemarks && <Descriptions.Item label="Driver">{pod.driverRemarks}</Descriptions.Item>}
              </Descriptions>
            </Card>
            <Card title="History">
              {pod.reviews.length === 0 ? <Typography.Text type="secondary">Nothing yet.</Typography.Text> : pod.reviews.map((r, i) => (
                <div key={i} style={{ marginBottom: 6 }}>
                  <strong>{r.action}</strong>{r.fieldName ? ` · ${r.fieldName}: ${r.oldValue ?? '—'} → ${r.newValue ?? '—'}` : ''}
                  <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12 }}>{formatDateTime(r.at)}{r.reason ? ` · ${r.reason}` : ''}</Typography.Text>
                </div>
              ))}
            </Card>
            {canReview && <AuditPanel subjects={[{ entityType: 'PodRecord', entityId: pod.summary.id }, { entityType: 'Delivery', entityId: pod.summary.deliveryId }]} />}
            {delivery && delivery.discrepancies.length > 0 && <Alert type="warning" showIcon title={`${delivery.discrepancies.length} discrepancy record(s) on the delivery`} />}
          </Flex>
        </Col>
      </Row>

      <Modal open={dialog?.kind === 'reject' || dialog?.kind === 'resubmission' || dialog?.kind === 'correction'}
        title={dialog?.kind === 'reject' ? 'Reject this proof?' : dialog?.kind === 'resubmission' ? 'Ask for more evidence' : 'Correct an accepted proof'}
        okText={dialog?.kind === 'reject' ? 'Reject' : dialog?.kind === 'resubmission' ? 'Send back' : 'Start a new version'}
        okButtonProps={{ disabled: text.trim().length === 0, danger: dialog?.kind === 'reject' }} confirmLoading={reject.isPending || resubmission.isPending || correction.isPending}
        onOk={() => (dialog?.kind === 'reject' ? reject.mutate() : dialog?.kind === 'resubmission' ? resubmission.mutate() : correction.mutate())} onCancel={() => setDialog(null)} destroyOnHidden>
        <Input.TextArea rows={3} maxLength={500} aria-label="Reason" placeholder={dialog?.kind === 'correction' ? 'What needs correcting? The accepted version is kept as it is.' : 'Say why, so the transporter knows what to fix'} value={text} onChange={(e) => setText(e.target.value)} />
      </Modal>
      <Modal open={dialog?.kind === 'edit'} title={dialog?.kind === 'edit' ? `Correct ${dialog.field.name}` : ''} okText="Save correction"
        okButtonProps={{ disabled: text.trim().length === 0 || reason.trim().length === 0 }} confirmLoading={edit.isPending} onOk={() => dialog?.kind === 'edit' && edit.mutate(dialog.field.name)} onCancel={() => setDialog(null)} destroyOnHidden>
        <Flex vertical gap={12}>
          <Typography.Text type="secondary">The original reading is kept; this adds your correction beside it and records why.</Typography.Text>
          <Input aria-label="Corrected value" value={text} onChange={(e) => setText(e.target.value)} />
          <Input aria-label="Why is it being changed" placeholder="Why is it being changed?" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Flex>
      </Modal>
      <Modal open={dialog?.kind === 'exception'} title="Create an exception" okText="Raise" okButtonProps={{ disabled: text.trim().length === 0 }} confirmLoading={raise.isPending} onOk={() => raise.mutate()} onCancel={() => setDialog(null)} destroyOnHidden>
        <Flex vertical gap={12}>
          <Select aria-label="Type" value={exceptionType} onChange={setExceptionType} options={exceptionTypeOptions} />
          <Input.TextArea rows={3} maxLength={1000} aria-label="What is the problem" placeholder="What is the problem?" value={text} onChange={(e) => setText(e.target.value)} />
        </Flex>
      </Modal>
    </>
  )
}
