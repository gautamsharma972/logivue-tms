const dateTime = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' })

export function formatDateTime(value: string | null | undefined): string {
  return value ? dateTime.format(new Date(value)) : '—'
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase() ?? '')
    .join('')
}

const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 })

export function formatInr(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : inr.format(value)
}

const inrExact = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 })

/** Money that will be compared with an invoice: always shows paise, never rounds for display. */
export function formatInrExact(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : inrExact.format(value)
}
