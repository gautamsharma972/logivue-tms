/** Formats a percentage. Null means not measurable, which is shown as a dash rather than a zero. */
export function fmtPct(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined ? '—' : `${value.toFixed(digits)}%`;
}

export function fmtScore(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : value.toFixed(1);
}

export function fmtNumber(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : value.toLocaleString('en-IN');
}

export function fmtMoney(value: number | null | undefined, currency = 'INR'): string {
  if (value === null || value === undefined) {
    return '—';
  }
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value);
}

export function fmtDate(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
}

export function fmtDateTime(value: string | null | undefined): string {
  return value
    ? new Date(value).toLocaleString('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
    : '—';
}

/** Splits a 'yyyy-mm-dd' date-only value. Dates from the API are used as text, so no timezone shift is applied. */
export function isoDay(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Start of the quarter that contains today, as a date-only string. Default period for performance views. */
export function defaultPeriod(): { from: string; to: string } {
  const now = new Date();
  const quarterStart = new Date(now.getFullYear(), Math.floor(now.getMonth() / 3) * 3, 1);
  return { from: isoDay(quarterStart), to: isoDay(now) };
}
