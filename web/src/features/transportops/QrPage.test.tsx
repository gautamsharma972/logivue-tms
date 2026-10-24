import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as api from '@/lib/api/opsApi'
import { renderWithProviders } from '@/test/renderWithProviders'
import { QrPage } from './QrPage'

vi.mock('@/features/auth/AuthContext', async (original) => ({
  ...(await original<typeof import('@/features/auth/AuthContext')>()),
  useAuth: () => ({ user: null, can: () => true, status: 'unauthenticated', login: vi.fn(), logout: vi.fn() }),
}))

const view = (over: Partial<api.QrViewDto> = {}): api.QrViewDto => ({
  lrNumber: 'LR-000042', customerName: 'Demo FMCG', vehicleRegistration: 'MH12AB1234', cfaName: 'Demo CFA', destinationName: 'Distributor Pune', destinationCity: 'Pune', material: 'Soap cartons x 100 PKG (1200 kg, 100 packages)',
  status: 'InTransit', statusText: 'In transit', podStatus: 'NotYetDue', arrivedAt: null, unloadedAt: null, nextAction: 'ConfirmArrival', message: 'When you reach the delivery point, tap Confirm Arrival.', podRejectionReason: null,
  maxFiles: 10, maxFileMb: 10, canMarkPodPending: false, serverTime: '2026-10-24T10:00:00Z', ...over,
})

const page = () => renderWithProviders(<Routes><Route path="/q/:token" element={<QrPage />} /></Routes>, { route: '/q/abc' })

let getCurrentPosition: ReturnType<typeof vi.fn>

beforeEach(() => {
  vi.restoreAllMocks()
  getCurrentPosition = vi.fn()
  Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition }, configurable: true })
})
afterEach(() => vi.restoreAllMocks())

describe('The driver page', () => {
  it('shows the LR and does not ask for the location when it opens', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view())
    page()

    expect(await screen.findByText('LR LR-000042')).toBeInTheDocument()
    expect(screen.getByText('Distributor Pune')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm Arrival' })).toBeInTheDocument()
    expect(getCurrentPosition).not.toHaveBeenCalled()
  })

  it('asks for the location only when Confirm Arrival is tapped and sends what the phone reports', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view())
    const arrive = vi.spyOn(api.qrApi, 'arrive').mockResolvedValue(view({ status: 'Arrived', nextAction: 'ConfirmUnloading', statusText: 'Arrived at destination' }))
    getCurrentPosition.mockImplementation((ok: PositionCallback) => ok({ coords: { latitude: 18.7607, longitude: 73.8637, accuracy: 12 }, timestamp: Date.now() } as GeolocationPosition))
    const user = userEvent.setup()
    page()

    await user.click(await screen.findByRole('button', { name: 'Confirm Arrival' }))

    await waitFor(() => expect(getCurrentPosition).toHaveBeenCalledTimes(1))
    expect(arrive).toHaveBeenCalledWith('abc', expect.objectContaining({ latitude: 18.7607, longitude: 73.8637, accuracyM: 12, locationError: null }))
    expect(await screen.findByRole('button', { name: 'Confirm Unloading Completed' })).toBeInTheDocument()
  })

  it('reports a denied location permission to the server instead of pretending to arrive, and says what went wrong', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view())
    const arrive = vi.spyOn(api.qrApi, 'arrive').mockRejectedValue(new api.QrError(400, 'ops.arrival_permission_denied', 'Location permission was denied. Allow location access and try again.'))
    getCurrentPosition.mockImplementation((_ok: PositionCallback, fail: PositionErrorCallback) => fail({ code: 1 } as GeolocationPositionError))
    const user = userEvent.setup()
    page()

    await user.click(await screen.findByRole('button', { name: 'Confirm Arrival' }))

    await waitFor(() => expect(arrive).toHaveBeenCalledWith('abc', expect.objectContaining({ latitude: null, locationError: 'denied' })))
    expect(await screen.findByText(/permission was denied/i)).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /Confirm Arrival/ })).toBeInTheDocument() // the driver can try again (antd keeps its loading icon in jsdom, so match loosely)
  })

  it('confirms unloading without ever touching the location', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view({ status: 'Arrived', nextAction: 'ConfirmUnloading', statusText: 'Arrived at destination' }))
    const unload = vi.spyOn(api.qrApi, 'unload').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'Pending', canMarkPodPending: true }))
    const user = userEvent.setup()
    page()

    await user.click(await screen.findByRole('button', { name: 'Confirm Unloading Completed' }))

    await waitFor(() => expect(unload).toHaveBeenCalledWith('abc'))
    expect(getCurrentPosition).not.toHaveBeenCalled()
    expect(await screen.findByRole('button', { name: 'POD not available yet' })).toBeInTheDocument()
  })

  it('lets the driver say the POD is not available, without a location', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'Pending', canMarkPodPending: true }))
    const pending = vi.spyOn(api.qrApi, 'podPending').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'Pending' }))
    const user = userEvent.setup()
    page()

    await user.click(await screen.findByRole('button', { name: 'POD not available yet' }))

    await waitFor(() => expect(pending).toHaveBeenCalledWith('abc'))
    expect(getCurrentPosition).not.toHaveBeenCalled()
  })

  it('uploads the chosen photos without a location and shows the confirmation', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'NotYetDue', canMarkPodPending: true }))
    const upload = vi.spyOn(api.qrApi, 'uploadPod').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'None', podStatus: 'Received', message: 'Delivery complete. Your proof of delivery has been received and is being checked.' }))
    const user = userEvent.setup()
    page()
    await screen.findByText('Proof of delivery (POD)')

    const input = screen.getByLabelText('Upload photos or PDFs') as HTMLInputElement
    await user.upload(input, new File(['x'], 'pod.png', { type: 'image/png' }))
    await user.click(screen.getByRole('button', { name: 'Upload POD' }))

    await waitFor(() => expect(upload).toHaveBeenCalledWith('abc', [expect.objectContaining({ name: 'pod.png' })]))
    expect(getCurrentPosition).not.toHaveBeenCalled()
    expect(await screen.findByText(/Your proof of delivery has been received/)).toBeInTheDocument()
  })

  it('keeps the files and offers a retry when the upload fails', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'Pending', canMarkPodPending: true }))
    vi.spyOn(api.qrApi, 'uploadPod').mockRejectedValue(new Error('network'))
    const user = userEvent.setup()
    page()
    await screen.findByText('Proof of delivery (POD)')

    await user.upload(screen.getByLabelText('Upload photos or PDFs') as HTMLInputElement, new File(['x'], 'pod.png', { type: 'image/png' }))
    await user.click(screen.getByRole('button', { name: 'Upload POD' }))

    expect(await screen.findByText(/Could not reach the server/)).toBeInTheDocument()
    expect(screen.getByAltText('pod.png')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: /Try again/ })).toBeInTheDocument()
  })

  it('shows the reviewer\'s reason when the proof was rejected and asks for a corrected copy', async () => {
    vi.spyOn(api.qrApi, 'view').mockResolvedValue(view({ status: 'UnloadingCompleted', nextAction: 'UploadPod', podStatus: 'Rejected', podRejectionReason: 'Signature is missing' }))
    page()

    expect(await screen.findByText(/Signature is missing/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Upload POD' })).toBeInTheDocument()
  })

  it('shows nothing about the LR for a code that is not valid', async () => {
    vi.spyOn(api.qrApi, 'view').mockRejectedValue(new api.QrError(404, 'ops.qr_invalid', 'This code is not valid.'))
    page()

    expect(await screen.findByText('This code is not valid')).toBeInTheDocument()
    expect(screen.queryByText(/LR LR-/)).not.toBeInTheDocument()
  })
})
