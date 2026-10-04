import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { openPodFile, vendorApi } from '../../transporter-management/api';
import { Badge, Card, ErrorBox, Field, Loading, PageHeader, type Tone } from '../../../shared/ui';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { fmtDate, fmtDateTime, isoDay } from '../../../shared/format';
import { ApiError } from '../../../core/http';
import type { DriverDto, PodDto, PodStatus, VendorLoad } from '../../transporter-management/types';

/* The vendor portal is for transporter staff, so screens are simpler than the internal ones and stack on narrow screens. */

export function VendorDashboardPage() {
  const dashboard = useQuery({ queryKey: ['vendor-dashboard'], queryFn: () => vendorApi.dashboard() });
  if (dashboard.isLoading) {
    return <Loading />;
  }
  if (!dashboard.data) {
    return <ErrorBox error={dashboard.error} />;
  }
  const d = dashboard.data;
  const tiles: { label: string; value: number | null; tone?: Tone }[] = [
    { label: 'New tenders', value: d.newTenders, tone: d.newTenders > 0 ? 'warn' : 'neutral' },
    { label: 'Awaiting your response', value: d.pendingAcceptance, tone: d.pendingAcceptance > 0 ? 'warn' : 'neutral' },
    { label: 'Accepted loads', value: d.acceptedLoads },
    { label: 'Upcoming placements', value: d.upcomingPlacements },
    { label: "Today's pickups", value: d.todaysPickups },
    { label: "Today's deliveries", value: d.todaysDeliveries },
    { label: 'PODs pending', value: d.pendingPod, tone: d.pendingPod ? 'warn' : 'neutral' },
    { label: 'Open exceptions', value: d.openExceptions, tone: d.openExceptions ? 'bad' : 'neutral' },
  ];
  return (
    <>
      <PageHeader title="Dashboard" subtitle="Your tenders, loads and proof of delivery at a glance." />
      <div className="tile-grid">
        {tiles.map((t) => (
          <div key={t.label} className={`tile tile-${t.tone ?? 'neutral'}`}>
            <span className="tile-value">{t.value ?? '—'}</span>
            <span className="tile-label">{t.label}</span>
          </div>
        ))}
      </div>
    </>
  );
}

export function VendorTendersPage() {
  const queryClient = useQueryClient();
  const tenders = useQuery({ queryKey: ['vendor-tenders'], queryFn: () => vendorApi.tenders() });
  const [rateByTender, setRateByTender] = useState<Record<number, string>>({});
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['vendor-tenders'] });
  const accept = useMutation({
    mutationFn: ({ id, rate }: { id: number; rate?: number }) => vendorApi.acceptTender(id, rate),
    onSuccess: refresh,
  });
  const reject = useMutation({
    mutationFn: (id: number) => vendorApi.rejectTender(id, 'RATE_ISSUE'),
    onSuccess: refresh,
  });

  const columns: TableColumn<{ id: number; tenderNumber: string; loadReference: string; status: string; offeredRate: number | null; responseDeadline: string }>[] = [
    { id: 'number', header: 'Tender', cell: (r) => r.tenderNumber, sort: (r) => r.tenderNumber },
    { id: 'load', header: 'Load', cell: (r) => r.loadReference, sort: (r) => r.loadReference },
    { id: 'status', header: 'Status', cell: (r) => <Badge>{r.status}</Badge>, sort: (r) => r.status },
    { id: 'rate', header: 'Offered rate', align: 'right', cell: (r) => r.offeredRate ?? '—', sort: (r) => r.offeredRate },
    { id: 'deadline', header: 'Respond by', cell: (r) => fmtDateTime(r.responseDeadline), sort: (r) => r.responseDeadline },
    {
      id: 'actions',
      header: 'Respond',
      cell: (r) => r.status === 'Sent' || r.status === 'Viewed' ? (
        <span className="row-actions">
          <input
            aria-label="Your rate"
            inputMode="decimal"
            placeholder="Rate"
            className="narrow"
            value={rateByTender[r.id] ?? ''}
            onChange={(e) => setRateByTender({ ...rateByTender, [r.id]: e.target.value })}
          />
          <button type="button" onClick={() => accept.mutate({ id: r.id, rate: rateByTender[r.id] ? Number(rateByTender[r.id]) : undefined })}>Accept</button>
          <button type="button" onClick={() => reject.mutate(r.id)}>Decline</button>
        </span>
      ) : '—',
    },
  ];

  return (
    <>
      <PageHeader title="Tenders" subtitle="Accept or decline invitations before the deadline." />
      <ErrorBox error={tenders.error ?? accept.error ?? reject.error} />
      {tenders.isLoading ? <Loading /> : <DataTable rows={tenders.data?.items ?? []} columns={columns} rowKey={(r) => r.id} empty="No tenders for you right now." />}
    </>
  );
}

export function VendorLoadsPage() {
  const loads = useQuery({ queryKey: ['vendor-loads'], queryFn: () => vendorApi.loads() });
  const columns: TableColumn<VendorLoad>[] = [
    { id: 'load', header: 'Load', cell: (r) => r.loadReference, sort: (r) => r.loadReference },
    { id: 'pickup', header: 'Pickup', cell: (r) => fmtDateTime(r.pickupDateTime), sort: (r) => r.pickupDateTime },
    { id: 'delivery', header: 'Delivery', cell: (r) => fmtDateTime(r.deliveryDateTime), sort: (r) => r.deliveryDateTime },
    { id: 'vehicle', header: 'Vehicle', cell: (r) => r.vehicleRegistration ?? 'Not assigned', sort: (r) => r.vehicleRegistration },
    { id: 'driver', header: 'Driver', cell: (r) => r.driverName ?? '—', sort: (r) => r.driverName },
    { id: 'placement', header: 'Placement', cell: (r) => r.placementStatus ?? '—', sort: (r) => r.placementStatus },
    { id: 'execution', header: 'Progress', cell: (r) => r.executionStatus ?? r.loadStatus, sort: (r) => r.executionStatus },
    { id: 'pod', header: 'POD', cell: (r) => r.podStatus ?? '—', sort: (r) => r.podStatus },
  ];
  return (
    <>
      <PageHeader title="My loads" subtitle="Accepted loads, their vehicle and driver, and where they stand." />
      <ErrorBox error={loads.error} />
      {loads.isLoading ? <Loading /> : <DataTable rows={loads.data ?? []} columns={columns} rowKey={(r) => r.invitationId} empty="No accepted loads yet." />}
    </>
  );
}

export function VendorPodsPage() {
  const queryClient = useQueryClient();
  const pods = useQuery({ queryKey: ['vendor-pods'], queryFn: () => vendorApi.pods() });
  const [loadReference, setLoadReference] = useState('');
  const [podDate, setPodDate] = useState(() => isoDay(new Date()));
  const [receivedBy, setReceivedBy] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const upload = useMutation({
    mutationFn: () => {
      const form = new FormData();
      if (file) {
        form.append('file', file);
      }
      form.append('podDate', podDate);
      form.append('receivedBy', receivedBy);
      return vendorApi.uploadPod(loadReference, form);
    },
    onSuccess: () => {
      setLoadReference('');
      setReceivedBy('');
      setFile(null);
      queryClient.invalidateQueries({ queryKey: ['vendor-pods'] });
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    upload.mutate();
  };

  const columns: TableColumn<PodDto>[] = [
    { id: 'load', header: 'Load', cell: (r) => r.loadReference, sort: (r) => r.loadReference },
    { id: 'delivered', header: 'Delivered', cell: (r) => fmtDate(r.deliveredAt), sort: (r) => r.deliveredAt },
    { id: 'due', header: 'POD due', cell: (r) => fmtDateTime(r.dueAt), sort: (r) => r.dueAt },
    { id: 'status', header: 'Status', cell: (r) => <Badge tone={podTone(r.status)}>{r.status}</Badge>, sort: (r) => r.status },
    { id: 'reason', header: 'Note', cell: (r) => r.rejectionReason ?? '—' },
    { id: 'file', header: 'File', cell: (r) => <button type="button" onClick={() => openPodFile(r.id, true)}>Open</button> },
  ];

  return (
    <>
      <PageHeader title="Proof of delivery" subtitle="Upload the signed POD for a delivered load. Rejected PODs can be sent again." />
      <Card title="Upload POD">
        <form className="stack-form" onSubmit={submit}>
          <Field label="Load reference"><input value={loadReference} onChange={(e) => setLoadReference(e.target.value)} required /></Field>
          <Field label="POD date"><input type="date" value={podDate} onChange={(e) => setPodDate(e.target.value)} required /></Field>
          <Field label="Received by"><input value={receivedBy} onChange={(e) => setReceivedBy(e.target.value)} required /></Field>
          <Field label="File (PDF or image)"><input type="file" accept="application/pdf,image/*" onChange={(e) => setFile(e.target.files?.[0] ?? null)} required /></Field>
          <button type="submit" disabled={upload.isPending}>Upload</button>
          <ErrorBox error={upload.error} />
        </form>
      </Card>
      <ErrorBox error={pods.error} />
      {pods.isLoading ? <Loading /> : <DataTable rows={pods.data?.items ?? []} columns={columns} rowKey={(r) => r.id} empty="No PODs yet." />}
    </>
  );
}

function podTone(status: PodStatus): Tone {
  return status === 'Accepted' ? 'good' : status === 'Rejected' || status === 'ResubmissionRequired' ? 'bad' : status === 'Pending' ? 'warn' : 'neutral';
}

export function VendorDriversPage() {
  const queryClient = useQueryClient();
  const drivers = useQuery({ queryKey: ['vendor-drivers'], queryFn: () => vendorApi.drivers() });
  const [editing, setEditing] = useState<DriverDto | null>(null);
  const [form, setForm] = useState({ fullName: '', mobile: '', licenceNumber: '' });
  const [docDriver, setDocDriver] = useState<number | null>(null);
  const [docFile, setDocFile] = useState<File | null>(null);
  const [docType, setDocType] = useState('');
  const [docNumber, setDocNumber] = useState('');
  const [docExpiry, setDocExpiry] = useState('');

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['vendor-drivers'] });
  const save = useMutation({
    mutationFn: () => (editing
      ? vendorApi.updateDriver(editing.id, form)
      : vendorApi.addDriver(form)),
    onSuccess: () => {
      setEditing(null);
      setForm({ fullName: '', mobile: '', licenceNumber: '' });
      refresh();
    },
  });
  const uploadDoc = useMutation({
    mutationFn: () => {
      const data = new FormData();
      if (docFile) {
        data.append('file', docFile);
      }
      data.append('documentTypeId', docType);
      data.append('documentNumber', docNumber);
      if (docExpiry) {
        data.append('expiryDate', docExpiry);
      }
      return vendorApi.uploadDriverDocument(docDriver ?? 0, data);
    },
    onSuccess: () => {
      setDocFile(null);
      setDocNumber('');
      setDocExpiry('');
    },
  });

  const startEdit = (d: DriverDto) => {
    setEditing(d);
    setForm({ fullName: d.fullName, mobile: d.mobile, licenceNumber: d.licenceNumber });
  };
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  const submitDoc = (e: FormEvent) => {
    e.preventDefault();
    uploadDoc.mutate();
  };

  const columns: TableColumn<DriverDto>[] = [
    { id: 'name', header: 'Name', cell: (r) => r.fullName, sort: (r) => r.fullName },
    { id: 'mobile', header: 'Mobile', cell: (r) => r.mobile },
    { id: 'licence', header: 'Licence', cell: (r) => r.licenceNumber, sort: (r) => r.licenceNumber },
    { id: 'status', header: 'Status', cell: (r) => <Badge tone={r.status === 'Active' ? 'good' : 'neutral'}>{r.status}</Badge>, sort: (r) => r.status },
    {
      id: 'actions',
      header: '',
      cell: (r) => (
        <span className="row-actions">
          <button type="button" onClick={() => startEdit(r)}>Edit</button>
          <button type="button" onClick={() => setDocDriver(r.id)}>Add document</button>
        </span>
      ),
    },
  ];

  const apiMessage = save.error instanceof ApiError ? save.error : null;

  return (
    <>
      <PageHeader title="Drivers" subtitle="Keep your drivers and their licences up to date. Compliance verifies each document." />
      <Card title={editing ? `Edit ${editing.fullName}` : 'Add driver'}>
        <form className="stack-form" onSubmit={submit}>
          <Field label="Full name"><input value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} required /></Field>
          <Field label="Mobile"><input inputMode="tel" value={form.mobile} onChange={(e) => setForm({ ...form, mobile: e.target.value })} required /></Field>
          <Field label="Licence number"><input value={form.licenceNumber} onChange={(e) => setForm({ ...form, licenceNumber: e.target.value })} required /></Field>
          <div className="toolbar">
            <button type="submit" disabled={save.isPending}>{editing ? 'Save changes' : 'Add driver'}</button>
            {editing && <button type="button" onClick={() => { setEditing(null); setForm({ fullName: '', mobile: '', licenceNumber: '' }); }}>Cancel</button>}
          </div>
          <ErrorBox error={save.error} />
          {apiMessage?.code === 'DRIVER_LICENCE_DUPLICATE' && <p className="muted">That licence is already registered to one of your drivers.</p>}
        </form>
      </Card>

      {docDriver !== null && (
        <Card title="Upload driver document" actions={<button type="button" onClick={() => setDocDriver(null)}>Close</button>}>
          <form className="stack-form" onSubmit={submitDoc}>
            <Field label="Document type ID"><input inputMode="numeric" value={docType} onChange={(e) => setDocType(e.target.value)} required /></Field>
            <Field label="Document number"><input value={docNumber} onChange={(e) => setDocNumber(e.target.value)} /></Field>
            <Field label="Expiry date"><input type="date" value={docExpiry} onChange={(e) => setDocExpiry(e.target.value)} /></Field>
            <Field label="File (PDF or image)"><input type="file" accept="application/pdf,image/*" onChange={(e) => setDocFile(e.target.files?.[0] ?? null)} required /></Field>
            <button type="submit" disabled={uploadDoc.isPending}>Upload</button>
            <ErrorBox error={uploadDoc.error} />
            {uploadDoc.isSuccess && <p className="muted">Uploaded. Compliance will verify it.</p>}
          </form>
        </Card>
      )}

      <ErrorBox error={drivers.error} />
      {drivers.isLoading ? <Loading /> : <DataTable rows={drivers.data ?? []} columns={columns} rowKey={(r) => r.id} empty="No drivers yet. Add your first driver above." />}
    </>
  );
}

export function VendorCapacityPage() {
  const queryClient = useQueryClient();
  const [from] = useState(() => isoDay(new Date()));
  const [to] = useState(() => isoDay(new Date(Date.now() + 13 * 86400000)));
  const capacity = useQuery({ queryKey: ['vendor-capacity', from, to], queryFn: () => vendorApi.capacity({ from, to }) });
  const [date, setDate] = useState(() => isoDay(new Date()));
  const [committed, setCommitted] = useState('');
  const [available, setAvailable] = useState('');
  const save = useMutation({
    mutationFn: () => vendorApi.saveCapacity({ date, vehiclesCommitted: Number(committed), vehiclesAvailable: Number(available) }),
    onSuccess: () => {
      setCommitted('');
      setAvailable('');
      queryClient.invalidateQueries({ queryKey: ['vendor-capacity'] });
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };

  return (
    <>
      <PageHeader title="Vehicle capacity" subtitle="Report how many vehicles you committed for each day and how many are available. Saving a day replaces its earlier figures." />
      <Card title="Report a day">
        <form className="stack-form" onSubmit={submit}>
          <Field label="Date"><input type="date" value={date} onChange={(e) => setDate(e.target.value)} required /></Field>
          <Field label="Committed vehicles"><input inputMode="numeric" value={committed} onChange={(e) => setCommitted(e.target.value)} required /></Field>
          <Field label="Available vehicles"><input inputMode="numeric" value={available} onChange={(e) => setAvailable(e.target.value)} required /></Field>
          <button type="submit" disabled={save.isPending}>Save</button>
          <ErrorBox error={save.error} />
        </form>
      </Card>
      <ErrorBox error={capacity.error} />
      {capacity.isLoading ? <Loading /> : (
        <DataTable
          rows={capacity.data ?? []}
          columns={[
            { id: 'date', header: 'Date', cell: (r) => fmtDate(r.date), sort: (r) => r.date },
            { id: 'committed', header: 'Committed', align: 'right', cell: (r) => r.vehiclesCommitted, sort: (r) => r.vehiclesCommitted },
            { id: 'available', header: 'Available', align: 'right', cell: (r) => r.vehiclesAvailable, sort: (r) => r.vehiclesAvailable },
          ]}
          rowKey={(r) => r.id}
          empty="No capacity reported for the next two weeks."
        />
      )}
    </>
  );
}
