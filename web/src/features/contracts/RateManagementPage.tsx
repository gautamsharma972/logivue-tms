import { DownloadOutlined, UploadOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { Alert, Button, Checkbox, Descriptions, Drawer, Dropdown, Flex, Input, Segmented, Select, Table, Tag, Tooltip } from 'antd'
import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PageHeader } from '@/components/PageHeader'
import { useAuth } from '@/features/auth/AuthContext'
import { freightApi } from '@/lib/api/endpoints'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractStatus, ContractType, ListRatesParams, RateRowDto } from '@/lib/api/types'
import { ContractStatusTag } from './shared'

const band = (a: number | null, b: number | null, unit: string) => (a == null && b == null ? '—' : `${a ?? 0}–${b ?? '∞'} ${unit}`)

/** The whole rate book in one grid: filter, sort, group, select, export and import. A rate's version moves up whenever its terms change in a revision. */
export function RateManagementPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const [filters, setFilters] = useState<ListRatesParams>({ page: 1, pageSize: 50, inForceOnly: true })
  const [group, setGroup] = useState<'none' | 'transporter' | 'contract'>('none')
  const [selected, setSelected] = useState<string[]>([])
  const [open, setOpen] = useState<RateRowDto | null>(null)
  const query = useQuery({ queryKey: queryKeys.freight.rates(filters), queryFn: () => freightApi.rates(filters) })
  const rows = useMemo(() => {
    const items = query.data?.items ?? []
    return group === 'none' ? items : [...items].sort((a, b) => (group === 'transporter' ? a.transporterName.localeCompare(b.transporterName) : a.contractNumber.localeCompare(b.contractNumber)))
  }, [query.data, group])
  const set = (patch: Partial<ListRatesParams>) => setFilters((f) => ({ ...f, ...patch, page: 1 }))

  const columns = [
    { title: 'Contract', fixed: 'left' as const, width: 150, render: (_: unknown, r: RateRowDto) => <Link to={`/contracts/${r.contractId}`} onClick={(e) => e.stopPropagation()}>{r.contractNumber} V{r.contractRevision}</Link>, sorter: true },
    { title: 'Transporter', dataIndex: 'transporterName', width: 170, ellipsis: true },
    { title: 'Rate', width: 190, render: (_: unknown, r: RateRowDto) => <span>{r.code} <Tag>V{r.version}</Tag></span>, sorter: true, dataIndex: 'code' },
    { title: 'Service', dataIndex: 'service', width: 90 },
    { title: 'Lane', dataIndex: 'lane', width: 240, ellipsis: true },
    { title: 'Vehicle', dataIndex: 'vehicleTypeName', width: 130, render: (v: string | null) => v ?? 'Any' },
    { title: 'Weight', width: 130, render: (_: unknown, r: RateRowDto) => band(r.minWeightKg, r.maxWeightKg, 'kg') },
    { title: 'Distance', width: 120, render: (_: unknown, r: RateRowDto) => band(r.minDistanceKm, r.maxDistanceKm, 'km') },
    { title: 'Volume', width: 110, render: (_: unknown, r: RateRowDto) => band(r.minVolumeCbm, r.maxVolumeCbm, 'CBM') },
    { title: 'Rate', width: 190, render: (_: unknown, r: RateRowDto) => <Tooltip title={r.pricingKind}>{r.rateSummary}</Tooltip> },
    { title: 'Min / max', width: 130, render: (_: unknown, r: RateRowDto) => (r.minimumCharge == null && r.maximumCharge == null ? '—' : `${r.minimumCharge ?? '—'} / ${r.maximumCharge ?? '—'}`) },
    { title: 'DPH', dataIndex: 'dphRuleCode', width: 80, render: (v: string | null) => v ?? '—' },
    { title: 'Priority', dataIndex: 'priority', width: 90, sorter: true },
    { title: 'Valid from', dataIndex: 'validFrom', width: 110, sorter: true },
    { title: 'Valid to', dataIndex: 'validTo', width: 110 },
    { title: 'Status', width: 150, render: (_: unknown, r: RateRowDto) => <Flex gap={4}><ContractStatusTag status={r.contractStatus} />{r.expiring && <Tag color="gold">Expiring</Tag>}{r.inForce && <Tag color="green">In force</Tag>}</Flex> },
  ]

  return (
    <>
      <PageHeader title="Rate management" description="Every rate across every contract. Rates in an approved contract never change: a change is a new version in a revision."
        actions={<>
          <Dropdown menu={{ items: [{ key: 'csv', label: 'CSV' }, { key: 'xlsx', label: 'Excel' }], onClick: ({ key }) => void freightApi.exportRates({ contractId: filters.contractId, transporterId: filters.transporterId, inForceOnly: filters.inForceOnly, format: key as 'csv' | 'xlsx' }) }}><Button icon={<DownloadOutlined />}>Export</Button></Dropdown>
          {can('contracts.manage') && <Button type="primary" icon={<UploadOutlined />} onClick={() => navigate('/contracts/rates/import')}>Import from Excel</Button>}
        </>} />
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search allowClear placeholder="Rate, contract or title" style={{ width: 220 }} onSearch={(search) => set({ search: search || undefined })} />
        <Input.Search allowClear placeholder="Origin" style={{ width: 150 }} onSearch={(origin) => set({ origin: origin || undefined })} />
        <Input.Search allowClear placeholder="Destination" style={{ width: 150 }} onSearch={(destination) => set({ destination: destination || undefined })} />
        <Select<ContractType> allowClear placeholder="Service" style={{ width: 130 }} onChange={(service) => set({ service })} options={[{ value: 'Ftl', label: 'FTL' }, { value: 'Ptl', label: 'PTL' }, { value: 'Dedicated', label: 'Dedicated' }]} />
        <Select<ContractStatus> allowClear placeholder="Contract status" style={{ width: 160 }} onChange={(status) => set({ status })} options={(['Active', 'Draft', 'PendingApproval', 'Suspended', 'Expired', 'Superseded'] as ContractStatus[]).map((v) => ({ value: v, label: v }))} />
        <Select allowClear placeholder="Expiring" style={{ width: 150 }} onChange={(expiringWithinDays: number | undefined) => set({ expiringWithinDays })} options={[{ value: 15, label: 'Within 15 days' }, { value: 30, label: 'Within 30 days' }, { value: 60, label: 'Within 60 days' }, { value: 90, label: 'Within 90 days' }]} />
        <Checkbox checked={!!filters.inForceOnly} onChange={(e) => set({ inForceOnly: e.target.checked || undefined })}>Only rates in force</Checkbox>
        <Segmented value={group} onChange={setGroup} options={[{ value: 'none', label: 'No grouping' }, { value: 'transporter', label: 'By transporter' }, { value: 'contract', label: 'By contract' }]} />
      </Flex>
      {selected.length > 0 && <Alert type="info" showIcon style={{ marginBottom: 8 }} title={`${selected.length} rate(s) selected`} action={<Button size="small" onClick={() => setSelected([])}>Clear</Button>} />}
      <Table<RateRowDto>
        size="small" rowKey="id" loading={query.isLoading} dataSource={rows} columns={columns} scroll={{ x: 2400 }} sticky
        rowSelection={{ selectedRowKeys: selected, onChange: (keys) => setSelected(keys as string[]) }}
        onChange={(_, __, sorter) => { const s = Array.isArray(sorter) ? sorter[0] : sorter; setFilters((f) => ({ ...f, sortBy: s?.order ? String(s.field ?? 'contract') : undefined, descending: s?.order === 'descend' })) }}
        onRow={(r) => ({ onClick: () => setOpen(r), style: { cursor: 'pointer' } })}
        pagination={{ current: filters.page, pageSize: filters.pageSize, total: query.data?.totalCount ?? 0, showSizeChanger: true, pageSizeOptions: [25, 50, 100, 250], onChange: (page, pageSize) => setFilters((f) => ({ ...f, page, pageSize })) }}
      />
      <RateDetailDrawer rate={open} onClose={() => setOpen(null)} />
    </>
  )
}

function RateDetailDrawer({ rate, onClose }: { rate: RateRowDto | null; onClose: () => void }) {
  const history = useQuery({ queryKey: queryKeys.freight.rateHistory(rate?.id ?? ''), queryFn: () => freightApi.rateHistory(rate!.id), enabled: !!rate })
  return (
    <Drawer open={!!rate} onClose={onClose} size={620} title={rate ? `${rate.code} V${rate.version}` : ''}>
      {rate && (
        <Flex vertical gap={16}>
          <Descriptions size="small" bordered column={1}>
            <Descriptions.Item label="Contract"><Link to={`/contracts/${rate.contractId}`}>{rate.contractNumber} V{rate.contractRevision}</Link> · {rate.transporterName}</Descriptions.Item>
            <Descriptions.Item label="Lane">{rate.lane}</Descriptions.Item>
            <Descriptions.Item label="Service / vehicle">{rate.service} · {rate.vehicleTypeName ?? 'any vehicle'}</Descriptions.Item>
            <Descriptions.Item label="Rating basis">{rate.pricingKind}: {rate.rateSummary}</Descriptions.Item>
            <Descriptions.Item label="Slabs">weight {band(rate.minWeightKg, rate.maxWeightKg, 'kg')} · distance {band(rate.minDistanceKm, rate.maxDistanceKm, 'km')} · volume {band(rate.minVolumeCbm, rate.maxVolumeCbm, 'CBM')}</Descriptions.Item>
            <Descriptions.Item label="Minimum / maximum">{rate.minimumCharge ?? 'none'} / {rate.maximumCharge ?? 'none'}</Descriptions.Item>
            <Descriptions.Item label="DPH">{rate.dphRuleCode ?? 'The contract default'}</Descriptions.Item>
            <Descriptions.Item label="Priority">{rate.priority} (lower is preferred)</Descriptions.Item>
            <Descriptions.Item label="Validity">{rate.validFrom} → {rate.validTo}</Descriptions.Item>
          </Descriptions>
          <Table size="small" pagination={false} rowKey="id" loading={history.isLoading} dataSource={history.data ?? []} title={() => 'Versions of this rate'} columns={[
            { title: 'Contract', render: (_, r: RateRowDto) => <Link to={`/contracts/${r.contractId}`}>V{r.contractRevision}</Link> }, { title: 'Rate version', dataIndex: 'version' }, { title: 'Rate', dataIndex: 'rateSummary' },
            { title: 'Valid', render: (_, r: RateRowDto) => `${r.validFrom} → ${r.validTo}` }, { title: 'Status', render: (_, r: RateRowDto) => <ContractStatusTag status={r.contractStatus} /> },
          ]} />
        </Flex>
      )}
    </Drawer>
  )
}
