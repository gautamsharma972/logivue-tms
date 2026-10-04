import { useMemo, type ReactNode } from 'react';
import {
  createColumnHelper,
  createSortedRowModel,
  rowSortingFeature,
  tableFeatures,
  useTable,
} from '@tanstack/react-table';

/** One column of a DataTable. `sort` makes the column sortable by the value it returns. */
export interface TableColumn<T> {
  id: string;
  header: string;
  cell: (row: T) => ReactNode;
  sort?: (row: T) => string | number | null;
  align?: 'left' | 'right';
}

const features = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
});

/**
 * Sortable table on TanStack Table v9. Headless: this component supplies the markup and the table supplies sort state.
 * Null sort values sort last, so unmeasured rows do not jump to the top.
 */
export function DataTable<T extends object>({
  rows,
  columns,
  rowKey,
  empty = 'Nothing to show.',
}: {
  rows: T[];
  columns: TableColumn<T>[];
  rowKey: (row: T) => string | number;
  empty?: string;
}) {
  const tableColumns = useMemo(() => {
    const helper = createColumnHelper<typeof features, T>();
    return columns.map((column) =>
      helper.accessor((row: T): unknown => (column.sort ? column.sort(row) : null), {
        id: column.id,
        header: column.header,
        cell: (ctx) => column.cell(ctx.row.original),
        enableSorting: Boolean(column.sort),
      }),
    );
  }, [columns]);
  const alignById = new Map(columns.map((c) => [c.id, c.align ?? 'left']));

  const table = useTable({ features, columns: tableColumns, data: rows });

  if (rows.length === 0) {
    return <p className="empty">{empty}</p>;
  }

  return (
    <div className="table-wrap">
      <table className="data-table">
        <thead>
          {table.getHeaderGroups().map((group) => (
            <tr key={group.id}>
              {group.headers.map((header) => {
                const sortable = header.column.getCanSort();
                const sorted = header.column.getIsSorted();
                return (
                  <th key={header.id} className={sortable ? 'sortable' : undefined}>
                    {header.isPlaceholder ? null : (
                      <button
                        type="button"
                        className="th-button"
                        disabled={!sortable}
                        onClick={() => header.column.toggleSorting()}
                      >
                        <table.FlexRender header={header} />
                        {sorted === 'asc' ? ' ▲' : sorted === 'desc' ? ' ▼' : ''}
                      </button>
                    )}
                  </th>
                );
              })}
            </tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map((row) => (
            <tr key={rowKey(row.original)}>
              {row.getAllCells().map((cell) => (
                <td key={cell.id} className={alignById.get(cell.column.id) === 'right' ? 'num' : undefined}>
                  <table.FlexRender cell={cell} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
