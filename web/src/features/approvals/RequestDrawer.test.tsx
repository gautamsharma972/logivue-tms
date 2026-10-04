import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { approvalsApi } from '@/lib/api/endpoints'
import type { RequestDto } from '@/lib/api/types'
import { renderWithProviders } from '@/test/renderWithProviders'
import { RequestDrawer } from './RequestDrawer'

vi.mock('@/lib/api/endpoints', () => ({
  authApi: { login: vi.fn(), logout: vi.fn(), me: vi.fn() },
  approvalsApi: { request: vi.fn(), approve: vi.fn(), reject: vi.fn(), cancel: vi.fn() },
}))

const base: RequestDto = {
  id: 'r1',
  documentType: 'spot_rate',
  documentTypeName: 'Spot rate',
  documentId: 'd1',
  title: 'Mumbai → Pune, 32ft',
  amount: 125000,
  requesterId: 'u1',
  requesterName: 'Riya Requester',
  status: 'Pending',
  createdAt: '2026-05-01T09:00:00Z',
  completedAt: null,
  currentStepIndex: 0,
  canDecide: true,
  canCancel: false,
  version: 0,
  steps: [
    { order: 1, name: 'Regional manager', requiredPermission: 'spot_rate.approve', status: 'Pending', decidedBy: null, decidedByName: null, onBehalfOf: null, onBehalfOfName: null, decidedAt: null, comment: null },
  ],
}

async function openModal() {
  await screen.findByLabelText('Comment')
  return within(document.querySelector('.ant-modal') as HTMLElement)
}

function renderDrawer(request: RequestDto) {
  vi.mocked(approvalsApi.request).mockResolvedValue(request)
  return renderWithProviders(<RequestDrawer requestId={request.id} onClose={() => {}} />)
}

describe('RequestDrawer', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the request, its amount and who it is waiting for', async () => {
    renderDrawer(base)

    expect(await screen.findByText('Mumbai → Pune, 32ft')).toBeInTheDocument()
    expect(screen.getByText('Riya Requester')).toBeInTheDocument()
    expect(screen.getByText('Regional manager')).toBeInTheDocument()
    expect(screen.getByText('spot_rate.approve')).toBeInTheDocument()
  })

  it('offers decisions only to someone who is allowed to decide', async () => {
    renderDrawer({ ...base, canDecide: false })

    await screen.findByText('Mumbai → Pune, 32ft')
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
  })

  it('will not reject without a reason', async () => {
    const user = userEvent.setup()
    renderDrawer(base)
    await user.click(await screen.findByRole('button', { name: 'Reject' }))

    const dialog = await openModal()
    const confirm = dialog.getByRole('button', { name: 'Reject' })
    expect(confirm).toBeDisabled()

    await user.type(dialog.getByLabelText('Comment'), 'Rate is too high')
    expect(confirm).toBeEnabled()
    expect(approvalsApi.reject).not.toHaveBeenCalled()
  })

  it('sends the comment when approving', async () => {
    vi.mocked(approvalsApi.approve).mockResolvedValue({ ...base, status: 'Approved' })
    const user = userEvent.setup()
    renderDrawer(base)
    await user.click(await screen.findByRole('button', { name: 'Approve' }))

    const dialog = await openModal()
    await user.type(dialog.getByLabelText('Comment'), 'Looks right')
    await user.click(dialog.getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(approvalsApi.approve).toHaveBeenCalledWith('r1', 'Looks right'))
  })

  it('records a delegate decision as being on behalf of the delegator', async () => {
    renderDrawer({
      ...base,
      status: 'Approved',
      canDecide: false,
      currentStepIndex: null,
      steps: [{ ...base.steps[0]!, status: 'Approved', decidedBy: 'u2', decidedByName: 'Dev Delegate', onBehalfOf: 'u3', onBehalfOfName: 'Mira Manager', decidedAt: '2026-05-01T10:00:00Z', comment: 'Covering' }],
    })

    expect(await screen.findByText('Dev Delegate')).toBeInTheDocument()
    expect(screen.getByText('Mira Manager')).toBeInTheDocument()
    expect(screen.getByText(/Covering/)).toBeInTheDocument()
  })
})
