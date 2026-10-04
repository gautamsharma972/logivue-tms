import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { transporterApi, type RankingScopeParams } from '../api';
import { PageHeader, Card, Loading } from '../../../shared/ui';
import { BenchmarkTab } from '../components/details/BenchmarkTab';
import { defaultPeriod } from '../../../shared/format';

/** Benchmark for any transporter. Picks the transporter here; the comparison itself is the same view as the details tab. */
export function TransporterBenchmarkPage() {
  const [selected, setSelected] = useState<number | null>(null);
  const transporters = useQuery({
    queryKey: ['transporter-options'],
    queryFn: () => transporterApi.list({ pageSize: 100 }),
  });
  const period: RankingScopeParams = defaultPeriod();

  return (
    <>
      <PageHeader title="Transporter benchmark" subtitle="Compare a transporter with its lane, region, category and the top performer." />
      <Card>
        {transporters.isLoading ? <Loading /> : (
          <select value={selected ?? ''} onChange={(e) => setSelected(e.target.value ? Number(e.target.value) : null)} aria-label="Transporter">
            <option value="">Select a transporter</option>
            {transporters.data?.items.map((t) => (
              <option key={t.id} value={t.id}>{t.transporterCode} · {t.legalName}</option>
            ))}
          </select>
        )}
        <span className="muted"> Default period: {period.from} to {period.to}</span>
      </Card>
      {selected !== null && <BenchmarkTab transporterId={selected} />}
    </>
  );
}
