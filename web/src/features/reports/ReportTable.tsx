import { Table, Tag } from 'antd'
import type { TablePaginationConfig } from 'antd'
import type { SorterResult } from 'antd/es/table/interface'
import { Link } from 'react-router-dom'
import { canDrill, drillLink } from './drill'
import { formatCell, toneOf } from './format'
import type { ColumnDto, DrillDto, ReportRow } from './types'

interface Props {
  columns: ColumnDto[]
  rows: ReportRow[]
  total: number
  page: number
  pageSize: number
  drills: DrillDto[]
  filters: Record<string, string>
  sort: { field: string; direction: 'ASC' | 'DESC' } | null
  loading?: boolean
  onChange: (change: { page: number; pageSize: number; sort: { field: string; direction: 'ASC' | 'DESC' } | null }) => void
}

const NUMERIC = new Set(['Whole', 'Number', 'Percent', 'Currency', 'Duration'])

/** The detailed rows, paged and sorted by the server. A value that can be drilled into is a link that keeps the filters the person is looking at. */
export function ReportTable({ columns, rows, total, page, pageSize, drills, filters, sort, loading, onChange }: Props) {
  const antColumns = columns.map((c) => {
    const drill = drills.find((d) => d.field === c.field)
    return {
      key: c.field,
      dataIndex: c.field,
      title: c.displayName,
      sorter: c.sortable,
      sortOrder: sort?.field === c.field ? (sort.direction === 'ASC' ? ('ascend' as const) : ('descend' as const)) : null,
      align: NUMERIC.has(c.dataType) ? ('right' as const) : ('left' as const),
      ellipsis: c.dataType === 'Text',
      render: (value: unknown, row: ReportRow) => {
        const text = formatCell(value as ReportRow[string], c.dataType)
        if (drill && value !== null && value !== undefined && value !== '' && canDrill(drill, row)) {
          return (
            <Link to={drillLink(drill.targetReport, drill.map, row, filters)} title={drill.label}>
              {text}
            </Link>
          )
        }
        if (c.dataType === 'Status' && typeof value === 'string') return <Tag color={toneOf(value)}>{value}</Tag>
        return text
      },
    }
  })
  const handle = (pagination: TablePaginationConfig, _filters: unknown, sorter: SorterResult<ReportRow> | SorterResult<ReportRow>[]) => {
    const s = Array.isArray(sorter) ? sorter[0] : sorter
    const next = s?.order && s.columnKey ? { field: String(s.columnKey), direction: s.order === 'ascend' ? ('ASC' as const) : ('DESC' as const) } : null
    onChange({ page: pagination.current ?? 1, pageSize: pagination.pageSize ?? pageSize, sort: next })
  }
  return (
    <Table<ReportRow>
      size="small"
      rowKey={(_, i) => String(i)}
      columns={antColumns}
      dataSource={rows}
      loading={loading}
      scroll={{ x: 'max-content' }}
      onChange={handle}
      pagination={{ current: page, pageSize, total, showSizeChanger: true, pageSizeOptions: [25, 50, 100, 200], showTotal: (t) => `${t.toLocaleString('en-IN')} rows` }}
      locale={{ emptyText: 'No data found for the selected filters.' }}
    />
  )
}
