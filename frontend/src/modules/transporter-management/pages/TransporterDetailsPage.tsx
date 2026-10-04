import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { transporterApi } from '../api';
import { ErrorBox, Loading, PageHeader, Tabs, Badge, type Tone } from '../../../shared/ui';
import { BenchmarkTab } from '../components/details/BenchmarkTab';
import { ComplianceTab, FleetTab, LanesTab, OverviewTab } from '../components/details/DetailTabs';
import { PerformanceTab } from '../components/details/PerformanceTab';
import { ScorecardTab } from '../components/details/ScorecardTab';
import type { TransporterStatus } from '../types';

type TabKey = 'overview' | 'fleet' | 'lanes' | 'compliance' | 'performance' | 'scorecard' | 'benchmark';

const TABS: { key: TabKey; label: string }[] = [
  { key: 'overview', label: 'Overview' },
  { key: 'fleet', label: 'Fleet & drivers' },
  { key: 'lanes', label: 'Lanes' },
  { key: 'compliance', label: 'Compliance' },
  { key: 'performance', label: 'Performance' },
  { key: 'scorecard', label: 'Scorecard' },
  { key: 'benchmark', label: 'Benchmark' },
];

const STATUS_TONE: Partial<Record<TransporterStatus, Tone>> = { Active: 'good', Approved: 'good', Suspended: 'warn', Blacklisted: 'bad' };

/** Transporter 360: one page with tabs, so related information is a click away rather than another screen. */
export function TransporterDetailsPage() {
  const { id } = useParams();
  const transporterId = Number(id);
  const [tab, setTab] = useState<TabKey>('overview');
  const transporter = useQuery({
    queryKey: ['transporter', transporterId],
    queryFn: () => transporterApi.get(transporterId),
    enabled: Number.isFinite(transporterId),
  });

  if (transporter.isLoading) {
    return <Loading />;
  }
  if (transporter.error || !transporter.data) {
    return <ErrorBox error={transporter.error} />;
  }

  const t = transporter.data;
  return (
    <>
      <PageHeader
        title={t.legalName}
        subtitle={`${t.transporterCode}${t.tradeName ? ` · ${t.tradeName}` : ''}`}
        actions={
          <>
            <Badge tone={STATUS_TONE[t.status] ?? 'neutral'}>{t.status}</Badge>{' '}
            <Link to="/transporters">All transporters</Link>
          </>
        }
      />
      <Tabs tabs={TABS} active={tab} onChange={setTab} />
      {tab === 'overview' && <OverviewTab transporter={t} />}
      {tab === 'fleet' && <FleetTab transporterId={transporterId} />}
      {tab === 'lanes' && <LanesTab transporterId={transporterId} />}
      {tab === 'compliance' && <ComplianceTab transporterId={transporterId} />}
      {tab === 'performance' && <PerformanceTab transporterId={transporterId} />}
      {tab === 'scorecard' && <ScorecardTab transporterId={transporterId} />}
      {tab === 'benchmark' && <BenchmarkTab transporterId={transporterId} />}
    </>
  );
}
