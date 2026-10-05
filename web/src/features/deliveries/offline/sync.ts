import type { DeliveryStatus, MobileBundleDto, SyncCommand, SyncOperation, SyncResultDto } from '@/lib/api/types'
import { newKey, type OfflineStore, type QueuedCommand, type QueuedSubmit, type QueuedUpload } from './store'

/** The few calls the synchroniser makes, so it can be driven by the real API or by a test. */
export interface SyncApi {
  sync(deviceId: string, commands: SyncCommand[]): Promise<{ results: SyncResultDto[] }>
  addEvidence(podId: string, file: Blob, type: string, fix: QueuedUpload['fix'], clientRecordId: string, device: string | null): Promise<unknown>
  addSignature(podId: string, file: Blob, signerName: string): Promise<unknown>
  submitPod(podId: string): Promise<unknown>
  download(): Promise<MobileBundleDto>
}

export interface FlushSummary {
  reachable: boolean
  synced: number
  conflicts: number
  failed: number
  uploaded: number
  submitted: number
}

export const MAX_BATCH = 50
export const MAX_AUTOMATIC_ATTEMPTS = 5

/** A network failure (no response from the server) as opposed to the server saying no. */
export function isOffline(error: unknown): boolean {
  const e = error as { response?: unknown; code?: string; message?: string }
  return !e?.response && (e?.code === 'ERR_NETWORK' || e?.message === 'Network Error' || e instanceof TypeError)
}

function errorText(error: unknown): string {
  const e = error as { response?: { data?: { detail?: string; title?: string } }; message?: string }
  return e?.response?.data?.detail ?? e?.response?.data?.title ?? e?.message ?? 'Failed'
}

/**
 * Keeps what a driver did on the device until the server has it. Commands go up in order in batches; each has its own key, so a batch that is sent twice
 * (the answer was lost) changes nothing the second time. A command the server refuses because the delivery moved on is a conflict for the driver to see,
 * never silently dropped and never forced.
 */
export class SyncManager {
  private flushing: Promise<FlushSummary> | null = null

  private readonly store: OfflineStore
  private readonly api: SyncApi
  private readonly device: string
  private readonly now: () => Date

  constructor(store: OfflineStore, api: SyncApi, device: string, now: () => Date = () => new Date()) {
    this.store = store
    this.api = api
    this.device = device
    this.now = now
  }

  async enqueue(type: SyncOperation, deliveryId: string, payload: unknown): Promise<QueuedCommand> {
    const at = this.now().toISOString()
    const command: QueuedCommand = { id: newKey(), type, deliveryId, payload, createdAt: at, status: 'Pending', attempts: 0, error: null, errorCode: null, podId: null }
    await this.store.put('command', command)
    return command
  }

  async enqueueUpload(upload: Omit<QueuedUpload, 'id' | 'createdAt' | 'attempts' | 'error'>): Promise<QueuedUpload> {
    const record: QueuedUpload = { ...upload, id: newKey(), createdAt: this.now().toISOString(), attempts: 0, error: null }
    await this.store.put('upload', record)
    return record
  }

  async enqueueSubmit(deliveryId: string): Promise<QueuedSubmit> {
    const record: QueuedSubmit = { id: newKey(), deliveryId, createdAt: this.now().toISOString(), attempts: 0, error: null }
    await this.store.put('submit', record)
    return record
  }

  async commands(): Promise<QueuedCommand[]> {
    return (await this.store.list<QueuedCommand>('command')).sort((a, b) => a.createdAt.localeCompare(b.createdAt))
  }

  /** Everything still on the device that the server has not accepted. */
  async pendingCount(): Promise<number> {
    const commands = (await this.store.list<QueuedCommand>('command')).filter((c) => c.status !== 'Synced')
    return commands.length + (await this.store.list('upload')).length + (await this.store.list('submit')).length
  }

  /** Sends what is waiting. Safe to call at any time and from several places: concurrent calls share one run. */
  flush(): Promise<FlushSummary> {
    this.flushing ??= this.run().finally(() => {
      this.flushing = null
    })
    return this.flushing
  }

  private async run(): Promise<FlushSummary> {
    const summary: FlushSummary = { reachable: true, synced: 0, conflicts: 0, failed: 0, uploaded: 0, submitted: 0 }
    try {
      await this.sendCommands(summary)
      await this.sendUploads(summary)
      await this.sendSubmits(summary)
    } catch (e) {
      if (!isOffline(e)) throw e
      summary.reachable = false
    }

    return summary
  }

  private async sendCommands(summary: FlushSummary): Promise<void> {
    const waiting = (await this.commands()).filter((c) => c.status === 'Pending' || (c.status === 'Failed' && c.attempts < MAX_AUTOMATIC_ATTEMPTS))
    for (let i = 0; i < waiting.length; i += MAX_BATCH) {
      const batch = waiting.slice(i, i + MAX_BATCH)
      const commands: SyncCommand[] = batch.map((c) => ({
        clientRecordId: c.id, type: c.type, deliveryId: c.deliveryId, clientCreatedAt: c.createdAt, clientUpdatedAt: c.createdAt, payload: c.payload,
      }))
      const { results } = await this.api.sync(this.device, commands) // a network failure leaves them pending, exactly as they were
      for (const result of results) {
        const command = batch.find((c) => c.id === result.clientRecordId)
        if (!command) continue
        command.attempts = result.attempt || command.attempts + 1
        command.status = result.status
        command.error = result.error
        command.errorCode = result.errorCode
        command.podId = result.podId ?? command.podId
        await this.store.put('command', command)
        if (result.status === 'Synced') summary.synced++
        else if (result.status === 'Conflict') summary.conflicts++
        else summary.failed++
      }
    }
  }

  private async podIdFor(deliveryId: string): Promise<string | null> {
    const completed = (await this.commands()).find((c) => c.deliveryId === deliveryId && c.type === 'complete' && c.status === 'Synced' && c.podId)
    return completed?.podId ?? null
  }

  private async sendUploads(summary: FlushSummary): Promise<void> {
    const uploads = (await this.store.list<QueuedUpload>('upload')).sort((a, b) => a.createdAt.localeCompare(b.createdAt))
    for (const upload of uploads) {
      const podId = await this.podIdFor(upload.deliveryId)
      if (!podId) continue // the delivery has not been completed on the server yet
      try {
        if (upload.kind === 'signature') await this.api.addSignature(podId, upload.blob, upload.signerName ?? '')
        else await this.api.addEvidence(podId, upload.blob, upload.evidenceType ?? 'PackagePhoto', upload.fix, upload.id, this.device)
        await this.store.remove('upload', upload.id)
        summary.uploaded++
      } catch (e) {
        if (isOffline(e)) throw e
        upload.attempts++
        upload.error = errorText(e)
        await this.store.put('upload', upload)
        summary.failed++
      }
    }
  }

  private async sendSubmits(summary: FlushSummary): Promise<void> {
    const submits = await this.store.list<QueuedSubmit>('submit')
    const uploads = await this.store.list<QueuedUpload>('upload')
    for (const submit of submits) {
      const podId = await this.podIdFor(submit.deliveryId)
      if (!podId || uploads.some((u) => u.deliveryId === submit.deliveryId)) continue // evidence first
      try {
        await this.api.submitPod(podId)
        await this.store.remove('submit', submit.id)
        summary.submitted++
      } catch (e) {
        if (isOffline(e)) throw e
        submit.attempts++
        submit.error = errorText(e)
        await this.store.put('submit', submit)
        summary.failed++
      }
    }
  }

  /** Puts a failed or conflicting command back in the queue to be tried again (after the driver has fixed the cause). */
  async retry(id: string): Promise<void> {
    const command = (await this.store.list<QueuedCommand>('command')).find((c) => c.id === id)
    if (command) await this.store.put('command', { ...command, status: 'Pending', attempts: 0, error: null, errorCode: null })
  }

  async discard(id: string): Promise<void> {
    await this.store.remove('command', id)
  }
}

/** What a delivery looks like on the device once the driver's own unsent actions are applied: they have done it, whether or not the server knows yet. */
export function localStatus(serverStatus: DeliveryStatus, commands: QueuedCommand[]): DeliveryStatus {
  let status = serverStatus
  for (const c of commands.filter((x) => x.status !== 'Synced' && x.status !== 'Conflict')) {
    if (c.type === 'start' && status === 'Assigned') status = 'EnRoute'
    else if (c.type === 'arrive' && (status === 'EnRoute' || status === 'Attempted')) status = 'Arrived'
    else if (c.type === 'attempt' && status === 'Arrived') status = 'Attempted'
    else if (c.type === 'fail' && (status === 'Arrived' || status === 'Attempted')) status = 'Failed'
    else if (c.type === 'refuse' && (status === 'Arrived' || status === 'Attempted')) status = 'Refused'
    else if (c.type === 'complete' && (status === 'Arrived' || status === 'Attempted')) {
      const outcome = (c.payload as { outcome?: string })?.outcome
      status = outcome === 'Full' ? 'Delivered' : 'PartiallyDelivered'
    }
  }

  return status
}
