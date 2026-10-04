import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Collapse, DatePicker, Drawer, Flex, Form, Input, InputNumber, Select, Switch } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useState } from 'react'
import { contractsApi, transportersApi } from '@/lib/api/endpoints'
import { toApiError } from '@/lib/api/errors'
import { queryKeys } from '@/lib/api/queryKeys'
import type { ContractDto, ContractTerms, ContractType, FuelClause } from '@/lib/api/types'
import { applyFieldErrors } from '@/lib/formErrors'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

interface FormValues {
  transporterId: string
  type: ContractType
  title: string
  period: [Dayjs, Dayjs]
  paymentTermsDays: number
  estimatedAnnualSpend: number | null
  terms: ContractTerms
  fuelEnabled: boolean
  fuel: FuelClause
}

const defaultTerms: ContractTerms = {
  volumetricKgPerCbm: 250,
  detentionFreeHours: 24,
  detentionRatePerHour: 0,
  loadingCharge: 0,
  unloadingCharge: 0,
  multiDropChargePerPoint: 0,
  minChargePerConsignment: 0,
  notes: null,
}

const defaultFuel: FuelClause = {
  region: '',
  basePricePerLitre: 90,
  unit: 'Rupees',
  stepSize: 1,
  impactPercentPerStep: 0.5,
  deadBand: 0,
  capPercent: null,
  direction: 'Both',
}

const money = { min: 0, precision: 2, style: { width: '100%' }, controls: false } as const

interface Props {
  open: boolean
  /** The contract being edited, or null to draft a new one. */
  contract: ContractDto | null
  onClose: () => void
  onCreated?: (contract: ContractDto) => void
}

export function ContractFormDrawer({ open, contract, onClose, onCreated }: Props) {
  const [form] = Form.useForm<FormValues>()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const editing = contract !== null
  const fuelEnabled = Form.useWatch('fuelEnabled', form) as boolean | undefined
  const unit = Form.useWatch(['fuel', 'unit'], form) as FuelClause['unit'] | undefined
  const [search, setSearch] = useState('')
  const debounced = useDebouncedValue(search.trim())

  const transporters = useQuery({
    queryKey: queryKeys.transporters.lookup(debounced),
    queryFn: () => transportersApi.lookup(debounced || undefined),
    enabled: open && !editing,
  })
  const diesel = useQuery({ queryKey: queryKeys.contracts.diesel, queryFn: () => contractsApi.dieselPrices(), enabled: open })
  const regions = [...new Set(diesel.data?.map((d) => d.region) ?? [])]

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      contract
        ? {
            transporterId: contract.summary.transporterId,
            type: contract.summary.type,
            title: contract.summary.title,
            period: [dayjs(contract.summary.effectiveFrom), dayjs(contract.summary.effectiveTo)],
            paymentTermsDays: contract.paymentTermsDays,
            estimatedAnnualSpend: contract.summary.estimatedAnnualSpend,
            terms: contract.terms,
            fuelEnabled: contract.fuel !== null,
            fuel: contract.fuel ?? defaultFuel,
          }
        : { type: 'Ftl', period: [dayjs(), dayjs().add(1, 'year').subtract(1, 'day')], paymentTermsDays: 30, terms: defaultTerms, fuelEnabled: false, fuel: defaultFuel },
    )
  }, [open, contract, form])

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const body = {
        transporterId: v.transporterId,
        type: v.type,
        title: v.title.trim(),
        effectiveFrom: v.period[0].format('YYYY-MM-DD'),
        effectiveTo: v.period[1].format('YYYY-MM-DD'),
        paymentTermsDays: v.paymentTermsDays,
        estimatedAnnualSpend: v.estimatedAnnualSpend ?? null,
        ownerUserId: contract?.ownerUserId ?? null,
        terms: { ...v.terms, notes: v.terms.notes?.trim() || null },
        fuel: v.fuelEnabled ? { ...v.fuel, region: v.fuel.region.trim(), capPercent: v.fuel.capPercent ?? null } : null,
        version: contract?.version ?? null,
      }
      return contract ? contractsApi.update(contract.summary.id, body) : contractsApi.create(body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.all })
      void message.success(editing ? 'Contract updated' : `Contract ${saved.summary.number} drafted`)
      onClose()
      if (!editing) onCreated?.(saved)
    },
    onError: async (e) => {
      const error = toApiError(e)
      if (applyFieldErrors(form, error, { effectiveTo: 'period', effectiveFrom: 'period' })) return
      if (error.code === 'concurrency.conflict') {
        await queryClient.invalidateQueries({ queryKey: queryKeys.contracts.all })
        onClose()
      }
      void message.error(error.message)
    },
  })

  return (
    <Drawer
      open={open}
      onClose={onClose}
      size={680}
      destroyOnHidden
      maskClosable={!save.isPending}
      title={editing ? 'Edit contract' : 'New contract'}
      footer={
        <Flex justify="flex-end" gap={8}>
          <Button onClick={onClose} disabled={save.isPending}>Cancel</Button>
          <Button type="primary" loading={save.isPending} onClick={() => form.submit()}>{editing ? 'Save changes' : 'Create draft'}</Button>
        </Flex>
      }
    >
      <Form<FormValues> form={form} layout="vertical" requiredMark="optional" disabled={save.isPending} onFinish={(v) => save.mutate(v)}>
        {!editing && (
          <Alert type="info" showIcon style={{ marginBottom: 16 }} title="You'll add the rates next. The transporter and contract type can't be changed afterwards." />
        )}
        <Flex gap={16} wrap>
          <Form.Item label="Transporter" name="transporterId" rules={[{ required: true, message: 'Choose the transporter' }]} style={{ flex: '2 1 280px' }}>
            {editing ? (
              <Input disabled value={contract.summary.transporterName} />
            ) : (
              <Select
                showSearch
                filterOption={false}
                onSearch={setSearch}
                loading={transporters.isFetching}
                placeholder="Search active transporters"
                options={transporters.data?.map((t) => ({ value: t.id, label: `${t.legalName} (${t.code})` }))}
              />
            )}
          </Form.Item>
          <Form.Item label="Contract type" name="type" style={{ flex: '1 1 180px' }}>
            <Select
              disabled={editing}
              options={[{ value: 'Ftl', label: 'Full truck load' }, { value: 'Ptl', label: 'Part load' }, { value: 'Dedicated', label: 'Dedicated vehicle' }]}
            />
          </Form.Item>
        </Flex>

        <Form.Item label="Title" name="title" rules={[{ required: true, whitespace: true, message: 'Give the contract a title' }, { max: 200 }]}>
          <Input placeholder="e.g. West–North FTL lanes FY26-27" />
        </Form.Item>
        <Form.Item label="Valid from – to" name="period" rules={[{ required: true, message: 'Choose the validity period' }]}>
          <DatePicker.RangePicker style={{ width: '100%' }} format="D MMM YYYY" />
        </Form.Item>
        <Flex gap={16} wrap>
          <Form.Item label="Payment terms (days)" name="paymentTermsDays" rules={[{ required: true }]} style={{ flex: '1 1 160px' }}>
            <InputNumber min={0} max={365} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item label="Estimated annual spend" name="estimatedAnnualSpend" extra="Used to decide how many approvals are needed." style={{ flex: '2 1 240px' }}>
            <InputNumber {...money} prefix="₹" />
          </Form.Item>
        </Flex>

        <Collapse
          ghost
          defaultActiveKey={['terms']}
          items={[
            {
              key: 'terms',
              label: 'Commercial terms',
              children: (
                <>
                  <Flex gap={16} wrap>
                    <Form.Item label="Volumetric factor (kg per CBM)" name={['terms', 'volumetricKgPerCbm']} extra="Part-load weight is the higher of actual and volume × this." style={{ flex: '1 1 220px' }}>
                      <InputNumber min={50} max={1000} style={{ width: '100%' }} />
                    </Form.Item>
                    <Form.Item label="Minimum charge per consignment" name={['terms', 'minChargePerConsignment']} style={{ flex: '1 1 220px' }}>
                      <InputNumber {...money} prefix="₹" />
                    </Form.Item>
                  </Flex>
                  <Flex gap={16} wrap>
                    <Form.Item label="Loading charge" name={['terms', 'loadingCharge']} style={{ flex: '1 1 150px' }}><InputNumber {...money} prefix="₹" /></Form.Item>
                    <Form.Item label="Unloading charge" name={['terms', 'unloadingCharge']} style={{ flex: '1 1 150px' }}><InputNumber {...money} prefix="₹" /></Form.Item>
                    <Form.Item label="Per extra drop point" name={['terms', 'multiDropChargePerPoint']} style={{ flex: '1 1 150px' }}><InputNumber {...money} prefix="₹" /></Form.Item>
                  </Flex>
                  <Flex gap={16} wrap>
                    <Form.Item label="Detention free hours" name={['terms', 'detentionFreeHours']} style={{ flex: '1 1 150px' }}><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
                    <Form.Item label="Detention per hour after that" name={['terms', 'detentionRatePerHour']} style={{ flex: '1 1 150px' }}><InputNumber {...money} prefix="₹" /></Form.Item>
                  </Flex>
                  <Form.Item label="Notes" name={['terms', 'notes']}><Input.TextArea rows={2} maxLength={2000} /></Form.Item>
                </>
              ),
            },
            {
              key: 'fuel',
              label: 'Diesel price variation (DPH) clause',
              children: (
                <>
                  <Form.Item name="fuelEnabled" valuePropName="checked" extra="Adjusts base freight up or down as the diesel price moves away from the base price.">
                    <Switch checkedChildren="Applies" unCheckedChildren="None" />
                  </Form.Item>
                  {fuelEnabled && (
                    <>
                      <Flex gap={16} wrap>
                        <Form.Item label="Diesel price region" name={['fuel', 'region']} rules={[{ required: true, message: 'Choose or type a region' }]} style={{ flex: '1 1 200px' }} extra="Prices are kept under Rate masters.">
                          <Select showSearch allowClear options={regions.map((r) => ({ value: r, label: r }))} placeholder="e.g. Delhi" notFoundContent="Add prices under Rate masters first" />
                        </Form.Item>
                        <Form.Item label="Base diesel price (₹/litre)" name={['fuel', 'basePricePerLitre']} rules={[{ required: true }]} style={{ flex: '1 1 200px' }}><InputNumber {...money} /></Form.Item>
                      </Flex>
                      <Flex gap={16} wrap>
                        <Form.Item label="Measure change in" name={['fuel', 'unit']} style={{ flex: '1 1 160px' }}>
                          <Select options={[{ value: 'Rupees', label: '₹ per litre' }, { value: 'Percent', label: '% of base price' }]} />
                        </Form.Item>
                        <Form.Item label={`For every ${unit === 'Percent' ? '% change' : '₹ change'}`} name={['fuel', 'stepSize']} style={{ flex: '1 1 160px' }}><InputNumber min={0.01} precision={2} style={{ width: '100%' }} /></Form.Item>
                        <Form.Item label="Freight changes by (%)" name={['fuel', 'impactPercentPerStep']} style={{ flex: '1 1 160px' }}><InputNumber min={0.01} max={100} precision={2} style={{ width: '100%' }} /></Form.Item>
                      </Flex>
                      <Flex gap={16} wrap>
                        <Form.Item label={`Tolerance (${unit === 'Percent' ? '%' : '₹'})`} name={['fuel', 'deadBand']} extra="No change until the price moves at least this much." style={{ flex: '1 1 160px' }}><InputNumber min={0} precision={2} style={{ width: '100%' }} /></Form.Item>
                        <Form.Item label="Cap on adjustment (%)" name={['fuel', 'capPercent']} extra="Leave empty for no cap." style={{ flex: '1 1 160px' }}><InputNumber min={0.01} max={100} precision={2} style={{ width: '100%' }} /></Form.Item>
                        <Form.Item label="Direction" name={['fuel', 'direction']} style={{ flex: '1 1 160px' }}>
                          <Select options={[{ value: 'Both', label: 'Up and down' }, { value: 'EscalationOnly', label: 'Increases only' }]} />
                        </Form.Item>
                      </Flex>
                    </>
                  )}
                </>
              ),
            },
          ]}
        />
      </Form>
    </Drawer>
  )
}
