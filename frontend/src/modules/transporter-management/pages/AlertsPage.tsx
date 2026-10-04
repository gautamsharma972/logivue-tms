import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { alertApi } from '../api';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { Badge, ErrorBox, Loading, PageHeader, type Tone } from '../../../shared/ui';
import { ExportMenu } from '../components/ExportMenu';
import { fmtDateTime } from '../../../shared/format';
import type { AlertDto, AlertStatus, Severity } from '../types';

const SEVERITY_TONE: Record<Severity, Tone> = { Low: 'neutral', Medium: 'warn', High: 'bad', Critical: 'bad' };
const STATUSES: AlertStatus[] = ['Open', 'Acknowledged', 'Resolved'];

export function AlertsPage() {
  const [status, setStatus] = useState<AlertStatus | ''>('Open');
  const queryClient = useQueryClient();
  const alerts = useQuery({
    queryKey: ['alerts', status],
    queryFn: () => alertApi.list({ status: status || undefined, pageSize: 100 }),
  });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['alerts'] });
  const acknowledge = useMutation({ mutationFn: (id: number) => alertApi.acknowledge(id), onSuccess: refresh });
  const resolve = useMutation({ mutationFn: (id: number) => alertApi.resolve(id), onSuccess: refresh });
  const rows = alerts.data?.items ?? [];

  const columns: TableColumn<AlertDto>[] = [
    { id: 'severity', header: 'Severity', cell: (r) => <Badge tone={SEVERITY_TONE[r.severity]}>{r.severity}</Badge>, sort: (r) => r.severity },
    { id: 'type', header: 'Type', cell: (r) => r.alertType, sort: (r) => r.alertType },
    { id: 'transporter', header: 'Transporter', cell: (r) => `#${r.transporterId}`, sort: (r) => r.transporterId },
    { id: 'message', header: 'Message', cell: (r) => r.message },
    { id: 'raised', header: 'Raised', cell: (r) => fmtDateTime(r.createdAt), sort: (r) => r.createdAt },
    { id: 'due', header: 'Due', cell: (r) => fmtDateTime(r.dueAt), sort: (r) => r.dueAt },
    { id: 'status', header: 'Status', cell: (r) => r.status, sort: (r) => r.status },
    {
      id: 'actions',
      header: 'Actions',
      cell: (r) => (
        <span className="row-actions">
          {r.status === 'Open' && <button type="button" onClick={() => acknowledge.mutate(r.id)}>Acknowledge</button>}
          {r.status !== 'Resolved' && <button type="button" onClick={() => resolve.mutate(r.id)}>Resolve</button>}
        </span>
      ),
    },
  ];

  const exportColumns = [
    { header: 'Severity', value: (r: AlertDto) => r.severity },
    { header: 'Type', value: (r: AlertDto) => r.alertType },
    { header: 'Transporter ID', value: (r: AlertDto) => r.transporterId },
    { header: 'Message', value: (r: AlertDto) => r.message },
    { header: 'Raised', value: (r: AlertDto) => r.createdAt },
    { header: 'Status', value: (r: AlertDto) => r.status },
  ];

  return (
    <>
      <PageHeader title="Alerts" subtitle="Compliance, tender, placement and POD alerts raised by the monitors." actions={<ExportMenu baseName="transporter-alerts" rows={rows} columns={exportColumns} disabled={rows.length === 0} />} />
      <div className="toolbar">
        <select value={status} onChange={(e) => setStatus(e.target.value as AlertStatus | '')}>
          <option value="">All statuses</option>
          {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      <ErrorBox error={alerts.error ?? acknowledge.error ?? resolve.error} />
      {alerts.isLoading ? <Loading /> : <DataTable rows={rows} columns={columns} rowKey={(r) => r.id} empty="No alerts for this filter." />}
    </>
  );
}
