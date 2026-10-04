import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { useState } from 'react';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { ErrorBox, Loading, PageHeader, Badge, type Tone } from '../../../shared/ui';
import { ExportMenu } from '../components/ExportMenu';
import { rankingApi, transporterApi } from '../api';
import { TRANSPORTER_STATUSES } from '../constants';
import { defaultPeriod, fmtPct, fmtScore } from '../../../shared/format';
import type { KpiType, RankedTransporter, TransporterListItem, TransporterStatus } from '../types';

const STATUS_TONE: Partial<Record<TransporterStatus, Tone>> = {
  Active: 'good',
  Approved: 'good',
  Suspended: 'warn',
  Blacklisted: 'bad',
};

function kpi(row: RankedTransporter | undefined, kind: KpiType): number | null {
  return row?.kpis.find((k) => k.kpi === kind)?.value ?? null;
}

/** Transporter master with the period's KPIs alongside, so the list shows performance without opening each record. */
export function TransporterListPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<TransporterStatus | ''>('');
  const [page, setPage] = useState(1);
  const period = defaultPeriod();

  const masters = useQuery({
    queryKey: ['transporters', search, status, page],
    queryFn: () => transporterApi.list({ search: search || undefined, status: status || undefined, page, pageSize: 25 }),
  });

  const performance = useQuery({
    queryKey: ['transporter-kpis', period.from, period.to],
    queryFn: () => rankingApi.rank({ from: period.from, to: period.to }, 'OverallScore'),
  });

  const byId = new Map((performance.data?.rows ?? []).map((r) => [r.transporterId, r]));
  const rows = masters.data?.items ?? [];

  const columns: TableColumn<TransporterListItem>[] = [
    { id: 'code', header: 'Code', cell: (r) => <Link to={`/transporters/${r.id}`}>{r.transporterCode}</Link>, sort: (r) => r.transporterCode },
    { id: 'name', header: 'Name', cell: (r) => r.legalName, sort: (r) => r.legalName },
    {
      id: 'status',
      header: 'Status',
      cell: (r) => <Badge tone={STATUS_TONE[r.status] ?? 'neutral'}>{r.status}</Badge>,
      sort: (r) => r.status,
    },
    { id: 'city', header: 'City', cell: (r) => r.city ?? '—', sort: (r) => r.city },
    { id: 'vehicles', header: 'Active vehicles', align: 'right', cell: (r) => r.activeVehicles, sort: (r) => r.activeVehicles },
    { id: 'lanes', header: 'Lanes', align: 'right', cell: (r) => r.activeLanes, sort: (r) => r.activeLanes },
    { id: 'otp', header: 'OTP', align: 'right', cell: (r) => fmtPct(kpi(byId.get(r.id), 'OnTimePickup')), sort: (r) => kpi(byId.get(r.id), 'OnTimePickup') },
    { id: 'otd', header: 'OTD', align: 'right', cell: (r) => fmtPct(kpi(byId.get(r.id), 'OnTimeDelivery')), sort: (r) => kpi(byId.get(r.id), 'OnTimeDelivery') },
    { id: 'placement', header: 'Placement', align: 'right', cell: (r) => fmtPct(kpi(byId.get(r.id), 'PlacementCompliance')), sort: (r) => kpi(byId.get(r.id), 'PlacementCompliance') },
    { id: 'pod', header: 'POD', align: 'right', cell: (r) => fmtPct(kpi(byId.get(r.id), 'PodCompliance')), sort: (r) => kpi(byId.get(r.id), 'PodCompliance') },
    {
      id: 'score',
      header: 'Overall score',
      align: 'right',
      cell: (r) => fmtScore(byId.get(r.id)?.overallScore ?? null),
      sort: (r) => byId.get(r.id)?.overallScore ?? null,
    },
  ];

  const exportColumns = [
    { header: 'Code', value: (r: TransporterListItem) => r.transporterCode },
    { header: 'Name', value: (r: TransporterListItem) => r.legalName },
    { header: 'Status', value: (r: TransporterListItem) => r.status },
    { header: 'City', value: (r: TransporterListItem) => r.city },
    { header: 'Active vehicles', value: (r: TransporterListItem) => r.activeVehicles },
    { header: 'Lanes', value: (r: TransporterListItem) => r.activeLanes },
    { header: 'OTP %', value: (r: TransporterListItem) => kpi(byId.get(r.id), 'OnTimePickup') },
    { header: 'OTD %', value: (r: TransporterListItem) => kpi(byId.get(r.id), 'OnTimeDelivery') },
    { header: 'Overall score', value: (r: TransporterListItem) => byId.get(r.id)?.overallScore ?? null },
  ];

  return (
    <>
      <PageHeader
        title="Transporters"
        subtitle={`KPIs for ${period.from} to ${period.to}. Not measurable values show as a dash.`}
        actions={<ExportMenu baseName="transporter-master" rows={rows} columns={exportColumns} disabled={rows.length === 0} />}
      />
      <div className="toolbar">
        <input placeholder="Search name, code or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
        <select value={status} onChange={(e) => { setStatus(e.target.value as TransporterStatus | ''); setPage(1); }}>
          <option value="">All statuses</option>
          {TRANSPORTER_STATUSES.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>
        <Link className="button" to="/transporters/rankings">Ranking</Link>
        <Link className="button" to="/transporters/benchmark">Benchmark</Link>
      </div>
      <ErrorBox error={masters.error ?? performance.error} />
      {masters.isLoading ? <Loading /> : <DataTable rows={rows} columns={columns} rowKey={(r) => r.id} empty="No transporters match these filters." />}
      {masters.data && masters.data.totalPages > 1 && (
        <div className="pager">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {masters.data.page} of {masters.data.totalPages}</span>
          <button type="button" disabled={page >= masters.data.totalPages} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      )}
    </>
  );
}
