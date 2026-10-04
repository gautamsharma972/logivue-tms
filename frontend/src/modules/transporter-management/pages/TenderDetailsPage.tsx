import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { tenderApi } from '../api';
import { Badge, Card, ErrorBox, Loading, PageHeader } from '../../../shared/ui';
import { fmtDateTime, fmtMoney } from '../../../shared/format';

export function TenderDetailsPage() {
  const { id } = useParams();
  const tender = useQuery({ queryKey: ['tender', id], queryFn: () => tenderApi.get(Number(id)), enabled: Boolean(id) });

  if (tender.isLoading) {
    return <Loading />;
  }
  if (!tender.data) {
    return <ErrorBox error={tender.error} />;
  }

  const { invitation: inv, responses, events } = tender.data;
  return (
    <>
      <PageHeader title={inv.tenderNumber} subtitle={`${inv.transporterName} · ${inv.loadReference}`} actions={<Link to="/tenders">All tenders</Link>} />
      <div className="grid-2">
        <Card title="Load">
          <dl className="facts">
            <dt>Status</dt><dd><Badge>{inv.status}</Badge></dd>
            <dt>Type</dt><dd>{inv.tenderType}{inv.sequenceNumber ? ` · step ${inv.sequenceNumber}` : ''}</dd>
            <dt>Service</dt><dd>{inv.serviceType}</dd>
            <dt>Weight</dt><dd>{inv.weightKg.toLocaleString('en-IN')} kg</dd>
            <dt>Offered rate</dt><dd>{fmtMoney(inv.offeredRate, inv.currency)}</dd>
            <dt>Pickup</dt><dd>{fmtDateTime(inv.pickupDateTime)}</dd>
            <dt>Delivery</dt><dd>{fmtDateTime(inv.deliveryDateTime)}</dd>
            <dt>Response deadline</dt><dd>{fmtDateTime(inv.responseDeadline)}</dd>
          </dl>
        </Card>
        <Card title="Responses">
          {responses.length === 0 ? <p className="empty">No responses yet.</p> : (
            <table className="data-table">
              <thead><tr><th>Response</th><th>At</th><th className="num">Quoted</th><th>Reason / comments</th></tr></thead>
              <tbody>
                {responses.map((r) => (
                  <tr key={r.id}><td>{r.response}</td><td>{fmtDateTime(r.responseAt)}</td><td className="num">{r.quotedRate ?? '—'}</td><td>{r.reason ?? r.comments ?? '—'}</td></tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>
      </div>
      <Card title="History">
        <table className="data-table">
          <thead><tr><th>When</th><th>Event</th><th>By</th><th>Comments</th></tr></thead>
          <tbody>
            {events.map((e) => (
              <tr key={e.id}><td>{fmtDateTime(e.eventAt)}</td><td>{e.eventType}</td><td>{e.performedBy}</td><td>{e.comments ?? '—'}</td></tr>
            ))}
          </tbody>
        </table>
      </Card>
    </>
  );
}
