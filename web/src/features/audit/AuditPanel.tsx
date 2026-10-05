import { useQueries } from '@tanstack/react-query'
import { Card, Table, Tag, Typography } from 'antd'
import { useAuth } from '@/features/auth/AuthContext'
import { auditApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { AuditAction, AuditLogDto } from '@/lib/api/types'
import { formatDateTime } from '@/lib/format'
import { ChangeTable } from './AuditLogPage'

const actionColor: Record<AuditAction, string> = { Created: 'green', Updated: 'blue', Deleted: 'red' }
const entityLabel: Record<string, string> = {
  Delivery: 'Delivery', DeliveryItem: 'Item', DeliveryAttempt: 'Attempt', DeliveryDiscrepancy: 'Discrepancy', PodRecord: 'Proof', PodEvidence: 'Evidence', PodSignature: 'Signature',
  PodReviewAction: 'Review', PodOcrField: 'OCR field', DeliveryException: 'Exception', ClaimHandoff: 'Claim',
}

export interface AuditSubject {
  entityType: string
  entityId: string
}

/** Every recorded change to the given records, newest first. Shown only to people who may read the audit trail. */
export function AuditPanel({ subjects, title = 'Audit' }: { subjects: AuditSubject[]; title?: string }) {
  const { can } = useAuth()
  const allowed = can('audit.read')
  const results = useQueries({
    queries: subjects.map((s) => {
      const params = { entityType: s.entityType, entityId: s.entityId, pageSize: 100 }
      return { queryKey: queryKeys.audit.list(params), queryFn: () => auditApi.list(params), enabled: allowed }
    }),
  })
  if (!allowed) return null

  const rows: AuditLogDto[] = results.flatMap((r) => r.data?.items ?? []).sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
  return (
    <Card title={title}>
      <Table
        size="small" rowKey="id" loading={results.some((r) => r.isLoading)} dataSource={rows} pagination={{ pageSize: 10, hideOnSinglePage: true }} locale={{ emptyText: 'Nothing recorded yet' }}
        expandable={{ rowExpandable: (l) => l.changes !== null && Object.keys(l.changes).length > 0, expandedRowRender: (l) => <ChangeTable changes={l.changes!} /> }}
        columns={[
          { title: 'When', dataIndex: 'occurredAt', width: 180, render: formatDateTime },
          { title: 'Who', key: 'who', render: (_, l) => l.userName ?? <Typography.Text type="secondary">System</Typography.Text> },
          { title: 'Action', dataIndex: 'action', width: 100, render: (a: AuditAction) => <Tag color={actionColor[a]}>{a}</Tag> },
          { title: 'Record', dataIndex: 'entityType', render: (t: string) => entityLabel[t] ?? t },
        ]}
      />
    </Card>
  )
}
