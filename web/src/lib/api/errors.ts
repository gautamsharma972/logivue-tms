import axios from 'axios'

/** A failed API call, normalised from an RFC 9457 problem-details body. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly fieldErrors: Record<string, string[]>
  readonly traceId: string | undefined

  constructor(message: string, status: number, code: string, fieldErrors: Record<string, string[]> = {}, traceId?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
    this.traceId = traceId
  }

  get isValidation() {
    return this.status === 400 && Object.keys(this.fieldErrors).length > 0
  }
}

interface ProblemBody {
  title?: string
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (axios.isAxiosError<ProblemBody>(error)) {
    if (!error.response) {
      return new ApiError('Cannot reach the server. Check your connection and try again.', 0, 'network.unreachable')
    }
    const { status, data } = error.response
    return new ApiError(
      data?.detail ?? data?.title ?? `Request failed (${status}).`,
      status,
      data?.code ?? `http.${status}`,
      data?.errors ?? {},
      data?.traceId,
    )
  }

  return new ApiError(error instanceof Error ? error.message : 'Unexpected error.', 0, 'client.error')
}
