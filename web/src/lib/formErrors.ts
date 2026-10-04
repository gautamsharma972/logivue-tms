import type { FormInstance } from 'antd'
import type { ApiError } from '@/lib/api/errors'

/**
 * Shows server-side validation messages under the matching form fields. Returns true if any were applied.
 * @param rename maps a server field name to the form field that edits it (e.g. effectiveTo → period).
 */
export function applyFieldErrors(form: FormInstance, error: ApiError, rename: Record<string, string> = {}): boolean {
  const entries = Object.entries(error.fieldErrors)
  if (entries.length === 0) return false
  form.setFields(entries.map(([name, errors]) => ({ name: (rename[name] ?? name).split('.'), errors })))
  return true
}
