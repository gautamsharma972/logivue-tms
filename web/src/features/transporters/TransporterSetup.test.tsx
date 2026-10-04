import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { masterDataApi } from '@/lib/api/endpoints'
import type { DocumentRuleDto, MasterEntryDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { TransporterSetupPage } from './TransporterSetupPage'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  masterDataApi: { types: vi.fn(), saveType: vi.fn(), capabilityTypes: vi.fn(), saveCapabilityType: vi.fn(), documentRules: vi.fn(), saveDocumentRule: vi.fn() },
}))

const rule = (o: Partial<DocumentRuleDto>): DocumentRuleDto => ({
  kind: 'Insurance', label: 'Insurance', owner: 'Vehicle', isMandatory: true, expiryRequired: true, renewalReminderDays: 30, blockWhenExpired: true, isActive: true, isCustomised: false, ...o,
})

const types: MasterEntryDto[] = [{ code: 'FTL', name: 'Full truck load', isActive: true, isBuiltIn: true }]

describe('TransporterSetupPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(masterDataApi.types).mockResolvedValue(types)
    vi.mocked(masterDataApi.capabilityTypes).mockResolvedValue([{ code: 'HAZARDOUS', name: 'Hazardous goods', isActive: true, isBuiltIn: true }])
    vi.mocked(masterDataApi.documentRules).mockResolvedValue([rule({}), rule({ kind: 'Puc', label: 'PUC certificate', isMandatory: false, blockWhenExpired: false, renewalReminderDays: 15 })])
  })

  it('saves a changed document rule and only that rule', async () => {
    vi.mocked(masterDataApi.saveDocumentRule).mockResolvedValue(rule({ kind: 'Puc', label: 'PUC certificate', isMandatory: true, isCustomised: true }))
    const user = userEvent.setup()
    renderWithProviders(<TransporterSetupPage />)

    await user.click(await screen.findByRole('switch', { name: 'PUC certificate mandatory' }))
    const saves = screen.getAllByRole('button', { name: 'Save' })
    expect(saves[0]).toBeDisabled() // insurance is untouched
    await user.click(saves[1]!)

    expect(masterDataApi.saveDocumentRule).toHaveBeenCalledWith('Puc', { isMandatory: true, expiryRequired: true, renewalReminderDays: 15, blockWhenExpired: false, isActive: true })
  })

  it('switching a paper off also clears mandatory', async () => {
    const user = userEvent.setup()
    renderWithProviders(<TransporterSetupPage />)

    await user.click(await screen.findByRole('switch', { name: 'Insurance in use' }))
    expect(screen.getByRole('switch', { name: 'Insurance mandatory' })).not.toBeChecked()
    expect(screen.getByRole('switch', { name: 'Insurance mandatory' })).toBeDisabled()
  })

  it('adds a capability of the tenants own', async () => {
    vi.mocked(masterDataApi.saveCapabilityType).mockResolvedValue({ code: 'COLD_CHAIN', name: 'Cold chain', isActive: true, isBuiltIn: false })
    const user = userEvent.setup()
    renderWithProviders(<TransporterSetupPage />)

    await user.click(await screen.findByRole('tab', { name: 'Capabilities' }))
    await user.type(await screen.findByLabelText('New code'), 'COLD_CHAIN')
    await user.type(screen.getByLabelText('New name'), 'Cold chain')
    await user.click(screen.getByRole('button', { name: /Add/ }))

    expect(masterDataApi.saveCapabilityType).toHaveBeenCalledWith({ code: 'COLD_CHAIN', name: 'Cold chain', isActive: true })
  })
})
