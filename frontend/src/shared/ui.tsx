import type { ReactNode } from 'react';
import { ApiError } from '../core/http';

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <header className="page-header">
      <div>
        <h1>{title}</h1>
        {subtitle && <p className="subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </header>
  );
}

export function Card({ title, actions, children }: { title?: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <section className="card">
      {(title || actions) && (
        <div className="card-head">
          {title && <h2>{title}</h2>}
          {actions && <div className="card-actions">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

export type Tone = 'good' | 'warn' | 'bad' | 'neutral';

export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>;
}

/** A 0-100 score or KPI as a bar. Null renders as "not measurable" rather than an empty bar. */
export function KpiBar({ label, value, weight }: { label: string; value: number | null; weight?: number }) {
  const clamped = value === null ? 0 : Math.max(0, Math.min(100, value));
  const tone: Tone = value === null ? 'neutral' : value >= 95 ? 'good' : value >= 85 ? 'warn' : 'bad';
  return (
    <div className="kpi-bar">
      <div className="kpi-label">
        <span>{label}</span>
        <span className="kpi-value">
          {value === null ? 'Not measurable' : `${value.toFixed(1)}%`}
          {weight !== undefined && <span className="kpi-weight"> · weight {weight}</span>}
        </span>
      </div>
      <div className="kpi-track">
        <div className={`kpi-fill kpi-${tone}`} style={{ width: `${clamped}%` }} />
      </div>
    </div>
  );
}

export function Tabs<K extends string>({
  tabs,
  active,
  onChange,
}: {
  tabs: { key: K; label: string }[];
  active: K;
  onChange: (key: K) => void;
}) {
  return (
    <nav className="tabs" role="tablist">
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          role="tab"
          aria-selected={tab.key === active}
          className={tab.key === active ? 'tab active' : 'tab'}
          onClick={() => onChange(tab.key)}
        >
          {tab.label}
        </button>
      ))}
    </nav>
  );
}

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return <p className="loading">{label}</p>;
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }
  const api = error instanceof ApiError ? error : null;
  const message = api ? api.message : error instanceof Error ? error.message : 'Something went wrong.';
  return (
    <div className="error-box" role="alert">
      <strong>{api ? `${api.code}` : 'Error'}</strong> {message}
      {api?.details.map((d, i) => (
        <div key={i} className="error-detail">
          {d.field ? `${d.field}: ` : ''}
          {d.message}
        </div>
      ))}
    </div>
  );
}

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
    </label>
  );
}
