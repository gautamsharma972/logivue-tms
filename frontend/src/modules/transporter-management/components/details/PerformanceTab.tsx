import { useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { transporterApi } from '../../api';
import { Card, Loading, ErrorBox, Field, KpiBar, Badge } from '../../../../shared/ui';
import { defaultPeriod, fmtDate, fmtMoney, fmtPct, isoDay } from '../../../../shared/format';
import { CLAIM_TYPES, KPI_LABELS, SCORED_KPIS } from '../../constants';
import type { KpiRow } from '../../types';

/**
 * Operational KPIs for a period, with the records that feed them. Claims, invoiced cost and capacity are entered here
 * (or by the vendor for capacity); each save rebuilds the KPI for its month, so nothing is typed into a KPI directly.
 */
export function PerformanceTab({ transporterId }: { transporterId: number }) {
  const [period, setPeriod] = useState(defaultPeriod());
  const queryClient = useQueryClient();
  const operations = useQuery({
    queryKey: ['operations', transporterId, period.from, period.to],
    queryFn: () => transporterApi.operations(transporterId, period),
  });
  const claims = useQuery({
    queryKey: ['claims', transporterId, period.from, period.to],
    queryFn: () => transporterApi.claims(transporterId, period),
  });
  const capacity = useQuery({
    queryKey: ['capacity', transporterId, period.from, period.to],
    queryFn: () => transporterApi.capacity(transporterId, period),
  });

  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: ['operations', transporterId] });
    queryClient.invalidateQueries({ queryKey: ['claims', transporterId] });
    queryClient.invalidateQueries({ queryKey: ['capacity', transporterId] });
  };

  const resolve = useMutation({
    mutationFn: (claimId: number) => transporterApi.resolveClaim(transporterId, claimId),
    onSuccess: refresh,
  });

  const kpiByType = new Map<string, KpiRow>((operations.data?.kpis ?? []).map((k) => [k.kpi, k]));

  return (
    <>
      <Card title="Period">
        <div className="filter-grid">
          <Field label="From"><input type="date" value={period.from} onChange={(e) => setPeriod({ ...period, from: e.target.value })} /></Field>
          <Field label="To"><input type="date" value={period.to} onChange={(e) => setPeriod({ ...period, to: e.target.value })} /></Field>
        </div>
      </Card>

      <Card title="Operational KPIs">
        <ErrorBox error={operations.error} />
        {operations.isLoading ? <Loading /> : (
          <div className="kpi-grid">
            {SCORED_KPIS.map((kind) => {
              const row = kpiByType.get(kind);
              const value = row?.value ?? null;
              return (
                <div key={kind}>
                  {kind === 'ClaimsRate' ? (
                    <p className="kpi-plain">
                      <span>{KPI_LABELS[kind]} (lower is better)</span>{' '}
                      <strong>{value === null ? 'Not measurable' : fmtPct(value, 2)}</strong>
                    </p>
                  ) : (
                    <KpiBar label={KPI_LABELS[kind]} value={value} />
                  )}
                  <small className="muted">{row ? `${row.numerator} of ${row.denominator}` : 'No records in this period'}</small>
                </div>
              );
            })}
          </div>
        )}
      </Card>

      <div className="grid-2">
        <Card title="Claims">
          <ClaimForm transporterId={transporterId} onSaved={refresh} />
          <ErrorBox error={claims.error ?? resolve.error} />
          {claims.isLoading ? <Loading /> : (
            <table className="data-table">
              <thead><tr><th>Date</th><th>Type</th><th>Load</th><th>Value</th><th>Status</th><th /></tr></thead>
              <tbody>
                {claims.data?.length === 0 && <tr><td colSpan={6} className="empty">No claims in this period.</td></tr>}
                {claims.data?.map((c) => (
                  <tr key={c.id}>
                    <td>{fmtDate(c.claimDate)}</td><td>{c.claimType}</td><td>{c.loadReference ?? '—'}</td>
                    <td className="num">{fmtMoney(c.claimValue)}</td>
                    <td><Badge tone={c.status === 'Open' ? 'warn' : 'good'}>{c.status}</Badge></td>
                    <td>{c.status === 'Open' && <button type="button" onClick={() => resolve.mutate(c.id)}>Resolve</button>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card title="Invoiced cost">
          <CostForm transporterId={transporterId} onSaved={refresh} />
          <p className="muted">Each load can carry one cost record. Cost performance is the share of loads invoiced at or below the agreed amount.</p>
        </Card>
      </div>

      <Card title="Vehicle capacity">
        <ErrorBox error={capacity.error} />
        {capacity.isLoading ? <Loading /> : (
          <table className="data-table">
            <thead><tr><th>Date</th><th className="num">Committed</th><th className="num">Available</th><th className="num">Availability</th></tr></thead>
            <tbody>
              {capacity.data?.length === 0 && <tr><td colSpan={4} className="empty">No capacity reported in this period.</td></tr>}
              {capacity.data?.map((d) => (
                <tr key={d.id}>
                  <td>{fmtDate(d.date)}</td><td className="num">{d.vehiclesCommitted}</td><td className="num">{d.vehiclesAvailable}</td>
                  <td className="num">{d.vehiclesCommitted === 0 ? '—' : fmtPct((d.vehiclesAvailable * 100) / d.vehiclesCommitted)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}

function ClaimForm({ transporterId, onSaved }: { transporterId: number; onSaved: () => void }) {
  const [today] = useState(() => isoDay(new Date()));
  const [claimType, setClaimType] = useState<(typeof CLAIM_TYPES)[number]>('Damage');
  const [claimDate, setClaimDate] = useState(() => isoDay(new Date()));
  const [claimValue, setClaimValue] = useState('');
  const [loadReference, setLoadReference] = useState('');
  const save = useMutation({
    mutationFn: () => transporterApi.recordClaim(transporterId, {
      claimType,
      claimDate,
      claimValue: Number(claimValue),
      loadReference: loadReference || undefined,
    }),
    onSuccess: () => {
      setClaimValue('');
      setLoadReference('');
      onSaved();
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <form className="inline-form" onSubmit={submit}>
      <select value={claimType} onChange={(e) => setClaimType(e.target.value as (typeof CLAIM_TYPES)[number])}>
        {CLAIM_TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
      </select>
      <input type="date" value={claimDate} max={today} onChange={(e) => setClaimDate(e.target.value)} />
      <input placeholder="Load ref" value={loadReference} onChange={(e) => setLoadReference(e.target.value)} />
      <input placeholder="Value (INR)" inputMode="decimal" value={claimValue} onChange={(e) => setClaimValue(e.target.value)} required />
      <button type="submit" disabled={save.isPending}>Record claim</button>
      <ErrorBox error={save.error} />
    </form>
  );
}

function CostForm({ transporterId, onSaved }: { transporterId: number; onSaved: () => void }) {
  const [loadReference, setLoadReference] = useState('');
  const [serviceDate, setServiceDate] = useState(() => isoDay(new Date()));
  const [agreed, setAgreed] = useState('');
  const [invoiced, setInvoiced] = useState('');
  const save = useMutation({
    mutationFn: () => transporterApi.recordLoadCost(transporterId, {
      loadReference,
      serviceDate,
      agreedAmount: Number(agreed),
      invoicedAmount: Number(invoiced),
    }),
    onSuccess: () => {
      setLoadReference('');
      setAgreed('');
      setInvoiced('');
      onSaved();
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    save.mutate();
  };
  return (
    <form className="inline-form" onSubmit={submit}>
      <input placeholder="Load ref" value={loadReference} onChange={(e) => setLoadReference(e.target.value)} required />
      <input type="date" value={serviceDate} onChange={(e) => setServiceDate(e.target.value)} />
      <input placeholder="Agreed (INR)" inputMode="decimal" value={agreed} onChange={(e) => setAgreed(e.target.value)} required />
      <input placeholder="Invoiced (INR)" inputMode="decimal" value={invoiced} onChange={(e) => setInvoiced(e.target.value)} required />
      <button type="submit" disabled={save.isPending}>Record cost</button>
      <ErrorBox error={save.error} />
    </form>
  );
}
