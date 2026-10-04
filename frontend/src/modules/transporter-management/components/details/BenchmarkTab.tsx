import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { rankingApi, type RankingScopeParams } from '../../api';
import { Card, ErrorBox, Loading } from '../../../../shared/ui';
import { defaultPeriod, fmtPct } from '../../../../shared/format';
import { KPI_LABELS, LOWER_IS_BETTER, SCORED_KPIS } from '../../constants';
import { ScopeFilter } from '../ScopeFilter';

/** Peer comparison for one transporter. Gaps are signed: negative means behind the comparison, for claims too. */
export function BenchmarkTab({ transporterId }: { transporterId: number }) {
  const [scope, setScope] = useState<RankingScopeParams>({ ...defaultPeriod() });
  const [applied, setApplied] = useState<RankingScopeParams>({ ...defaultPeriod() });
  const benchmark = useQuery({
    queryKey: ['benchmark', transporterId, applied],
    queryFn: () => rankingApi.benchmark(transporterId, applied),
  });

  const rows = (benchmark.data?.rows ?? []).filter((r) => SCORED_KPIS.includes(r.kpi));
  const chart = rows.map((r) => ({
    kpi: KPI_LABELS[r.kpi].replace(/ \(.*\)/, ''),
    transporter: r.transporter,
    top: r.topPerformer,
    lane: r.laneAverage,
  }));

  return (
    <>
      <Card title="Scope">
        <ScopeFilter value={scope} onChange={setScope} showTransporterCategory={false} />
        <div className="toolbar">
          <button type="button" onClick={() => setApplied(scope)}>Compare</button>
          <span className="muted">Scope: {benchmark.data?.scopeLabel ?? '—'}</span>
        </div>
      </Card>
      <ErrorBox error={benchmark.error} />
      {benchmark.isLoading ? <Loading /> : benchmark.data && (
        <>
          <Card title="Transporter against peers">
            <div className="chart">
              <ResponsiveContainer width="100%" height={300}>
                <BarChart data={chart}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="kpi" interval={0} angle={-15} textAnchor="end" height={60} />
                  <YAxis domain={[0, 100]} />
                  <Tooltip />
                  <Legend />
                  <Bar dataKey="transporter" name="This transporter" fill="#2563eb" />
                  <Bar dataKey="lane" name="Lane average" fill="#94a3b8" />
                  <Bar dataKey="top" name="Top performer" fill="#16a34a" />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </Card>
          <Card title="Gaps">
            <table className="data-table">
              <thead>
                <tr>
                  <th>KPI</th><th className="num">This</th><th className="num">Lane avg</th><th className="num">Region avg</th>
                  <th className="num">Category avg</th><th className="num">Top performer</th><th className="num">Gap to top</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.kpi}>
                    <td>{KPI_LABELS[r.kpi]}{LOWER_IS_BETTER.includes(r.kpi) ? ' (lower is better)' : ''}</td>
                    <td className="num">{fmtPct(r.transporter, 2)}</td>
                    <td className="num">{fmtPct(r.laneAverage, 2)}</td>
                    <td className="num">{fmtPct(r.regionAverage, 2)}</td>
                    <td className="num">{fmtPct(r.categoryAverage, 2)}</td>
                    <td className="num">{fmtPct(r.topPerformer, 2)}</td>
                    <td className={`num ${r.gapToTop !== null && r.gapToTop < 0 ? 'gap-bad' : ''}`}>
                      {r.gapToTop === null ? '—' : `${r.gapToTop > 0 ? '+' : ''}${r.gapToTop.toFixed(2)} pts`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="muted">Region and category averages need the transporter's state or category to be set. Lane averages need lane-level records in the period.</p>
          </Card>
        </>
      )}
    </>
  );
}
