/**
 * Saves a printed sheet (the LR on A5 landscape, the invoice on A4 portrait) as a PDF built by the app itself, so no browser header or footer
 * (date, page title, address) can appear on it. The libraries load only when the button is pressed.
 */
export async function downloadSheetPdf(sheet: HTMLElement, fileName: string, paper: { format: 'a4' | 'a5'; orientation: 'portrait' | 'landscape' } = { format: 'a5', orientation: 'landscape' }): Promise<void> {
  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import('html2canvas'), import('jspdf')])
  const canvas = await html2canvas(sheet, { scale: 3, backgroundColor: '#ffffff', ignoreElements: (el) => el.classList.contains('no-print') })
  const pdf = new jsPDF({ orientation: paper.orientation, unit: 'mm', format: paper.format })
  const pageW = pdf.internal.pageSize.getWidth()
  const pageH = pdf.internal.pageSize.getHeight()
  // Fit the sheet on the page, keeping its proportions; a sheet taller than the page is shrunk rather than cut.
  const scale = Math.min(pageW / canvas.width, pageH / canvas.height)
  const w = canvas.width * scale
  const h = canvas.height * scale
  pdf.addImage(canvas.toDataURL('image/jpeg', 0.95), 'JPEG', (pageW - w) / 2, 0, w, h)
  pdf.save(fileName)
}
