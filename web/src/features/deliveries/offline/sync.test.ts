import { describe, expect, it, vi } from 'vitest'
import type { SyncCommand, SyncResultDto } from '@/lib/api/types'
import { memoryStore } from './store'
import { SyncManager, localStatus, type SyncApi } from './sync'

const result = (c: SyncCommand, over: Partial<SyncResultDto> = {}): SyncResultDto => ({
  clientRecordId: c.clientRecordId, status: 'Synced', duplicate: false, attempt: 1, error: null, errorCode: null, deliveryStatus: null, podId: c.type === 'complete' ? 'pod-1' : null, ...over,
})

function api(over: Partial<SyncApi> = {}): SyncApi {
  return {
    sync: vi.fn(async (_d: string, commands: SyncCommand[]) => ({ results: commands.map((c) => result(c)) })),
    addEvidence: vi.fn(async () => ({})),
    addSignature: vi.fn(async () => ({})),
    submitPod: vi.fn(async () => ({})),
    download: vi.fn(),
    ...over,
  }
}

const networkDown = () => Object.assign(new Error('Network Error'), { code: 'ERR_NETWORK' })

describe('SyncManager', () => {
  it('keeps everything when the signal is down and sends it, in order, when it returns', async () => {
    const server = api({ sync: vi.fn().mockRejectedValueOnce(networkDown()) })
    const manager = new SyncManager(memoryStore(), server, 'device-1', () => new Date('2026-10-05T10:00:00Z'))
    await manager.enqueue('start', 'd1', {})
    await manager.enqueue('arrive', 'd1', {})

    const offline = await manager.flush()

    expect(offline.reachable).toBe(false)
    expect(await manager.pendingCount()).toBe(2)

    vi.mocked(server.sync).mockImplementation(async (_d: string, commands: SyncCommand[]) => ({ results: commands.map((c) => result(c)) }))
    const online = await manager.flush()

    expect(online).toMatchObject({ reachable: true, synced: 2 })
    expect(vi.mocked(server.sync).mock.calls[1]![1].map((c: SyncCommand) => c.type)).toEqual(['start', 'arrive'])
    expect(await manager.pendingCount()).toBe(0)
  })

  it('sends a command that was already sent again with the same key, so a lost answer cannot do it twice', async () => {
    const server = api({ sync: vi.fn().mockRejectedValueOnce(networkDown()) })
    const manager = new SyncManager(memoryStore(), server, 'device-1')
    const command = await manager.enqueue('start', 'd1', {})

    await manager.flush()
    await manager.flush()

    const keys = vi.mocked(server.sync).mock.calls.map((c: [string, SyncCommand[]]) => c[1][0]!.clientRecordId)
    expect(keys).toEqual([command.id, command.id])
  })

  it('records a conflict without retrying it, and retries a failure up to a limit', async () => {
    const server = api({
      sync: vi.fn(async (_d: string, commands: SyncCommand[]) => ({
        results: commands.map((c) => (c.type === 'start' ? result(c, { status: 'Conflict', errorCode: 'deliveries.invalid_state', error: 'Already started' }) : result(c, { status: 'Failed', error: 'bad', errorCode: 'x', attempt: 1 }))),
      })),
    })
    const manager = new SyncManager(memoryStore(), server, 'device-1')
    await manager.enqueue('start', 'd1', {})
    await manager.enqueue('attempt', 'd1', {})

    const first = await manager.flush()
    expect(first).toMatchObject({ conflicts: 1, failed: 1 })

    await manager.flush()
    const sent = vi.mocked(server.sync).mock.calls.map((c: [string, SyncCommand[]]) => c[1].map((x) => x.type))
    expect(sent).toEqual([['start', 'attempt'], ['attempt']]) // the conflict is left for the driver

    const [conflict] = (await manager.commands()).filter((c) => c.status === 'Conflict')
    await manager.retry(conflict!.id)
    expect((await manager.commands()).find((c) => c.id === conflict!.id)?.status).toBe('Pending')
  })

  it('uploads evidence only after the delivery is completed on the server, then submits the proof', async () => {
    const server = api()
    const manager = new SyncManager(memoryStore(), server, 'device-1')
    await manager.enqueueUpload({ deliveryId: 'd1', kind: 'evidence', evidenceType: 'PackagePhoto', signerName: null, blob: new Blob(['x']), fix: null })
    await manager.enqueueSubmit('d1')

    const early = await manager.flush()
    expect(early).toMatchObject({ uploaded: 0, submitted: 0 })
    expect(server.addEvidence).not.toHaveBeenCalled()

    await manager.enqueue('complete', 'd1', { outcome: 'Full' })
    const later = await manager.flush()

    expect(later).toMatchObject({ synced: 1, uploaded: 1, submitted: 1 })
    expect(server.addEvidence).toHaveBeenCalledWith('pod-1', expect.any(Blob), 'PackagePhoto', null, expect.any(String), 'device-1')
    expect(server.submitPod).toHaveBeenCalledWith('pod-1')
    expect(await manager.pendingCount()).toBe(0)
  })

  it('keeps an upload that failed and does not submit the proof without it', async () => {
    const server = api({ addEvidence: vi.fn().mockRejectedValue(Object.assign(new Error('That exact file is already on this proof.'), { response: { data: { detail: 'Duplicate' } } })) })
    const manager = new SyncManager(memoryStore(), server, 'device-1')
    await manager.enqueue('complete', 'd1', { outcome: 'Full' })
    await manager.enqueueUpload({ deliveryId: 'd1', kind: 'evidence', evidenceType: null, signerName: null, blob: new Blob(['x']), fix: null })
    await manager.enqueueSubmit('d1')

    const summary = await manager.flush()

    expect(summary).toMatchObject({ uploaded: 0, submitted: 0, failed: 1 })
    expect(server.submitPod).not.toHaveBeenCalled()
    expect(await manager.pendingCount()).toBe(2)
  })

  it('shows a delivery as the driver left it even before the server knows', async () => {
    const manager = new SyncManager(memoryStore(), api(), 'device-1')
    await manager.enqueue('start', 'd1', {})
    await manager.enqueue('arrive', 'd1', {})
    const commands = await manager.commands()

    expect(localStatus('Assigned', commands)).toBe('Arrived')
    expect(localStatus('Arrived', [...commands, { ...commands[0]!, type: 'complete', payload: { outcome: 'Shortage' } }])).toBe('PartiallyDelivered')
    expect(localStatus('Assigned', commands.map((c) => ({ ...c, status: 'Synced' as const })))).toBe('Assigned') // already counted by the server
  })
})
