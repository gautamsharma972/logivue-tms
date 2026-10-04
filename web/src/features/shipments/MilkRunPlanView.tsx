import { Alert, Card, Col, Collapse, Flex, Row, Statistic, Table, Tag, Typography } from 'antd'
import type { MilkRunPlan, MilkRunTrip } from '@/lib/api/types'
import { formatDateTime, formatInrExact } from '@/lib/format'
import { DriverCell, RouteSourceTag, TransporterCell, UtilizationBar, VehicleCell, formatKg, formatMinutes } from './shared'

function Trip({ trip }: { trip: MilkRunTrip }) {
  const saving = trip.templateOrderKm !== null && trip.shortestOrderKm !== null ? trip.templateOrderKm - trip.shortestOrderKm : 0
  return (
    <Card
      size="small"
      title={
        <Flex gap={8} wrap align="center">
          <span>Trip {trip.number} · {trip.vehicleTypeName}</span>
          <RouteSourceTag source={trip.source} />
        </Flex>
      }
      extra={<Typography.Text strong>{trip.cost === null ? 'Cost unknown' : formatInrExact(trip.cost)}</Typography.Text>}
    >
      <Row gutter={[16, 8]} style={{ marginBottom: 12 }}>
        <Col xs={24} md={8}><Typography.Text type="secondary">Transporter</Typography.Text><TransporterCell transporter={trip.transporter} fallbackName={trip.transporterName} /></Col>
        <Col xs={24} md={8}><Typography.Text type="secondary">Vehicle</Typography.Text><VehicleCell typeName={trip.vehicleTypeName} vehicle={trip.vehicle} /></Col>
        <Col xs={24} md={8}><Typography.Text type="secondary">Driver</Typography.Text><DriverCell driver={trip.driver} /></Col>
      </Row>
      <Row gutter={16}>
        <Col xs={24} md={12}>
          <Typography.Text type="secondary">Peak load {formatKg(trip.peakWeightKg)}{trip.payloadKg ? ` of ${formatKg(trip.payloadKg)}` : ''}</Typography.Text>
          <UtilizationBar value={trip.weightUtilisation} />
        </Col>
        <Col xs={24} md={12}>
          <Typography.Text type="secondary">{trip.peakVolumeCbm === null ? 'Volume not entered' : `Peak volume ${trip.peakVolumeCbm} CBM${trip.volumeCapacityCbm ? ` of ${trip.volumeCapacityCbm} CBM` : ''}`}</Typography.Text>
          {trip.peakVolumeCbm !== null && <UtilizationBar value={trip.volumeUtilisation} />}
        </Col>
      </Row>
      <Flex gap={8} wrap align="center" style={{ marginTop: 8 }}>
        <Typography.Text strong>{trip.distanceKm.toLocaleString('en-IN')} km</Typography.Text>
        <Typography.Text type="secondary">· driving {formatMinutes(trip.drivingMinutes)} · whole trip {formatMinutes(trip.totalMinutes)} · {formatDateTime(trip.departure)} → {formatDateTime(trip.returnAt)}</Typography.Text>
        {trip.costPerTonneKm !== null && <Tag>₹{trip.costPerTonneKm}/tonne-km</Tag>}
        {trip.stops.length > 3 && <Tag>Stop order: {trip.sequenceMethod === 'Exact' ? 'best of all orders' : trip.sequenceMethod === 'Given' ? 'as planned' : 'improved heuristic'}</Tag>}
      </Flex>
      {saving > 0.5 && <Alert type="info" showIcon style={{ marginTop: 8 }} title={`The shortest order would be ${trip.shortestOrderKm} km instead of ${trip.templateOrderKm} km, saving ${saving.toFixed(1)} km. Turn off “keep the planned order” to see it.`} />}
      {trip.warnings.map((w) => <Alert key={w} type="warning" showIcon style={{ marginTop: 8 }} title={w} />)}
      <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>{trip.reason}</Typography.Paragraph>
      <Table
        size="small"
        pagination={false}
        rowKey="sequence"
        dataSource={trip.stops}
        scroll={{ x: 'max-content' }}
        columns={[
          { title: '#', dataIndex: 'sequence', width: 40 },
          { title: 'Stop', key: 's', render: (_: unknown, s: MilkRunTrip['stops'][number]) => <>{s.kind === 'Depot' ? 'Back at depot' : s.kind} · {s.label}</> },
          { title: 'Orders', key: 'o', render: (_: unknown, s: MilkRunTrip['stops'][number]) => (s.orders.length ? s.orders.map((o) => o.number).join(', ') : '—') },
          { title: 'Load', dataIndex: 'weightKg', align: 'right', render: (w: number) => (w ? formatKg(w) : '—') },
          { title: 'Arrive', dataIndex: 'arrival', render: (v: string | null) => (v ? formatDateTime(v) : '—') },
          { title: 'Waits', dataIndex: 'waitMinutes', render: (m: number | null) => (m ? formatMinutes(m) : '—') },
          { title: 'On board', dataIndex: 'onboardKg', align: 'right', render: formatKg },
        ]}
      />
      <Collapse ghost size="small" items={[{
        key: 'alt',
        label: `Vehicles considered (${trip.alternatives.length})`,
        children: (
          <Table size="small" pagination={false} rowKey={(a) => `${a.vehicleTypeId ?? a.verdict}-${a.transporterId ?? ''}`} dataSource={trip.alternatives}
            columns={[
              { title: 'Vehicle', key: 'v', render: (_: unknown, a: MilkRunTrip['alternatives'][number]) => <><VehicleCell typeName={a.vehicleTypeName} vehicle={a.vehicle} priced={a.total !== null} />{a.chosen && <Tag color="green">Chosen</Tag>}</> },
              { title: 'Transporter', key: 't', render: (_: unknown, a: MilkRunTrip['alternatives'][number]) => <TransporterCell transporter={a.transporter} fallbackName={a.transporterName} /> },
              { title: 'Driver', key: 'd', render: (_: unknown, a: MilkRunTrip['alternatives'][number]) => (a.total === null ? '—' : <DriverCell driver={a.driver} />) },
              { title: 'Cost', dataIndex: 'total', align: 'right', render: (t: number | null) => formatInrExact(t) },
              { title: 'Why', dataIndex: 'verdict' },
            ]} />
        ),
      }]} />
    </Card>
  )
}

/** What a milk run actually does on one day: which stops are served, what is carried, which vehicle, what it costs. */
export function MilkRunPlanView({ plan }: { plan: MilkRunPlan }) {
  const t = plan.totals
  return (
    <Flex vertical gap={16}>
      {plan.warnings.map((w) => <Alert key={w} type="warning" showIcon title={w} />)}
      <Flex wrap gap={12}>
        {[
          ['Orders', t.orders],
          ['Stops served', `${t.stopsServed} (${t.stopsSkipped} skipped)`],
          ['Trips', t.trips],
          ['Collected', formatKg(t.inboundKg)],
          ['Delivered', formatKg(t.outboundKg)],
          ['Distance', `${t.distanceKm.toLocaleString('en-IN')} km`],
          ['Freight', t.cost === null ? '—' : formatInrExact(t.cost)],
        ].map(([title, value]) => (
          <Card key={title as string} size="small" style={{ flex: '1 1 150px' }}><Statistic title={title as string} value={value as string | number} /></Card>
        ))}
      </Flex>
      {plan.trips.length === 0 && <Alert type="info" showIcon title="Nothing to run on this day: no open orders are linked to this run's stops." />}
      {plan.trips.map((trip) => <Trip key={trip.number} trip={trip} />)}
      {plan.skipped.length > 0 && (
        <Card size="small" title={`Skipped stops (${plan.skipped.length})`}>
          <Flex vertical gap={4}>{plan.skipped.map((s) => <Typography.Text key={s.templateSequence}>{s.templateSequence}. {s.label} <Typography.Text type="secondary">— {s.reason}</Typography.Text></Typography.Text>)}</Flex>
        </Card>
      )}
      {plan.unplanned.length > 0 && (
        <Card size="small" title={`Could not be planned (${plan.unplanned.length})`}>
          <Flex vertical gap={8}>
            {plan.unplanned.map((u) => (
              <div key={u.orderId}><Typography.Text strong>{u.number}</Typography.Text> <Tag color="red">{u.code.replaceAll('_', ' ').toLowerCase()}</Tag><div>{u.reason}</div></div>
            ))}
          </Flex>
        </Card>
      )}
    </Flex>
  )
}
