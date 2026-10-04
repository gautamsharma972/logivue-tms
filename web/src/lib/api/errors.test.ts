import { AxiosError, type AxiosResponse } from 'axios'
import { describe, expect, it } from 'vitest'
import { ApiError, toApiError } from './errors'

function axiosError(status: number | null, data?: unknown) {
  const error = new AxiosError('failed')
  if (status !== null) error.response = { status, data } as AxiosResponse
  return error
}

describe('toApiError', () => {
  it('maps an RFC 9457 validation problem to field errors', () => {
    const error = toApiError(
      axiosError(400, { title: 'Invalid', code: 'validation.failed', errors: { password: ['Too short'] }, traceId: 'abc' }),
    )

    expect(error).toBeInstanceOf(ApiError)
    expect(error.isValidation).toBe(true)
    expect(error.fieldErrors).toEqual({ password: ['Too short'] })
    expect(error.traceId).toBe('abc')
  })

  it('prefers the problem detail as the user-facing message and keeps the machine code', () => {
    const error = toApiError(axiosError(409, { title: 'Conflict', detail: 'Email already used', code: 'users.email_taken' }))

    expect(error.message).toBe('Email already used')
    expect(error.code).toBe('users.email_taken')
    expect(error.status).toBe(409)
  })

  it('reports an unreachable server distinctly from an HTTP failure', () => {
    const error = toApiError(axiosError(null))

    expect(error.code).toBe('network.unreachable')
    expect(error.status).toBe(0)
  })

  it('passes an existing ApiError through untouched', () => {
    const original = new ApiError('x', 500, 'server.error')
    expect(toApiError(original)).toBe(original)
  })
})
