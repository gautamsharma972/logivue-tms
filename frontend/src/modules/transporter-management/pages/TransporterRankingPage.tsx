import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { rankingApi, type RankingScopeParams } from '../api';
import { DataTable, type TableColumn } from '../../../shared/DataTable';
import { Card, ErrorBox, Loading, PageHeader, Badge } from '../../../shared/ui';
import { ExportMenu } from '../components/ExportMenu';
import { ScopeFilter } from '../components/ScopeFilter';
import { defaultPeriod, fmtPct, fmtScore } from '../../../shared/format';
import { RANKING_METRICS } from '../constants';
import type { RankedTransporter } from '../types';

/**
 * Ranking by metric. Transporters below the minimum sample are listed without a rank, so a few loads cannot put one
 * at the top. Scores use the same weights and sample rules as scorecards.
 */
export function TransporterRankingPage() {
  const [scope, setScope] = useState<RankingScopeParams>(defaultPeriod());
  const [applied, setApplied] = useState<RankingScopeParams>(defaultPeriod());
  const [metric, setMetric] = useState('OverallScore');

  const ranking = useQuery({
    queryKey: ['ranking', applied, metric],
    queryFn: () => rankingApi.rank(applied, metric),
  });

  const rows = ranking.data?.rows ?? [];
  const ranked = rows.filter((r) => r.ranked);
  const metricLabel = RANKING_METRICS.find((m) => m.value === metric)?.label ?? metric;
  const format = (value: number | null) => (metric === 'OverallScore' ? fmtScore(value) : fmtPct(value, 2));

  const columns: TableColumn<RankedTransporter>[] = [
    { id: 'rank', header: 'Rank', align: 'right', cell: (r) => r.rank ?? '—', sort: (r) => r.rank },
    { id: 'name', header: 'Transporter', cell: (r) => <Link to={`/transporters/${r.transporterId}`}>{r.transporterName}</Link>, sort: (r) => r.transporterName },
    { id: 'region', header: 'Region', cell: (r) => r.region ?? '—', sort: (r) => r.region },
    { id: 'value', header: metricLabel, align: 'right', cell: (r) => format(r.metricValue), sort: (r) => r.metricValue },
    { id: 'overall', header: 'Overall score', align: 'right', cell: (r) => fmtScore(r.overallScore), sort: (r) => r.overallScore },
    {
      id: 'note',
      header: 'Status',
      cell: (r) => (r.ranked ? <Badge tone="good">Ranked</Badge> : <Badge tone="warn">{r.note ?? 'Not ranked'}</Badge>),
    },
  ];

  const exportColumns = [
    { header: 'Rank', value: (r: RankedTransporter) => r.rank },
    { header: 'Code', value: (r: RankedTransporter) => r.transporterCode },
    { header: 'Transporter', value: (r: RankedTransporter) => r.transporterName },
    { header: 'Region', value: (r: RankedTransporter) => r.region },
    { header: metricLabel, value: (r: RankedTransporter) => r.metricValue },
    { header: 'Overall score', value: (r: RankedTransporter) => r.overallScore },
    { header: 'Note', value: (r: RankedTransporter) => r.note },
  ];

  return (
    <>
      <PageHeader
        title="Transporter ranking"
        subtitle={ranking.data ? `Scope: ${ranking.data.scopeLabel}` : 'Choose a period and scope'}
        actions={<ExportMenu baseName="transporter-ranking" rows={rows} columns={exportColumns} disabled={rows.length === 0} />}
      />
      <Card>
        <ScopeFilter value={scope} onChange={setScope} />
        <div className="toolbar">
          <select value={metric} onChange={(e) => setMetric(e.target.value)} aria-label="Rank by">
            {RANKING_METRICS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </select>
          <button type="button" onClick={() => setApplied(scope)}>Apply</button>
        </div>
      </Card>
      <ErrorBox error={ranking.error} />
      {ranking.isLoading ? <Loading /> : (
        <>
          {ranked.length > 0 && (
            <Card title={`Top ${Math.min(10, ranked.length)} on ${metricLabel}`}>
              <div className="chart">
                <ResponsiveContainer width="100%" height={280}>
                  <BarChart data={ranked.slice(0, 10).map((r) => ({ name: r.transporterCode, value: r.metricValue }))}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="name" />
                    <YAxis />
                    <Tooltip />
                    <Bar dataKey="value" name={metricLabel} fill="#2563eb" />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </Card>
          )}
          <DataTable rows={rows} columns={columns} rowKey={(r) => r.transporterId} empty="No transporters have KPI records in this period." />
        </>
      )}
    </>
  );
}
