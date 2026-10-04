/**
 * Export of the rows on screen. CSV opens everywhere. "Excel" is SpreadsheetML 2003 XML with an .xls name, which Excel
 * opens natively without a library. PDF is not offered: the server is the right place for it when it is needed.
 */
export interface ExportColumn<T> {
  header: string;
  value: (row: T) => string | number | null | undefined;
}

export type ExportFormat = 'csv' | 'xls';

function csvCell(value: string | number | null | undefined): string {
  if (value === null || value === undefined) {
    return '';
  }
  const text = String(value);
  return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function xmlEscape(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export function buildCsv<T>(rows: T[], columns: ExportColumn<T>[]): string {
  const header = columns.map((c) => csvCell(c.header)).join(',');
  const body = rows.map((row) => columns.map((c) => csvCell(c.value(row))).join(','));
  return [header, ...body].join('\r\n');
}

export function buildSpreadsheetXml<T>(rows: T[], columns: ExportColumn<T>[], sheetName: string): string {
  const cell = (value: string | number | null | undefined) => {
    if (value === null || value === undefined) {
      return '<Cell><Data ss:Type="String"></Data></Cell>';
    }
    const type = typeof value === 'number' ? 'Number' : 'String';
    return `<Cell><Data ss:Type="${type}">${xmlEscape(String(value))}</Data></Cell>`;
  };
  const header = `<Row>${columns.map((c) => cell(c.header)).join('')}</Row>`;
  const body = rows.map((row) => `<Row>${columns.map((c) => cell(c.value(row))).join('')}</Row>`).join('');
  return `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
<Worksheet ss:Name="${xmlEscape(sheetName).slice(0, 31)}"><Table>${header}${body}</Table></Worksheet>
</Workbook>`;
}

export function exportRows<T>(baseName: string, rows: T[], columns: ExportColumn<T>[], format: ExportFormat): void {
  const stamp = new Date().toISOString().slice(0, 10);
  const fileName = `${baseName}-${stamp}.${format}`;
  const blob =
    format === 'csv'
      ? new Blob(['﻿', buildCsv(rows, columns)], { type: 'text/csv;charset=utf-8' })
      : new Blob([buildSpreadsheetXml(rows, columns, baseName)], { type: 'application/vnd.ms-excel' });

  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
