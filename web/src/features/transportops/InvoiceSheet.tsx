import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Space, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { toApiError } from '@/lib/api/errors'
import { opsApi, type BrandingKind } from '@/lib/api/opsApi'
import { formatInrExact } from '@/lib/format'
import { downloadSheetPdf } from './sheetPdf'

const dmy = (v: string) => v.split('-').reverse().join('-')
const money = (v: number) => formatInrExact(v).replace('₹', '').trim()

/** A4 portrait, laid out like the supplier's own invoice: the browser's header and footer are kept out by a zero page margin, and the paper by the page rule. */
const css = `
.inv-sheet { width: 210mm; margin: 0 auto; padding: 10mm; background: #fff; color: #000; font-family: Calibri, Carlito, Arial, sans-serif; font-size: 12px; line-height: 1.3; box-sizing: border-box }
.inv-sheet .box { border: 2px solid #000 }
.inv-sheet .band { background: #1f3864; color: #fff; text-align: center; font-weight: 700; font-size: 20px; padding: 7px 0; border-bottom: 2px solid #000 }
.inv-sheet .logo { text-align: center; padding: 8px 0 2px }
.inv-sheet .logo img { max-height: 76px; max-width: 70% }
.inv-sheet .logo .name { font-size: 24px; font-weight: 700 }
.inv-sheet .addr { border: 1px solid #c8c8c8; margin: 4px 14px 10px; padding: 5px 8px; text-align: center; font-weight: 700; font-size: 11.5px }
.inv-sheet table { border-collapse: collapse; width: 100%; table-layout: fixed }
.inv-sheet td { border: 1px solid #000; padding: 3px 6px; vertical-align: top }
.inv-sheet td.nb { border: 0 }
.inv-sheet td.vl { border-top: 0; border-bottom: 0 }
.inv-sheet td.blue { background: #ddebf7 }
.inv-sheet .b { font-weight: 700 }
.inv-sheet .r { text-align: right }
.inv-sheet .c { text-align: center }
.inv-sheet .sig { border: 2px solid #000; margin-top: 6px; padding: 4px 6px 2px; font-style: italic; font-weight: 700 }
.inv-sheet .sig img { display: block; height: 62px; margin: 6px 0 4px 18px }
@media print {
  .no-print, .app-footer, .ant-layout-sider, .ant-layout-header, .ant-float-btn-group { display: none !important }
  body, .ant-layout { background: #fff !important }
  .ant-layout-content { padding: 0 !important; max-width: none !important }
  .inv-sheet { width: auto }
}
@page { size: A4 portrait; margin: 0 }`

function useBrandingUrl(kind: BrandingKind, enabled: boolean) {
  const q = useQuery({ queryKey: ['ops', 'branding', kind], queryFn: () => opsApi.brandingImage(kind), enabled, retry: false, staleTime: Infinity })
  const [url, setUrl] = useState<string>()
  useEffect(() => {
    if (!q.data) { setUrl(undefined); return undefined }
    const u = URL.createObjectURL(q.data)
    setUrl(u)
    return () => URL.revokeObjectURL(u)
  }, [q.data])
  return url
}

export function InvoicePrintPage() {
  const { id = '' } = useParams()
  const nav = useNavigate()
  const q = useQuery({ queryKey: ['ops', 'invoice-print', id], queryFn: () => opsApi.invoicePrint(id), retry: false })
  const p = q.data
  const logo = useBrandingUrl('logo', !!p?.hasLogo)
  const signature = useBrandingUrl('signature', !!p?.hasSignature)
  const sheet = useRef<HTMLDivElement>(null)
  const [saving, setSaving] = useState(false)
  if (q.isError) return <Alert type="error" showIcon style={{ margin: 24 }} title={toApiError(q.error).message} action={<Button onClick={() => nav(-1)}>Back</Button>} />
  if (!p) return null
  const waiting = (p.hasLogo && !logo) || (p.hasSignature && !signature)
  const forward = p.taxMode === 'Forward'
  return (
    <div className="inv-sheet" ref={sheet}>
      <style>{css}</style>
      <Space className="no-print" style={{ marginBottom: 12 }}>
        <Button onClick={() => nav(-1)}>Back</Button>
        <Button type="primary" loading={saving} disabled={waiting} onClick={() => { setSaving(true); void downloadSheetPdf(sheet.current!, `${p.invoiceNumber.replaceAll('/', '-')}.pdf`, { format: 'a4', orientation: 'portrait' }).finally(() => setSaving(false)) }}>Download PDF</Button>
        <Button onClick={() => window.print()}>Print</Button>
        <Typography.Text type="secondary">A4 portrait</Typography.Text>
      </Space>
      {!p.supplierName && <Alert className="no-print" type="warning" showIcon style={{ marginBottom: 12 }} title="Your organisation's name, address and GSTIN are not set. Add them under Operations settings → Invoices." />}
      <div className="box">
        <div className="band">INVOICE</div>
        <div className="logo">{logo ? <img src={logo} alt="" /> : <span className="name">{p.supplierName}</span>}</div>
        <div className="addr">
          {p.supplierAddress && <div>{p.supplierAddress}</div>}
          {p.supplierGstin && <div>GSTIN: {p.supplierGstin}</div>}
          {p.supplierState && <div>State : {p.supplierState}</div>}
        </div>

        <table>
          <colgroup><col style={{ width: '52.4%' }} /><col style={{ width: '11%' }} /><col style={{ width: '8.4%' }} /><col style={{ width: '10.3%' }} /><col style={{ width: '17.9%' }} /></colgroup>
          <tbody>
            <tr>
              <td className="blue" rowSpan={2} style={{ height: 112 }}>
                <div className="b">Bill To:</div>
                <div className="b" style={{ fontSize: 14 }}>{p.customerName}</div>
                {p.customerAddress && <div style={{ fontSize: 13 }}>{p.customerAddress}</div>}
                <div style={{ fontSize: 13, display: 'flex', justifyContent: 'space-between' }}>
                  <span>{p.customerGstin ? `GSTIN: ${p.customerGstin}` : ''}</span><span>{p.customerState ? `State:${p.customerState}` : ''}</span>
                </div>
              </td>
              <td className="blue b" style={{ fontSize: 12.5, whiteSpace: 'nowrap' }}>Invoice No</td>
              <td className="blue b c" colSpan={2} style={{ fontSize: 13, whiteSpace: 'nowrap' }}>{p.invoiceNumber}</td>
              <td className="blue b" style={{ fontSize: 13, whiteSpace: 'nowrap' }}>Date: {dmy(p.invoiceDate)}</td>
            </tr>
            <tr>
              <td className="blue" style={{ fontSize: 12.5 }}>Reference:</td>
              <td className="blue" colSpan={3} style={{ fontSize: 12.5 }}>{p.reference}</td>
            </tr>
            <tr>
              <td className="b" style={{ height: 26 }}>Description</td>
              <td className="b c" style={{ fontSize: 10.5 }}>HSN/SAC Code</td>
              <td />
              <td />
              <td className="b c" style={{ fontSize: 14 }}>Amount</td>
            </tr>
            <tr>
              <td className="vl" style={{ height: 150, fontSize: 13 }}>
                <div style={{ marginTop: 8 }}>{p.description}</div>
                <div style={{ marginTop: 6, overflowWrap: 'anywhere' }}>LR No.: {p.lrNumbers.join(', ')}</div>
              </td>
              <td className="vl c" style={{ fontSize: 13, paddingTop: 8, overflowWrap: 'anywhere' }}>{p.hsnSac ?? ''}</td>
              <td className="vl" />
              <td className="vl" />
              <td className="b r" style={{ fontSize: 13, paddingTop: 8, borderTop: 0 }}>{money(p.taxable)}</td>
            </tr>
            <tr>
              <td className="vl b r" style={{ fontSize: 13, height: 24 }}>Total</td>
              <td className="vl" /><td className="vl" /><td className="vl" />
              <td className="b r" style={{ fontSize: 13 }}>{money(p.taxable)}</td>
            </tr>
            <tr>
              <td className="vl" style={{ height: 62 }} /><td className="vl" /><td className="vl" /><td className="vl" />
              <td className="vl" />
            </tr>
            <tr>
              <td className="vl" style={{ height: 32 }} /><td className="vl" />
              <td className="b r" colSpan={2} style={{ fontSize: 12.5, whiteSpace: 'nowrap' }}>Total Invoice Value</td>
              <td className="b" style={{ fontSize: 14 }}><div style={{ display: 'flex', justifyContent: 'space-between' }}><span>₹</span><span>{money(p.grandTotal)}</span></div></td>
            </tr>
            <tr><td colSpan={5} style={{ height: 12 }} /></tr>
            <tr><td colSpan={5} style={{ height: 12 }} /></tr>
            <tr>
              <td className="blue" rowSpan={5} style={{ verticalAlign: 'bottom', fontSize: 10.5 }}>
                <div className="b">Declaration</div>
                <div className="b">{p.declaration}</div>
              </td>
              <td className="b" colSpan={2}>Total Taxable Value</td><td />
              <td className="b"><div style={{ display: 'flex', justifyContent: 'space-between' }}><span>₹</span><span>{money(p.taxable)}</span></div></td>
            </tr>
            <tr>
              <td colSpan={2} className={forward && p.cgst > 0 ? 'b' : ''}>{forward && p.cgst > 0 ? `CGST ${p.taxRatePercent / 2}%` : ''}</td><td />
              <td className="b">{forward && p.cgst > 0 && <div style={{ display: 'flex', justifyContent: 'space-between' }}><span>₹</span><span>{money(p.cgst)}</span></div>}</td>
            </tr>
            <tr>
              <td colSpan={2} className={forward && (p.sgst > 0 || p.igst > 0) ? 'b' : ''}>{forward ? (p.igst > 0 ? `IGST ${p.taxRatePercent}%` : p.sgst > 0 ? `SGST ${p.taxRatePercent / 2}%` : '') : ''}</td><td />
              <td className="b">{forward && (p.sgst > 0 || p.igst > 0) && <div style={{ display: 'flex', justifyContent: 'space-between' }}><span>₹</span><span>{money(p.igst > 0 ? p.igst : p.sgst)}</span></div>}</td>
            </tr>
            <tr><td colSpan={4} className="b" style={{ height: 44, verticalAlign: 'top', paddingTop: 6, fontWeight: 700 }}>TAX PAYABLE ON RCM Based 5%</td></tr>
            <tr><td colSpan={3} style={{ height: 24 }} /><td /></tr>
            <tr><td colSpan={5} className="b" style={{ fontStyle: 'italic', textDecoration: 'underline', height: 22, fontSize: 11.5 }}>Total Invoice Amount (₹ - In Words) :-</td></tr>
          </tbody>
        </table>
      </div>
      <div className="b" style={{ fontSize: 11.5, margin: '8px 0 6px' }}>{p.amountInWords}</div>
      <div className="sig">
        <div>For : {p.supplierName}</div>
        {signature ? <img src={signature} alt="" /> : <div style={{ height: 70 }} />}
        <div>Authorised Signatory</div>
      </div>
    </div>
  )
}
