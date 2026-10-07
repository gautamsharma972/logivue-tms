import { InboxOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Checkbox, Flex, Form, Input, Modal, Radio, Table, Tag, Typography, Upload } from 'antd'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { freightApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ImportBatchDto, ImportRowDto } from '@/lib/api/types'

const COLUMNS = ['Contract Number', 'Service Type', 'Origin', 'Destination', 'Vehicle Type', 'Weight From', 'Weight To', 'Distance From', 'Distance To', 'Volume From', 'Volume To', 'Rate', 'Rate Type', 'Minimum Charge', 'Maximum Charge', 'DPH Rule', 'Priority', 'Effective From', 'Effective To']
const key = (header: string) => header.replace(/[^a-z0-9]/gi, '').toLowerCase()
const statusColour = { Valid: 'green', Warning: 'gold', Error: 'red' } as const

/** Download the template, upload a filled sheet, see every row checked, fix what is wrong, then put the good rows into a draft contract for approval. */
export function RateImportPage() {
  const { message } = App.useApp()
  const client = useQueryClient()
  const [mode, setMode] = useState<'Append' | 'Replace'>('Append')
  const [batchId, setBatchId] = useState<string | null>(null)
  const [skip, setSkip] = useState(false)
  const [editing, setEditing] = useState<ImportRowDto | null>(null)
  const batch = useQuery({ queryKey: queryKeys.freight.import(batchId ?? ''), queryFn: () => freightApi.import(batchId!), enabled: !!batchId })
  const recent = useQuery({ queryKey: queryKeys.freight.imports, queryFn: freightApi.imports })
  const upload = useMutation({
    mutationFn: (file: File) => freightApi.upload(file, mode),
    onSuccess: (b) => { setBatchId(b.id); client.setQueryData(queryKeys.freight.import(b.id), b); void client.invalidateQueries({ queryKey: queryKeys.freight.imports }) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const correct = useMutation({
    mutationFn: (v: { row: number; values: Record<string, string | null> }) => freightApi.correctRow(batchId!, v.row, v.values),
    onSuccess: (b) => { client.setQueryData(queryKeys.freight.import(b.id), b); setEditing(null) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const apply = useMutation({
    mutationFn: () => freightApi.applyImport(batchId!, { skipInvalidRows: skip }),
    onSuccess: (r) => { client.setQueryData(queryKeys.freight.import(r.batch.id), r.batch); void client.invalidateQueries({ queryKey: queryKeys.freight.all }); void message.success(`${r.imported} rate(s) are in a draft contract. It still needs approval.`) },
    onError: (e) => void message.error(toApiError(e).message),
  })
  const b: ImportBatchDto | undefined = batch.data

  return (
    <>
      <PageHeader title="Import rates from Excel" description="Importing never activates anything. The rates go into a draft contract (a new version when the contract is already approved) that is approved like any other change."
        actions={<><Button onClick={() => void freightApi.template('xlsx')}>Download the template</Button><Button onClick={() => void freightApi.template('csv')}>CSV template</Button></>} />
      <Flex gap={16} wrap align="start">
        <Card style={{ width: 380 }} title="1. Upload">
          <Radio.Group value={mode} onChange={(e) => setMode(e.target.value as 'Append' | 'Replace')} style={{ marginBottom: 12 }}>
            <Radio value="Append">Add to the contract's rates</Radio>
            <Radio value="Replace">Replace the contract's rates</Radio>
          </Radio.Group>
          <Upload.Dragger accept=".xlsx,.csv" maxCount={1} showUploadList={false} beforeUpload={(file) => { upload.mutate(file); return false }} disabled={upload.isPending}>
            <p className="ant-upload-drag-icon"><InboxOutlined /></p>
            <p>Drop the sheet here, or click to choose it</p>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>One sheet is for one contract. Up to 5,000 rows.</Typography.Text>
          </Upload.Dragger>
          <Typography.Paragraph type="secondary" style={{ marginTop: 12, fontSize: 12 }}>Columns: {COLUMNS.join(', ')}. Origin and Destination are written as "City, State", a state, or Any; or give a zone code in the Zone column. Rate Type is FIXED, PER_KM, PER_KG, PER_TON, PER_CBM, PER_BOX or MONTHLY.</Typography.Paragraph>
        </Card>
        <div style={{ flex: 1, minWidth: 420 }}>
          {!b && <Alert type="info" showIcon title="Upload a sheet to see it checked row by row." />}
          {b && (
            <Card title={`2. Preview · ${b.reference} · ${b.fileName}`} extra={<Tag color={b.status === 'Applied' ? 'green' : b.status === 'Discarded' ? 'default' : 'blue'}>{b.status}</Tag>}>
              <Flex gap={8} wrap style={{ marginBottom: 12 }}>
                <Tag>{b.rowCount} rows</Tag><Tag color="green">{b.rowCount - b.errorRows - b.warningRows} valid</Tag><Tag color="gold">{b.warningRows} with warnings</Tag><Tag color="red">{b.errorRows} with errors</Tag>
                {b.contractNumber ? <Tag color="blue">Contract {b.contractNumber}</Tag> : <Tag color="red">No contract found</Tag>}
              </Flex>
              <Table<ImportRowDto> size="small" rowKey="rowNumber" dataSource={b.rows ?? []} pagination={{ pageSize: 15, hideOnSinglePage: true }} columns={[
                { title: 'Row', dataIndex: 'rowNumber', width: 60 },
                { title: 'Status', dataIndex: 'status', width: 90, render: (s: ImportRowDto['status']) => <Tag color={statusColour[s]}>{s}</Tag> },
                { title: 'Lane', render: (_, r) => `${r.values[key('Origin')] ?? r.values[key('Origin Zone')] ?? '?'} → ${r.values[key('Destination')] ?? r.values[key('Destination Zone')] ?? '?'}` },
                { title: 'Rate', render: (_, r) => `${r.values[key('Rate')] ?? '—'} ${r.values[key('Rate Type')] ?? ''}` },
                { title: 'Problems', render: (_, r) => r.issues.map((i) => <div key={i.code + i.message} style={{ color: i.severity === 'Error' ? '#cf1322' : '#d48806' }}>{i.field}: {i.message}</div>) },
                { title: '', width: 80, render: (_, r) => b.status === 'Previewed' && <Button size="small" onClick={() => setEditing(r)}>Fix</Button> },
              ]} />
              {b.status === 'Previewed' && (
                <Flex vertical gap={8} style={{ marginTop: 12 }}>
                  {b.errorRows > 0 && <Checkbox checked={skip} onChange={(e) => setSkip(e.target.checked)}>Skip the {b.errorRows} row(s) with errors and import the rest</Checkbox>}
                  <Flex gap={8}>
                    <Button type="primary" disabled={!b.contractId || (b.errorRows > 0 && !skip)} loading={apply.isPending} onClick={() => apply.mutate()}>3. Put the valid rows into a draft</Button>
                    <Button danger onClick={() => void freightApi.discardImport(b.id).then(() => { setBatchId(null); void client.invalidateQueries({ queryKey: queryKeys.freight.imports }) })}>Discard</Button>
                  </Flex>
                </Flex>
              )}
              {b.status === 'Applied' && b.appliedContractId && <Alert type="success" showIcon style={{ marginTop: 12 }} title="Imported into a draft" description={<Link to={`/contracts/${b.appliedContractId}`}>Open the draft contract to submit it for approval</Link>} />}
            </Card>
          )}
          {recent.data && recent.data.length > 0 && !b && (
            <Card size="small" title="Recent imports" style={{ marginTop: 16 }}>
              <Table size="small" pagination={false} rowKey="id" dataSource={recent.data.slice(0, 8)} columns={[{ title: 'Import', dataIndex: 'reference' }, { title: 'File', dataIndex: 'fileName' }, { title: 'Contract', dataIndex: 'contractNumber' }, { title: 'Rows', dataIndex: 'rowCount' }, { title: 'Status', dataIndex: 'status' }, { title: '', render: (_, r) => <Button size="small" type="link" onClick={() => setBatchId(r.id)}>Open</Button> }]} />
            </Card>
          )}
        </div>
      </Flex>
      <Modal open={!!editing} title={editing ? `Fix row ${editing.rowNumber}` : ''} footer={null} onCancel={() => setEditing(null)} destroyOnHidden width={620}>
        {editing && (
          <Form layout="vertical" initialValues={editing.values} onFinish={(values: Record<string, string | null>) => correct.mutate({ row: editing.rowNumber, values })}>
            <Flex wrap gap={12}>
              {COLUMNS.map((c) => <Form.Item key={c} name={key(c)} label={c} style={{ width: 180 }}><Input /></Form.Item>)}
            </Flex>
            <Button type="primary" htmlType="submit" loading={correct.isPending}>Save and check again</Button>
          </Form>
        )}
      </Modal>
    </>
  )
}
