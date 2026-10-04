import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { placementApi } from '../api';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { Badge, ErrorBox, Loading, PageHeader, type Tone } from '../../../shared/ui';
import { ExportMenu } from '../components/ExportMenu';
import { fmtDateTime } from '../../../shared/format';
import type { PlacementDto, PlacementStatus } from '../types';

const SLA_TONE: Record<string, Tone> = { OnTime: 'good', Delayed: 'bad', Pending: 'neutral' };
const STATUSES: PlacementStatus[] = ['Requested', 'Confirmed', 'VehicleAssigned', 'Reported', 'Placed', 'LoadingStarted', 'NoShow', 'Replaced', 'Cancelled'];

export function VehiclePlacementPage() {
  const [status, setStatus] = useState<PlacementStatus | ''>('');
  const [page, setPage] = useState(1);
  const placements = useQuery({
    queryKey: ['placements', status, page],
    queryFn: () => placementApi.list({ status: status || undefined, page, pageSize: 25 }),
  });
  const rows = placements.data?.items ?? [];

  const columns: TableColumn<PlacementDto>[] = [
    { id: 'load', header: 'Load', cell: (r) => r.loadReference, sort: (r) => r.loadReference },
    { id: 'vehicle', header: 'Vehicle', cell: (r) => r.vehicleRegistration ?? '—', sort: (r) => r.vehicleRegistration },
    { id: 'status', header: 'Status', cell: (r) => r.status, sort: (r) => r.status },
    { id: 'required', header: 'Required by', cell: (r) => fmtDateTime(r.requiredPlacementAt), sort: (r) => r.requiredPlacementAt },
    { id: 'placed', header: 'Placed at', cell: (r) => fmtDateTime(r.placedAt), sort: (r) => r.placedAt },
    { id: 'sla', header: 'SLA', cell: (r) => <Badge tone={SLA_TONE[r.slaStatus] ?? 'neutral'}>{r.slaStatus}</Badge>, sort: (r) => r.slaStatus },
    { id: 'delay', header: 'Delay (min)', align: 'right', cell: (r) => r.placementDelayMinutes ?? '—', sort: (r) => r.placementDelayMinutes },
    { id: 'replacements', header: 'Replacements', align: 'right', cell: (r) => r.replacementCount, sort: (r) => r.replacementCount },
  ];

  const exportColumns = [
    { header: 'Load', value: (r: PlacementDto) => r.loadReference },
    { header: 'Vehicle', value: (r: PlacementDto) => r.vehicleRegistration },
    { header: 'Status', value: (r: PlacementDto) => r.status },
    { header: 'Required by', value: (r: PlacementDto) => r.requiredPlacementAt },
    { header: 'Placed at', value: (r: PlacementDto) => r.placedAt },
    { header: 'SLA', value: (r: PlacementDto) => r.slaStatus },
    { header: 'Delay (min)', value: (r: PlacementDto) => r.placementDelayMinutes },
  ];

  return (
    <>
      <PageHeader title="Vehicle placement" subtitle="Placement requests, vehicle confirmation and SLA." actions={<ExportMenu baseName="vehicle-placement" rows={rows} columns={exportColumns} disabled={rows.length === 0} />} />
      <div className="toolbar">
        <select value={status} onChange={(e) => { setStatus(e.target.value as PlacementStatus | ''); setPage(1); }}>
          <option value="">All statuses</option>
          {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      <ErrorBox error={placements.error} />
      {placements.isLoading ? <Loading /> : <DataTable rows={rows} columns={columns} rowKey={(r) => r.id} empty="No placements match these filters." />}
      {placements.data && placements.data.totalPages > 1 && (
        <div className="pager">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {placements.data.page} of {placements.data.totalPages}</span>
          <button type="button" disabled={page >= placements.data.totalPages} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      )}
    </>
  );
}
