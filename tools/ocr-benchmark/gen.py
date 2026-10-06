import json, random, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance
random.seed(7)
S='/System/Library/Fonts/Supplemental/'
def F(path, size, idx=0):
    return ImageFont.truetype(path, size, index=idx)
ARIAL=S+'Arial.ttf'; TIMES=S+'Times New Roman.ttf'; COURIER=S+'Courier New.ttf'; HAND=S+'Bradley Hand Bold.ttf'; DEVA='/System/Library/Fonts/Supplemental/Devanagari Sangam MN.ttc'
NAMES=["shipmentNumber","deliveryNumber","invoiceNumber","customer","transporter","vehicleNumber","deliveryDate","recipientName","deliveredQuantity","shortQuantity","damageRemarks"]
truth={}
def save(name, img, t, fmt='jpg', q=90):
    truth[name]=t
    if fmt=='pdf': img.convert('RGB').save(name+'.pdf', 'PDF', quality=q, resolution=150)
    else: img.convert('RGB').save(name+'.jpg', quality=q)
def paper(w=1000,h=1300,bg=(255,255,255)): return Image.new('RGB',(w,h),bg)
def T(**kw):
    d={n:None for n in NAMES}; d.update(kw); return d

# 1 clean typed table
img=paper(); d=ImageDraw.Draw(img); b=F(ARIAL,44); f=F(ARIAL,32)
d.text((60,50),"SHREE ROADLINES - DELIVERY CHALLAN",font=b,fill='black')
rows=[("Shipment No:","SH10025"),("Delivery No:","DLV-10025"),("Invoice No:","INV9001"),("Customer:","ABC Distributors"),("Transporter:","Shree Roadlines"),("Vehicle No:","MH12AB1234"),("Delivery Date:","05/10/2026"),("Goods dispatched:","100 cartons"),("Goods received:","95 cartons"),("Short:","3 cartons"),("Damage remarks:","2 cartons crushed"),("Received by:","Ramesh Patel")]
y=170
for k,v in rows: d.text((60,y),k,font=f,fill='black'); d.text((470,y),v,font=f,fill='black'); y+=85
save('s01_clean_table',img,T(shipmentNumber="SH10025",deliveryNumber="DLV-10025",invoiceNumber="INV9001",customer="ABC Distributors",transporter="Shree Roadlines",vehicleNumber="MH12AB1234",deliveryDate="05/10/2026",recipientName="Ramesh Patel",deliveredQuantity="95",shortQuantity="3",damageRemarks="2 cartons crushed"))

# 2 free-form paragraph style letter
img=paper(); d=ImageDraw.Draw(img); f=F(TIMES,34); b=F(TIMES,46)
d.text((60,60),"Bharat Cargo Carriers Pvt Ltd",font=b,fill='black')
d.text((60,120),"Proof of Delivery / Goods Receipt Note",font=F(TIMES,34),fill='black')
lines=["Date: 12-Oct-2026","","Received in good order the consignment against","Invoice INV-77412 from Bharat Cargo Carriers Pvt Ltd,","LR/Shipment SH-D20231, loaded on vehicle KA01MN4567.","","Consignee: Nashik Distribution","Quantity received: 240 bags. Short: nil.","No damage noted.","","Received by: Sunita Shah (Store Incharge)","Delivery ref: DLV-20231"]
y=220
for l in lines: d.text((60,y),l,font=f,fill='black'); y+=62
save('s02_free_paragraph',img,T(shipmentNumber="SHD20231",deliveryNumber="DLV-20231",invoiceNumber="INV-77412",customer="Nashik Distribution",transporter="Bharat Cargo Carriers Pvt Ltd",vehicleNumber="KA01MN4567",deliveryDate="12-Oct-2026",recipientName="Sunita Shah",deliveredQuantity="240",shortQuantity="0"))

# helper: scanner distortions
def scan(img, angle=2.5, noise=18, blur=0.6, q=70, shadow=True):
    img=img.rotate(angle, expand=True, fillcolor=(235,235,235), resample=Image.BICUBIC)
    px=img.load(); w,h=img.size
    n=Image.effect_noise((w,h),noise).convert('RGB')
    img=Image.blend(img,n,0.12)
    if shadow:
        g=Image.new('L',(w,h)); gd=ImageDraw.Draw(g)
        for x in range(w): gd.line((x,0,x,h),fill=int(90*x/w))
        img=Image.composite(Image.new('RGB',(w,h),(60,60,60)),img,g.point(lambda v:v//3))
    return img.filter(ImageFilter.GaussianBlur(blur))
base=Image.open('s01_clean_table.jpg')
# 3 skewed noisy scan
save('s03_skewed_noisy_scan',scan(base),truth['s01_clean_table'],q=65)
# 4 low-res blurry (fax-like)
lo=base.resize((520,676)).filter(ImageFilter.GaussianBlur(1.1)).convert('L').point(lambda v:0 if v<150 else 255)
save('s04_low_res_fax',lo,truth['s01_clean_table'],q=50)
# 5 handwritten fields on printed form
img=paper(); d=ImageDraw.Draw(img); f=F(ARIAL,30); h=F(HAND,44)
d.text((60,40),"DELIVERY RECEIPT (to be filled by receiver)",font=F(ARIAL,38),fill='black')
fields=[("Shipment No.","SH10031"),("Invoice No.","INV-5512"),("Vehicle No.","GJ05CD7788"),("Customer","Surat Retail Hub"),("Date","06/10/2026"),("Qty received","48"),("Qty short","2"),("Remarks","1 carton wet"),("Receiver name","Anil Kumar")]
y=150
for k,v in fields:
    d.text((60,y+8),k,font=f,fill='black'); d.line((400,y+55,940,y+55),fill='black',width=2); d.text((420,y-4),v,font=h,fill=(20,30,120)); y+=110
img=img.rotate(-1.2,fillcolor='white',resample=Image.BICUBIC)
save('s05_handwritten_form',img,T(shipmentNumber="SH10031",invoiceNumber="INV-5512",vehicleNumber="GJ05CD7788",customer="Surat Retail Hub",deliveryDate="06/10/2026",deliveredQuantity="48",shortQuantity="2",damageRemarks="1 carton wet",recipientName="Anil Kumar"))
# 6 phone photo: perspective, shadow, jpeg
def perspective(img, shift=90):
    w,h=img.size
    coeffs=[1,0.0,0, 0.00002,1,0, 0.00009,0.00002]
    return img.transform((w+shift,h),Image.PERSPECTIVE,coeffs,Image.BICUBIC,fillcolor=(70,60,50))
ph=perspective(scan(base,angle=-3,noise=26,blur=0.9,shadow=True))
ph=ImageEnhance.Brightness(ph).enhance(0.85)
save('s06_phone_photo',ph,truth['s01_clean_table'],q=45)
# 7 scanned PDF (embedded JPEG)
save('s07_scanned_pdf',scan(base,angle=1.0,noise=14,blur=0.5),truth['s01_clean_table'],fmt='pdf',q=70)
# 8 bilingual Hindi/English
img=paper(); d=ImageDraw.Draw(img); f=F(ARIAL,32); hi=F(DEVA,34)
d.text((60,50),"DELIVERY CHALLAN",font=F(ARIAL,44),fill='black'); d.text((560,60),"डिलीवरी चालान",font=F(DEVA,44),fill='black')
rows=[("Shipment No / शिपमेंट","SH10077"),("Invoice No / चालान नं.","INV-3090"),("Vehicle / वाहन","RJ14GA2020"),("Customer / ग्राहक","Jaipur Mart"),("Date / तारीख","08/10/2026"),("Received / प्राप्त","300"),("Short / कम","0"),("Receiver / प्राप्तकर्ता","Mohan Lal")]
y=170
for k,v in rows: d.text((60,y),k,font=hi,fill='black'); d.text((620,y),v,font=f,fill='black'); y+=95
save('s08_bilingual',img,T(shipmentNumber="SH10077",invoiceNumber="INV-3090",vehicleNumber="RJ14GA2020",customer="Jaipur Mart",deliveryDate="08/10/2026",deliveredQuantity="300",shortQuantity="0",recipientName="Mohan Lal"))
# 9 sparse: most fields missing (only shipment + qty) -> must come back null, not invented
img=paper(); d=ImageDraw.Draw(img); f=F(ARIAL,36)
d.text((60,60),"GOODS RECEIVED",font=F(ARIAL,52),fill='black')
d.text((60,260),"Shipment: SH10090",font=f,fill='black'); d.text((60,340),"Received: 75 pcs",font=f,fill='black'); d.text((60,900),"Signed: ________________",font=f,fill='black')
save('s09_sparse_fields_missing',img,T(shipmentNumber="SH10090",deliveredQuantity="75"))
# 10 dense table with similar numbers (confusable digits) and stamp over text
img=paper(); d=ImageDraw.Draw(img); f=F(COURIER,30); b=F(COURIER,40)
d.text((50,40),"NATIONAL FREIGHT LINES - POD",font=b,fill='black')
rows=[("SHIPMENT NO","SH10018"),("DELIVERY NO","DLV-10018"),("INVOICE NO","INV-0018B"),("CUSTOMER","Hyderabad DC"),("TRANSPORTER","National Freight Lines"),("VEHICLE NO","TS09EF1108"),("DATE","09/10/2026"),("RECEIVED QTY","118"),("SHORT QTY","12"),("REMARKS","seal broken, 12 cartons missing"),("RECEIVER","S. Venkat Rao")]
y=150
for k,v in rows: d.text((50,y),k,font=f,fill='black'); d.text((420,y),v,font=f,fill='black'); y+=80
st=Image.new('RGBA',(420,420),(0,0,0,0)); sd=ImageDraw.Draw(st); sd.ellipse((10,10,410,410),outline=(190,30,30,200),width=10); sd.text((60,170),"RECEIVED",font=F(ARIAL,64),fill=(190,30,30,200))
st=st.rotate(25,expand=True); img.paste(st,(480,330),st)
save('s10_stamp_over_text',img,T(shipmentNumber="SH10018",deliveryNumber="DLV-10018",invoiceNumber="INV-0018B",customer="Hyderabad DC",transporter="National Freight Lines",vehicleNumber="TS09EF1108",deliveryDate="09/10/2026",deliveredQuantity="118",shortQuantity="12",damageRemarks="seal broken, 12 cartons missing",recipientName="S. Venkat Rao"))
# 11 handwritten everything (no print) on lined paper
img=paper(bg=(250,247,235)); d=ImageDraw.Draw(img); h=F(HAND,50)
for y in range(120,1250,70): d.line((40,y,960,y),fill=(170,190,220),width=2)
lines=["Challan - 10/10/26","Shipment SH10102   Inv 8841","Veh MH43AK0099","Customer: Pune Traders","Delivered 60 boxes","Short 4 boxes","Recd by  R. Deshmukh"]
y=70
for l in lines: d.text((70,y),l,font=h,fill=(25,25,110)); y+=140
save('s11_all_handwritten',img.rotate(1.5,fillcolor=(250,247,235),resample=Image.BICUBIC),T(shipmentNumber="SH10102",invoiceNumber="8841",vehicleNumber="MH43AK0099",customer="Pune Traders",deliveryDate="10/10/26",deliveredQuantity="60",shortQuantity="4",recipientName="R. Deshmukh"))
json.dump(truth,open('truth.json','w'),indent=1)
print(len(truth),'samples')
