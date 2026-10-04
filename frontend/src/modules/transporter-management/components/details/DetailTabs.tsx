import { useQuery } from '@tanstack/react-query';
import { transporterApi } from '../../api';
import type { TransporterDetail } from '../../types';
import { Card, Loading, Badge, ErrorBox } from '../../../../shared/ui';
import { fmtDate } from '../../../../shared/format';

export function OverviewTab({ transporter }: { transporter: TransporterDetail }) {
  return (
    <div className="grid-2">
      <Card title="Company">
        <dl className="facts">
          <dt>Code</dt><dd>{transporter.transporterCode}</dd>
          <dt>Trade name</dt><dd>{transporter.tradeName ?? '—'}</dd>
          <dt>Type</dt><dd>{transporter.transporterType ?? '—'}</dd>
          <dt>PAN / GSTIN</dt><dd>{transporter.pan ?? '—'} / {transporter.gstin ?? '—'}</dd>
          <dt>Address</dt><dd>{[transporter.address, transporter.city, transporter.state, transporter.country].filter(Boolean).join(', ') || '—'}</dd>
          <dt>Created</dt><dd>{fmtDate(transporter.createdAt)}</dd>
        </dl>
      </Card>
      <Card title="Primary contact">
        <dl className="facts">
          <dt>Name</dt><dd>{transporter.primaryContactName ?? '—'}</dd>
          <dt>Email</dt><dd>{transporter.primaryContactEmail ?? '—'}</dd>
          <dt>Phone</dt><dd>{transporter.primaryContactPhone ?? '—'}</dd>
        </dl>
      </Card>
      <Card title="Capabilities">
        {transporter.capabilities.length === 0 ? <p className="empty">No capabilities recorded.</p> : (
          <ul className="chips">
            {transporter.capabilities.map((c) => <li key={c.id}><Badge tone={c.status === 'Active' ? 'good' : 'neutral'}>{c.name}</Badge></li>)}
          </ul>
        )}
      </Card>
    </div>
  );
}

export function FleetTab({ transporterId }: { transporterId: number }) {
  const vehicles = useQuery({ queryKey: ['vehicles', transporterId], queryFn: () => transporterApi.vehicles(transporterId) });
  const drivers = useQuery({ queryKey: ['drivers', transporterId], queryFn: () => transporterApi.drivers(transporterId) });
  const documents = useQuery({ queryKey: ['documents', transporterId], queryFn: () => transporterApi.documents(transporterId) });
  return (
    <div className="grid-2">
      <Card title={`Vehicles (${vehicles.data?.totalCount ?? '…'})`}>
        <ErrorBox error={vehicles.error} />
        {vehicles.isLoading ? <Loading /> : (
          <table className="data-table">
            <thead><tr><th>Registration</th><th>Payload (kg)</th><th>Availability</th><th>Status</th></tr></thead>
            <tbody>
              {vehicles.data?.items.map((v) => (
                <tr key={v.id}><td>{v.registrationNumber}</td><td className="num">{v.payloadCapacityKg.toLocaleString('en-IN')}</td><td>{v.availabilityStatus}</td><td>{v.status}</td></tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      <Card title={`Drivers (${drivers.data?.length ?? '…'})`}>
        <ErrorBox error={drivers.error} />
        {drivers.isLoading ? <Loading /> : (
          <table className="data-table">
            <thead><tr><th>Name</th><th>Mobile</th><th>Licence</th><th>Status</th></tr></thead>
            <tbody>
              {drivers.data?.map((d) => <tr key={d.id}><td>{d.fullName}</td><td>{d.mobile}</td><td>{d.licenceNumber}</td><td>{d.status}</td></tr>)}
            </tbody>
          </table>
        )}
      </Card>
      <Card title="Documents">
        <ErrorBox error={documents.error} />
        {documents.isLoading ? <Loading /> : (
          <table className="data-table">
            <thead><tr><th>Type</th><th>Number</th><th>Expiry</th><th>Verification</th></tr></thead>
            <tbody>
              {documents.data?.map((d) => (
                <tr key={d.id}>
                  <td>{d.documentTypeName}{d.driverId ? ' (driver)' : d.vehicleId ? ' (vehicle)' : ''}</td>
                  <td>{d.documentNumber ?? '—'}</td>
                  <td>{fmtDate(d.expiryDate)}</td>
                  <td><Badge tone={d.verificationStatus === 'Verified' ? 'good' : d.verificationStatus === 'Rejected' ? 'bad' : 'warn'}>{d.verificationStatus}</Badge></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </div>
  );
}

export function LanesTab({ transporterId }: { transporterId: number }) {
  const lanes = useQuery({ queryKey: ['lanes', transporterId], queryFn: () => transporterApi.lanes(transporterId) });
  if (lanes.isLoading) {
    return <Loading />;
  }
  return (
    <Card title={`Lanes (${lanes.data?.totalCount ?? 0})`}>
      <ErrorBox error={lanes.error} />
      {lanes.data?.items.length === 0 ? <p className="empty">No lanes configured.</p> : (
        <table className="data-table">
          <thead><tr><th>Origin ref</th><th>Destination ref</th><th>Service</th><th>Vehicle type ref</th><th>Transit SLA (min)</th><th>Effective from</th><th>Status</th></tr></thead>
          <tbody>
            {lanes.data?.items.map((l) => (
              <tr key={l.id}>
                <td>{l.originLocationReference}</td><td>{l.destinationLocationReference}</td><td>{l.serviceType}</td>
                <td>{l.vehicleTypeReference ?? '—'}</td><td className="num">{l.transitSlaMinutes ?? '—'}</td>
                <td>{fmtDate(l.effectiveFrom)}</td><td>{l.status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}

export function ComplianceTab({ transporterId }: { transporterId: number }) {
  const report = useQuery({ queryKey: ['compliance', transporterId], queryFn: () => transporterApi.compliance(transporterId) });
  if (report.isLoading) {
    return <Loading />;
  }
  if (!report.data) {
    return <ErrorBox error={report.error} />;
  }
  const r = report.data;
  const tone = r.overall === 'Compliant' ? 'good' : r.overall === 'ExpiringSoon' ? 'warn' : 'bad';
  return (
    <Card title="Compliance">
      <p>
        <Badge tone={tone}>{r.overall}</Badge>{' '}
        {r.approvalBlocked && <Badge tone="bad">Approval blocked</Badge>}{' '}
        {r.allocationBlocked && <Badge tone="bad">Allocation blocked</Badge>}
      </p>
      <table className="data-table">
        <thead><tr><th>Scope</th><th>Document</th><th>State</th><th>Days to expiry</th><th>Message</th></tr></thead>
        <tbody>
          {r.items.map((item, i) => (
            <tr key={i}>
              <td>{item.scope}</td><td>{item.documentTypeName}</td><td>{item.state}</td>
              <td className="num">{item.daysToExpiry ?? '—'}</td><td>{item.message}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  );
}
