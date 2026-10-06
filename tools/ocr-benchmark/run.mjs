// OCR benchmark: creates one delivery per synthetic challan, uploads the document as the paper POD, waits for the reader and scores every field against the known answer.
//   python3 tools/ocr-benchmark/gen.py        (in this folder: writes the sample images, a PDF and truth.json)
//   node tools/ocr-benchmark/run.mjs [report.json]     (ONLY=s02,s05 runs a subset; the API must run with Deliveries:Ocr:Enabled)
// Development tenants only: it adds deliveries. Needs Pillow for the generator.
import { readFileSync, writeFileSync } from 'node:fs'
const API=(process.env.TMS_API??'http://localhost:5080')+'/api/v1'
const TENANT=process.env.TMS_TENANT??'DEMO', EMAIL=process.env.TMS_EMAIL??'admin@demo.tms', PASSWORD=process.env.TMS_PASSWORD??'Admin@12345678'
const truth=JSON.parse(readFileSync('truth.json','utf8'))
const login=await (await fetch(API+'/auth/login',{method:'POST',headers:{'content-type':'application/json','x-tms-client':'ocr'},body:JSON.stringify({tenantCode:TENANT,email:EMAIL,password:PASSWORD})})).json()
let H={authorization:'Bearer '+login.accessToken,'x-tms-client':'ocr'}
let J={...H,'content-type':'application/json'}
const refresh=async()=>{const l=await (await fetch(API+'/auth/login',{method:'POST',headers:{'content-type':'application/json','x-tms-client':'ocr'},body:JSON.stringify({tenantCode:TENANT,email:EMAIL,password:PASSWORD})})).json();H={authorization:'Bearer '+l.accessToken,'x-tms-client':'ocr'};J={...H,'content-type':'application/json'}}
const call=async(m,u,b)=>{const r=await fetch(API+u,{method:m,headers:J,body:b===undefined?undefined:JSON.stringify(b)});const t=await r.text();if(!r.ok)throw new Error(m+' '+u+' '+r.status+' '+t.slice(0,200));return t?JSON.parse(t):null}
const carrier=(await call('GET','/transporters?status=Active&pageSize=5')).items[0]
const here={fix:{latitude:18.5204,longitude:73.8567,accuracyM:12},deviceReference:'ocr-test',at:null}
const KEYS={shipmentNumber:'Shipment Number',deliveryNumber:'Delivery Number',invoiceNumber:'Invoice Number',customer:'Customer',transporter:'Transporter',vehicleNumber:'Vehicle Number',deliveryDate:'Delivery Date',recipientName:'Recipient Name',deliveredQuantity:'Delivered Quantity',shortQuantity:'Short Quantity',damageRemarks:'Damage Remarks'}
const squash=s=>String(s??'').toLowerCase().replace(/[^a-z0-9]/g,'')
const stamp=Date.now()%100000
async function oneSample(file,t,deliveredOverride){
  await refresh()
  const delivered=Number(deliveredOverride??t.deliveredQuantity??50)
  const d=await call('POST','/deliveries',{shipmentReference:t.shipmentNumber??('SH-X'+stamp),orderReference:t.invoiceNumber??('INV-'+stamp),lrNumber:'LR-OCR',sequence:1,transporterId:carrier.id,transporterReference:carrier.legalName,vehicleReference:t.vehicleNumber??'MH12ZZ0001',driverName:'Tester',customerReference:'C1',customerName:t.customer??'Some Customer',customerPhone:'9876543210',customerEmail:'c@example.test',originReference:'Pune',destinationReference:'Surat',destinationAddress:'Plot 1',plannedDeliveryAt:new Date().toISOString(),windowEnd:new Date(Date.now()+8*3600e3).toISOString(),items:[{sku:'SKU-1',description:'Goods',orderedQuantity:delivered,dispatchedQuantity:delivered,unitOfMeasure:'PKG'}]})
  await call('POST',`/deliveries/${d.summary.id}/start`,here); const a=await call('POST',`/deliveries/${d.summary.id}/arrive`,here)
  const done=await call('POST',`/deliveries/${d.summary.id}/complete`,{outcome:'Full',items:a.items.map(i=>({itemId:i.id,deliveredQuantity:delivered,shortQuantity:0,damagedQuantity:0,rejectedQuantity:0})),remainingDisposition:null,driverRemarks:'ok',proof:{method:'Photo',recipientName:t.recipientName??'Receiver',recipientDesignation:'Mgr',recipientPhone:'9876543210',recipientRemarks:null,driverConfirmed:false,customerAcknowledged:false},context:here})
  const podId=done.summary.podId
  const form=new FormData(); form.append('type','PodDocument')
  const mime=file.endsWith('.pdf')?'application/pdf':'image/jpeg'
  form.append('file',new Blob([readFileSync(file)],{type:mime}),file)
  const up=await fetch(API+`/pods/${podId}/evidence`,{method:'POST',headers:H,body:form}); if(!up.ok) throw new Error('upload '+up.status+' '+(await up.text()).slice(0,150))
  await call('POST',`/pods/${podId}/ocr`,{})
  const t0=Date.now()
  for(;;){
    await new Promise(r=>setTimeout(r,4000)); if(Date.now()-t0>240000) await refresh()
    const r=await call('GET',`/pods/${podId}/ocr`); const o=Array.isArray(r)?r[0]:r
    if(o&&['Completed','Failed'].includes(o.status)) return {o,secs:Math.round((Date.now()-t0)/1000)}
    if(Date.now()-t0>900000) return {o:{status:'Timeout',fields:[]},secs:900}
  }
}
const only=(process.env.ONLY??'').split(',').filter(Boolean)
const jobs=[...Object.keys(truth).map(n=>[n,truth[n],null]), ['s01_clean_table',truth.s01_clean_table,80]].filter(j=>only.length===0||only.some(o=>j[0].startsWith(o)))
const files={s07_scanned_pdf:'s07_scanned_pdf.pdf'}
const report=[]
for(const [name,t,override] of jobs){
  const file=files[name]??name+'.jpg'
  let res; try{res=await oneSample(file,t,override)}catch(e){res={o:{status:'Error:'+e.message,fields:[]},secs:0}}
  const got=Object.fromEntries((res.o.fields??[]).map(f=>[f.name,f]))
  const rows=[]
  for(const [k,label] of Object.entries(KEYS)){
    const want=t[k]; const f=got[label]
    const val=f?(f.normalizedValue??f.rawValue):null
    let verdict
    if(want==null) verdict=f?'INVENTED':'ok-null'
    else if(!f) verdict='MISSED'
    else if(['deliveredQuantity','shortQuantity'].includes(k)?Number(val)===Number(want):squash(val)===squash(want)||squash(val).includes(squash(want))||squash(want).includes(squash(val))) verdict='correct'
    else verdict='WRONG'
    rows.push({k,want,got:val,conf:f?.confidence??null,status:f?.status??null,verdict})
  }
  report.push({name:name+(override?`(system qty ${override})`:''),status:res.o.status,secs:res.secs,provider:res.o.provider,error:res.o.error,rows})
  console.log(name,res.o.status,res.secs+'s',rows.filter(r=>r.verdict==='correct').length+'/'+rows.filter(r=>r.want!=null).length,'wrong:',rows.filter(r=>r.verdict==='WRONG').map(r=>`${r.k}=${r.got}(${r.conf})`).join(';')||'-','invented:',rows.filter(r=>r.verdict==='INVENTED').map(r=>`${r.k}=${r.got}(${r.conf})`).join(';')||'-')
  writeFileSync(process.argv[2]??'report.json',JSON.stringify(report,null,1))
}
