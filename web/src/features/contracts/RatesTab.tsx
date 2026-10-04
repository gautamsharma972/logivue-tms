import { DeleteOutlined, EditOutlined, PlusOutlined, SaveOutlined, SwapOutlined, UndoOutlined } from '@ant-design/icons'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Flex, Input, Popconfirm, Table, Tag, Typography, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { contractsApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractDto, PlaceDto, RateInputDto } from '@/lib/api/types'
import { describePricing } from './shared'
import { RateDrawer } from './RateDrawer'

const placeText = (p: PlaceDto) =>
  p.kind === 'Any' ? 'Anywhere' : p.kind === 'State' ? p.state! : p.kind === 'City' ? `${p.city}, ${p.state}` : `Zone ${p.zoneCode}`

interface Row extends RateInputDto {
  key: number
  vehicleTypeName?: string | null
}

export function RatesTab({ contract, editable }: { contract: ContractDto; editable: boolean }) {
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const id = contract.summary.id
  const type = contract.summary.type

  const saved = useQuery({ queryKey: queryKeys.contracts.rates(id), queryFn: () => contractsApi.rates(id) })
  const zones = useQuery({ queryKey: queryKeys.contracts.zones, queryFn: contractsApi.zones })
  const vehicleTypes = useQuery({ queryKey: queryKeys.contracts.vehicleTypes, queryFn: contractsApi.vehicleTypes })

  const [rows, setRows] = useState<Row[]>([])
  const [dirty, setDirty] = useState(false)
  const [search, setSearch] = useState('')
  const [drawer, setDrawer] = useState<{ open: boolean; index: number | null }>({ open: false, index: null })

  useEffect(() => {
    if (saved.data) {
      setRows(saved.data.map((r, i) => ({ ...r, key: i })))
      setDirty(false)
    }
  }, [saved.data])

  const vehicleName = (vehicleTypeId: string | null) => vehicleTypes.data?.find((v) => v.id === vehicleTypeId)?.name ?? null

  const save = useMutation({
    mutationFn: () => contractsApi.saveRates(id, rows.map(({ key: _key, vehicleTypeName: _name, ...rate }) => stripServerFields(rate)), contract.version),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.all })
      void message.success(`${rows.length} rate${rows.length === 1 ? '' : 's'} saved`)
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (error.code === 'concurrency.conflict') await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.all })
      void message.error(error.message)
    },
  })

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return term ? rows.filter((r) => `${placeText(r.origin)} ${placeText(r.destination)} ${vehicleName(r.vehicleTypeId) ?? ''}`.toLowerCase().includes(term)) : rows
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, search, vehicleTypes.data])

  const edit = (updated: RateInputDto) => {
    setRows((current) => {
      const next = [...current]
      if (drawer.index === null) next.push({ ...updated, key: Date.now() })
      else next[drawer.index] = { ...updated, key: current[drawer.index]!.key }
      return next
    })
    setDirty(true)
    setDrawer({ open: false, index: null })
  }

  const columns: TableColumnsType<Row> = [
    {
      title: 'Lane',
      key: 'lane',
      render: (_, r) => (
        <Typography.Text>
          {placeText(r.origin)} {r.bothWays ? <SwapOutlined aria-label="both ways" /> : '→'} {placeText(r.destination)}
        </Typography.Text>
      ),
    },
    ...(type !== 'Ptl' ? [{ title: 'Vehicle', key: 'vehicle', render: (_: unknown, r: Row) => vehicleName(r.vehicleTypeId) ?? '—' }] : []),
    {
      title: 'Distance',
      key: 'band',
      responsive: ['md'],
      render: (_, r) => (r.minDistanceKm === null && r.maxDistanceKm === null ? '—' : `${r.minDistanceKm ?? 0}–${r.maxDistanceKm ?? '∞'} km`),
    },
    { title: 'Price', key: 'price', render: (_, r) => describePricing(r.pricing) },
    ...(editable
      ? [{
          title: '',
          key: 'actions',
          width: 100,
          render: (_: unknown, r: Row) => {
            const index = rows.findIndex((x) => x.key === r.key)
            return (
              <Flex gap={4}>
                <Button type="text" aria-label="Edit rate" icon={<EditOutlined />} onClick={() => setDrawer({ open: true, index })} />
                <Popconfirm title="Remove this rate?" okText="Remove" okButtonProps={{ danger: true }} onConfirm={() => { setRows((c) => c.filter((x) => x.key !== r.key)); setDirty(true) }}>
                  <Button type="text" danger aria-label="Remove rate" icon={<DeleteOutlined />} />
                </Popconfirm>
              </Flex>
            )
          },
        }]
      : []),
  ]

  return (
    <Card
      title={<Flex align="center" gap={8}>Rates <Tag>{rows.length}</Tag>{dirty && <Tag color="gold">Unsaved changes</Tag>}</Flex>}
      extra={
        editable && (
          <Flex gap={8} wrap>
            <Button icon={<PlusOutlined />} onClick={() => setDrawer({ open: true, index: null })}>Add rate</Button>
            <Button icon={<UndoOutlined />} disabled={!dirty} onClick={() => saved.data && (setRows(saved.data.map((r, i) => ({ ...r, key: i }))), setDirty(false))}>Discard</Button>
            <Button type="primary" icon={<SaveOutlined />} disabled={!dirty} loading={save.isPending} onClick={() => save.mutate()}>Save rates</Button>
          </Flex>
        )
      }
      styles={{ body: { padding: 0 } }}
    >
      {!editable && <Alert type="info" showIcon title="These rates are locked. To change them, create a revision of the contract." style={{ margin: 16 }} />}
      {saved.isError && <Alert type="error" showIcon title={saved.error.message} style={{ margin: 16 }} />}
      <Flex style={{ padding: 16 }}>
        <Input.Search allowClear placeholder="Filter by place or vehicle" style={{ width: 320, maxWidth: '100%' }} value={search} onChange={(e) => setSearch(e.target.value)} />
      </Flex>
      <Table<Row>
        rowKey="key"
        size="middle"
        columns={columns}
        dataSource={visible}
        loading={saved.isLoading}
        scroll={{ x: 'max-content' }}
        pagination={{ pageSize: 25, hideOnSinglePage: true, showSizeChanger: false }}
        locale={{ emptyText: editable ? 'No rates yet. Add the first one.' : 'No rates' }}
      />
      <RateDrawer
        open={drawer.open}
        type={type}
        rate={drawer.index === null ? null : rows[drawer.index] ?? null}
        zones={zones.data ?? []}
        vehicleTypes={vehicleTypes.data ?? []}
        onClose={() => setDrawer({ open: false, index: null })}
        onSave={edit}
      />
    </Card>
  )
}

/** The API returns extra read-only fields (id, lane, names); only the input shape goes back. */
function stripServerFields(rate: RateInputDto & { id?: string; lane?: string }): RateInputDto {
  const { id: _id, lane: _lane, ...input } = rate
  return {
    origin: input.origin,
    destination: input.destination,
    bothWays: input.bothWays,
    vehicleTypeId: input.vehicleTypeId,
    minDistanceKm: input.minDistanceKm,
    maxDistanceKm: input.maxDistanceKm,
    pricing: input.pricing,
  }
}
