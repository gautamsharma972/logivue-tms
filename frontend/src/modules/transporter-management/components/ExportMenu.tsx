import { exportRows, type ExportColumn, type ExportFormat } from '../../../shared/export';

/** Exports the rows on screen. CSV and Excel (.xls) are offered; the files are built in the browser from what is shown. */
export function ExportMenu<T>({
  baseName,
  rows,
  columns,
  disabled,
}: {
  baseName: string;
  rows: T[];
  columns: ExportColumn<T>[];
  disabled?: boolean;
}) {
  const run = (format: ExportFormat) => exportRows(baseName, rows, columns, format);
  return (
    <div className="export-menu" role="group" aria-label="Export">
      <button type="button" disabled={disabled} onClick={() => run('csv')}>Export CSV</button>
      <button type="button" disabled={disabled} onClick={() => run('xls')}>Export Excel</button>
    </div>
  );
}
