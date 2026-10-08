import type { DrillDto, ReportCell, ReportRow } from './types'

/** Filter names that describe how a report is shown, not what it is about: they do not travel into the next report. */
const PRESENTATION = new Set(['search', 'rankBy', 'metric', 'by'])

/**
 * The address of the report a click leads to. The filters the person is looking at (period, carrier, lane …) are kept; the clicked row's own values
 * (mapped by the drill) are added on top, and a value that starts with "=" is a fixed one. With no row (a card, a total) every value is fixed. Nothing is dropped unless they remove it on the next page.
 */
export function drillLink(targetReport: string, map: Record<string, string> | null | undefined, row: ReportRow | null, current: Record<string, string>, literalOnly = false): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(current)) {
    if (value && !key.startsWith('f_') && !PRESENTATION.has(key)) params.set(key, value)
  }
  for (const [filter, source] of Object.entries(map ?? {})) {
    if (source.startsWith('=')) {
      params.set(filter, source.slice(1))
    } else if (literalOnly) {
      params.set(filter, source) // a KPI card, total or section figure has no row: its filters are the values themselves
    } else if (row) {
      const value: ReportCell = row[source] ?? null
      if (value !== null && value !== '') params.set(filter, String(value))
    }
  }
  const query = params.toString()
  return `/reports/${targetReport}${query ? `?${query}` : ''}`
}

/** Can this drill be followed from this row (every mapped value present)? */
export function canDrill(drill: DrillDto, row: ReportRow): boolean {
  return Object.values(drill.map).every((source) => source.startsWith('=') || (row[source] !== null && row[source] !== undefined && row[source] !== ''))
}

/** Reads a report page's filters from its address: everything except the paging, grouping and sorting controls. */
export function filtersFromSearch(search: URLSearchParams): Record<string, string> {
  const result: Record<string, string> = {}
  search.forEach((value, key) => {
    if (!['page', 'pageSize', 'groupBy', 'sort'].includes(key) && value) result[key] = value
  })
  return result
}
