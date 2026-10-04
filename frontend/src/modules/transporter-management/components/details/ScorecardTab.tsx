import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { transporterApi } from '../../api';
import { Card, Loading, ErrorBox, KpiBar, Field } from '../../../../shared/ui';
import { defaultPeriod, fmtDate, fmtScore } from '../../../../shared/format';
import { KPI_LABELS } from '../../constants';
import type { Scorecard } from '../../types';

/** Scorecards show the score and every KPI under it, with the numerator and denominator behind each value. */
export function ScorecardTab({ transporterId }: { transporterId: number }) {
  const [period, setPeriod] = useState(defaultPeriod());
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const queryClient = useQueryClient();
  const scorecards = useQuery({ queryKey: ['scorecards', transporterId], queryFn: () => transporterApi.scorecards(transporterId) });

  const generate = useMutation({
    mutationFn: () => transporterApi.generateScorecard(transporterId, period),
    onSuccess: (created) => {
      setSelectedId(created.id);
      queryClient.invalidateQueries({ queryKey: ['scorecards', transporterId] });
    },
  });

  const list = scorecards.data ?? [];
  const selected: Scorecard | undefined = list.find((s) => s.id === selectedId) ?? list[0];
  // Trend runs oldest to newest; the API returns newest first.
  const trend = [...list].reverse().map((s) => ({ period: fmtDate(s.periodEnd), score: s.overallScore }));

  if (scorecards.isLoading) {
    return <Loading />;
  }

  return (
    <>
      <Card title="Generate scorecard">
        <div className="toolbar">
          <Field label="From"><input type="date" value={period.from} onChange={(e) => setPeriod({ ...period, from: e.target.value })} /></Field>
          <Field label="To"><input type="date" value={period.to} onChange={(e) => setPeriod({ ...period, to: e.target.value })} /></Field>
          <button type="button" disabled={generate.isPending} onClick={() => generate.mutate()}>Generate</button>
        </div>
        <ErrorBox error={generate.error ?? scorecards.error} />
        <p className="muted">Scorecards are generated from stored KPIs. Nothing here is edited by hand.</p>
      </Card>

      {selected && (
        <Card title={`Overall score: ${fmtScore(selected.overallScore)}`} actions={<span className="muted">Period {fmtDate(selected.periodStart)} to {fmtDate(selected.periodEnd)} · formula v{selected.calculationVersion}</span>}>
          {selected.kpis.map((k) => (
            <div key={k.kpi} className="scorecard-row">
              <KpiBar label={KPI_LABELS[k.kpi]} value={k.kpiValue} weight={k.weight} />
              <small className="muted">{k.denominator > 0 ? `${k.numerator} of ${k.denominator}` : 'Sample too small or no records'}</small>
            </div>
          ))}
        </Card>
      )}

      <div className="grid-2">
        <Card title="Trend">
          {trend.length < 2 ? <p className="empty">Generate at least two scorecards to see a trend.</p> : (
            <div className="chart">
              <ResponsiveContainer width="100%" height={260}>
                <LineChart data={trend}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="period" />
                  <YAxis domain={[0, 100]} />
                  <Tooltip />
                  <Line type="monotone" dataKey="score" name="Overall score" stroke="#2563eb" connectNulls />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}
        </Card>
        <Card title="History">
          <table className="data-table">
            <thead><tr><th>Generated</th><th>Period</th><th className="num">Score</th><th /></tr></thead>
            <tbody>
              {list.map((s) => (
                <tr key={s.id}>
                  <td>{fmtDate(s.generatedAt)}</td>
                  <td>{fmtDate(s.periodStart)} – {fmtDate(s.periodEnd)}</td>
                  <td className="num">{fmtScore(s.overallScore)}</td>
                  <td><button type="button" onClick={() => setSelectedId(s.id)}>View</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      </div>
    </>
  );
}
