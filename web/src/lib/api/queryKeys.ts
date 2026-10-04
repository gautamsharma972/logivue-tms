import type { ListLocationsParams, ListPodParams, ListOrdersParams, ListShipmentsParams, ListContractsParams, ListAuditLogsParams, ListRequestsParams, ListTransportersParams, ListUsersParams } from './types'

/** One place for cache keys so invalidation after a mutation can never drift from the queries. */
export const queryKeys = {
  users: {
    all: ['users'] as const,
    list: (params: ListUsersParams) => ['users', 'list', params] as const,
  },
  roles: {
    all: ['roles'] as const,
    permissions: ['roles', 'permissions'] as const,
  },
  audit: {
    list: (params: ListAuditLogsParams) => ['audit', 'list', params] as const,
  },
  approvals: {
    all: ['approvals'] as const,
    requests: (params: ListRequestsParams) => ['approvals', 'requests', params] as const,
    request: (id: string) => ['approvals', 'request', id] as const,
    policies: ['approvals', 'policies'] as const,
    stepPermissions: ['approvals', 'policies', 'permissions'] as const,
    delegations: ['approvals', 'delegations'] as const,
  },
  transporters: {
    all: ['transporters'] as const,
    list: (params: ListTransportersParams) => ['transporters', 'list', params] as const,
    detail: (id: string) => ['transporters', 'detail', id] as const,
    vehicles: (id: string, page: number, search?: string) => ['transporters', id, 'vehicles', page, search] as const,
    drivers: (id: string, page: number, search?: string) => ['transporters', id, 'drivers', page, search] as const,
    documents: (id: string) => ['transporters', id, 'documents'] as const,
    vehicleTypes: ['transporters', 'vehicle-types'] as const,
    lookup: (search?: string) => ['transporters', 'lookup', search] as const,
    compliance: (withinDays: number, page: number) => ['transporters', 'compliance', withinDays, page] as const,
  },
  contracts: {
    all: ['contracts'] as const,
    list: (params: ListContractsParams) => ['contracts', 'list', params] as const,
    detail: (id: string) => ['contracts', 'detail', id] as const,
    rates: (id: string) => ['contracts', id, 'rates'] as const,
    documents: (id: string) => ['contracts', id, 'documents'] as const,
    expiring: (days: number) => ['contracts', 'expiring', days] as const,
    zones: ['contracts', 'zones'] as const,
    vehicleTypes: ['contracts', 'vehicle-types'] as const,
    diesel: ['contracts', 'diesel'] as const,
  },
  orders: {
    all: ['orders'] as const,
    list: (params: ListOrdersParams) => ['orders', 'list', params] as const,
  },
  locations: {
    all: ['locations'] as const,
    list: (params: ListLocationsParams) => ['locations', 'list', params] as const,
    active: ['locations', 'active'] as const,
  },
  pod: {
    all: ['pod'] as const,
    queue: (params: ListPodParams) => ['pod', 'queue', params] as const,
    ageing: ['pod', 'ageing'] as const,
    documents: (shipmentId: string, orderId: string) => ['pod', 'documents', shipmentId, orderId] as const,
  },
  milkRuns: {
    all: ['milk-runs'] as const,
    list: (page: number) => ['milk-runs', 'list', page] as const,
  },
  planning: {
    all: ['planning'] as const,
    runs: (page: number) => ['planning', 'runs', page] as const,
    run: (id: string) => ['planning', 'run', id] as const,
    versions: (id: string) => ['planning', 'versions', id] as const,
    openOrders: ['planning', 'open-orders'] as const,
    compatibility: ['planning', 'compatibility-rules'] as const,
    dashboard: (from?: string, to?: string) => ['planning', 'dashboard', from, to] as const,
  },
  shipments: {
    all: ['shipments'] as const,
    list: (params: ListShipmentsParams) => ['shipments', 'list', params] as const,
    detail: (id: string) => ['shipments', 'detail', id] as const,
    quotes: (id: string) => ['shipments', id, 'quotes'] as const,
    fleet: (id: string) => ['shipments', id, 'fleet'] as const,
    vehicleTypes: ['shipments', 'vehicle-types'] as const,
    suggestions: ['shipments', 'suggestions'] as const,
    utilization: ['shipments', 'utilization'] as const,
  },
}
