/** Styles shared by the LR and the invoice printouts: A5 landscape (210 x 148 mm), small type, 8 mm border. */
export const a5SheetCss = `
.a5-sheet { max-width: 210mm; margin: 0 auto; padding: 8mm; background: #fff; color: #000; font-size: 11px; line-height: 1.35 }
.a5-sheet .a5-kind { font-size: 14px; font-weight: 600; margin: 0 }
.a5-sheet .a5-number { font-size: 22px; font-weight: 700; margin: 0 0 2px }
.a5-sheet .ant-divider { margin: 6px 0 }
.a5-sheet .ant-descriptions-item-label, .a5-sheet .ant-descriptions-item-content, .a5-sheet .ant-table-cell { padding: 3px 6px !important; font-size: 11px !important; white-space: nowrap }
.a5-sheet .ant-descriptions-item-content { white-space: normal }
.a5-sheet .ant-table { overflow: visible }
.a5-sheet .ant-table-footer { padding: 3px 6px !important; font-size: 11px }
@media print {
  .no-print, .app-footer, .ant-layout-sider, .ant-layout-header, .ant-float-btn-group { display: none !important }
  body, .ant-layout { background: #fff !important }
  .ant-layout-content { padding: 0 !important; max-width: none !important }
  .a5-sheet { max-width: none; padding: 8mm 8mm 0 }
}
/* Top level, not inside @media: browsers ignore a nested @page. The top and sides have no page margin, because the browser prints its date, title and address in a margin; the 8 mm border is the sheet's own padding.
   The bottom margin carries only the page number, drawn by the page itself, which also stops the browser adding its own footer. */
@page { size: A5 landscape; margin: 0 0 8mm 0; @bottom-right { content: counter(page) " / " counter(pages); font: 10px sans-serif; color: #000; margin-right: 8mm } }`
