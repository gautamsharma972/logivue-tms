import { describe, expect, it } from 'vitest'
import { codeFromQr } from './QrScanner'

describe('codeFromQr', () => {
  it('takes the delivery code from bare digits or from text around them', () => {
    expect(codeFromQr('482913')).toBe('482913')
    expect(codeFromQr('TMS-DELIVERY code=482913 for DLV-00042')).toBe('482913')
  })

  it('does not mistake a longer number or no number for a code', () => {
    expect(codeFromQr('https://example.test/track')).toBeNull()
    expect(codeFromQr('1234567890123')).toBeNull()
  })
})
