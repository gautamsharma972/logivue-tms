import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Descriptions, Drawer, Flex, Input, Modal, Popconfirm, Skeleton, Steps, Typography } from 'antd'
import { useState } from 'react'
import { approvalsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { RequestDto, RequestStepDto } from '@/lib/api/types'
import { formatDateTime, formatInr } from '@/lib/format'
import { StatusTag } from './StatusTag'

type Decision = 'approve' | 'reject'

function stepStatus(step: RequestStepDto, index: number, request: RequestDto): 'finish' | 'process' | 'wait' | 'error' {
  if (step.status === 'Approved') return 'finish'
  if (step.status === 'Rejected') return 'error'
  return index === request.currentStepIndex ? 'process' : 'wait'
}

function StepDetail({ step, current }: { step: RequestStepDto; current: boolean }) {
  if (step.status === 'Pending') {
    return (
      <Typography.Text type="secondary">
        {current ? 'Waiting for someone holding ' : 'Needs '}
        <Typography.Text code>{step.requiredPermission}</Typography.Text>
      </Typography.Text>
    )
  }
  return (
    <div>
      <Typography.Text>
        {step.status} by <strong>{step.decidedByName ?? 'Unknown'}</strong>
        {step.onBehalfOfName && <> on behalf of <strong>{step.onBehalfOfName}</strong></>}
      </Typography.Text>
      <br />
      <Typography.Text type="secondary">{formatDateTime(step.decidedAt)}</Typography.Text>
      {step.comment && (
        <Typography.Paragraph style={{ margin: '4px 0 0' }} italic>
          “{step.comment}”
        </Typography.Paragraph>
      )}
    </div>
  )
}

interface Props {
  requestId: string | null
  onClose: () => void
}

export function RequestDrawer({ requestId, onClose }: Props) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [decision, setDecision] = useState<Decision | null>(null)
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)

  const request = useQuery({
    queryKey: queryKeys.approvals.request(requestId ?? ''),
    queryFn: () => approvalsApi.request(requestId!),
    enabled: requestId !== null,
  })

  const finish = async (text: string) => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.all })
    void message.success(text)
  }

  const decide = useMutation({
    mutationFn: ({ kind, text }: { kind: Decision; text: string }) =>
      kind === 'approve' ? approvalsApi.approve(requestId!, text || undefined) : approvalsApi.reject(requestId!, text),
    onSuccess: async (_, { kind }) => {
      setDecision(null)
      setComment('')
      await finish(kind === 'approve' ? 'Approved' : 'Rejected')
    },
    onError: async (e) => {
      const apiError = toApiError(e)
      setError(apiError.message)
      // Someone else may have just decided; refresh so the drawer shows the truth.
      if (apiError.status === 409 || apiError.status === 403) await queryClient.invalidateQueries({ queryKey: queryKeys.approvals.all })
    },
  })

  const cancel = useMutation({
    mutationFn: () => approvalsApi.cancel(requestId!),
    onSuccess: () => finish('Request cancelled'),
    onError: (e) => void message.error(toApiError(e).message),
  })

  const data = request.data
  const closeModal = () => {
    setDecision(null)
    setComment('')
    setError(null)
  }

  return (
    <>
      <Drawer
        open={requestId !== null}
        onClose={onClose}
        size={520}
        destroyOnHidden
        title={data ? data.documentTypeName : 'Approval request'}
        footer={
          data && (data.canDecide || data.canCancel) ? (
            <Flex justify="space-between" gap={8}>
              <div>
                {data.canCancel && (
                  <Popconfirm title="Cancel this request?" description="The document will no longer be waiting for approval." okText="Cancel request" cancelText="Keep" onConfirm={() => cancel.mutate()}>
                    <Button danger type="text" loading={cancel.isPending}>
                      Cancel request
                    </Button>
                  </Popconfirm>
                )}
              </div>
              {data.canDecide && (
                <Flex gap={8}>
                  <Button danger onClick={() => setDecision('reject')}>
                    Reject
                  </Button>
                  <Button type="primary" onClick={() => setDecision('approve')}>
                    Approve
                  </Button>
                </Flex>
              )}
            </Flex>
          ) : undefined
        }
      >
        {request.isLoading && <Skeleton active paragraph={{ rows: 8 }} />}
        {request.isError && <Alert type="error" showIcon title={request.error.message} />}
        {data && (
          <>
            <Flex justify="space-between" align="flex-start" gap={12} style={{ marginBottom: 16 }}>
              <Typography.Title level={4} style={{ margin: 0 }}>
                {data.title}
              </Typography.Title>
              <StatusTag status={data.status} />
            </Flex>

            <Descriptions column={1} size="small" colon={false} style={{ marginBottom: 24 }}>
              <Descriptions.Item label="Amount">{formatInr(data.amount)}</Descriptions.Item>
              <Descriptions.Item label="Requested by">{data.requesterName}</Descriptions.Item>
              <Descriptions.Item label="Submitted">{formatDateTime(data.createdAt)}</Descriptions.Item>
              {data.completedAt && <Descriptions.Item label="Completed">{formatDateTime(data.completedAt)}</Descriptions.Item>}
            </Descriptions>

            <Typography.Title level={5}>Approval steps</Typography.Title>
            {data.steps.length === 0 ? (
              <Typography.Text type="secondary">Approved automatically — no approval step applied to this amount.</Typography.Text>
            ) : (
              <Steps
                orientation="vertical"
                size="small"
                current={data.currentStepIndex ?? data.steps.length}
                items={data.steps.map((step, i) => ({
                  title: step.name,
                  status: stepStatus(step, i, data),
                  content: <StepDetail step={step} current={i === data.currentStepIndex} />,
                }))}
              />
            )}
          </>
        )}
      </Drawer>

      <Modal
        open={decision !== null}
        title={decision === 'approve' ? 'Approve request' : 'Reject request'}
        okText={decision === 'approve' ? 'Approve' : 'Reject'}
        okButtonProps={{ danger: decision === 'reject', disabled: decision === 'reject' && comment.trim() === '' }}
        confirmLoading={decide.isPending}
        onCancel={closeModal}
        onOk={() => decision && decide.mutate({ kind: decision, text: comment.trim() })}
        destroyOnHidden
      >
        {error && <Alert type="error" showIcon title={error} style={{ marginBottom: 12 }} role="alert" />}
        <Typography.Paragraph type="secondary">
          {decision === 'approve' ? 'Add a note if it helps the next approver (optional).' : 'Tell the requester why this is being rejected. This is required.'}
        </Typography.Paragraph>
        <Input.TextArea rows={4} maxLength={1000} showCount autoFocus value={comment} onChange={(e) => setComment(e.target.value)} aria-label="Comment" />
      </Modal>
    </>
  )
}
