import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { tenderApi } from '../api';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { Badge, ErrorBox, Loading, PageHeader, type Tone } from '../../../shared/ui';
import { ExportMenu } from '../components/ExportMenu';
import { fmtDateTime, fmtMoney } from '../../../shared/format';
import type { TenderInvitation, TenderStatus } from '../types';

const TONE: Partial<Record<TenderStatus, Tone>> = { Accepted: 'good', Awarded: 'good', Rejected: 'bad', Expired: 'bad', Sent: 'warn' };
const STATUSES: TenderStatus[] = ['Draft', 'Sent', 'Viewed', 'Accepted', 'Rejected', 'Expired', 'Withdrawn', 'Awarded', 'Cancelled'];

export function TenderListPage() {
  const [status, setStatus] = useState<TenderStatus | ''>('');
  const [tenderNumber, setTenderNumber] = useState('');
  const [page, setPage] = useState(1);
  const tenders = useQuery({
    queryKey: ['tenders', status, tenderNumber, page],
    queryFn: () => tenderApi.list({ status: status || undefined, tenderNumber: tenderNumber || undefined, page, pageSize: 25 }),
  });
  const rows = tenders.data?.items ?? [];

  const columns: TableColumn<TenderInvitation>[] = [
    { id: 'number', header: 'Tender', cell: (r) => <Link to={`/tenders/${r.id}`}>{r.tenderNumber}</Link>, sort: (r) => r.tenderNumber },
    { id: 'transporter', header: 'Transporter', cell: (r) => r.transporterName, sort: (r) => r.transporterName },
    { id: 'load', header: 'Load', cell: (r) => r.loadReference, sort: (r) => r.loadReference },
    { id: 'type', header: 'Type', cell: (r) => r.tenderType, sort: (r) => r.tenderType },
    { id: 'status', header: 'Status', cell: (r) => <Badge tone={TONE[r.status] ?? 'neutral'}>{r.status}</Badge>, sort: (r) => r.status },
    { id: 'rate', header: 'Offered rate', align: 'right', cell: (r) => fmtMoney(r.offeredRate, r.currency), sort: (r) => r.offeredRate },
    { id: 'deadline', header: 'Response deadline', cell: (r) => fmtDateTime(r.responseDeadline), sort: (r) => r.responseDeadline },
  ];

  const exportColumns = [
    { header: 'Tender', value: (r: TenderInvitation) => r.tenderNumber },
    { header: 'Transporter', value: (r: TenderInvitation) => r.transporterName },
    { header: 'Load', value: (r: TenderInvitation) => r.loadReference },
    { header: 'Type', value: (r: TenderInvitation) => r.tenderType },
    { header: 'Status', value: (r: TenderInvitation) => r.status },
    { header: 'Offered rate', value: (r: TenderInvitation) => r.offeredRate },
    { header: 'Response deadline', value: (r: TenderInvitation) => r.responseDeadline },
  ];

  return (
    <>
      <PageHeader title="Tenders" subtitle="Invitations to transporters, with their responses." actions={<ExportMenu baseName="tender-history" rows={rows} columns={exportColumns} disabled={rows.length === 0} />} />
      <div className="toolbar">
        <input placeholder="Tender number" value={tenderNumber} onChange={(e) => { setTenderNumber(e.target.value); setPage(1); }} />
        <select value={status} onChange={(e) => { setStatus(e.target.value as TenderStatus | ''); setPage(1); }}>
          <option value="">All statuses</option>
          {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      <ErrorBox error={tenders.error} />
      {tenders.isLoading ? <Loading /> : <DataTable rows={rows} columns={columns} rowKey={(r) => r.id} empty="No tenders match these filters." />}
      {tenders.data && tenders.data.totalPages > 1 && (
        <div className="pager">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {tenders.data.page} of {tenders.data.totalPages}</span>
          <button type="button" disabled={page >= tenders.data.totalPages} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      )}
    </>
  );
}
