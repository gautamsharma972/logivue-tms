import type { Rule } from 'antd/es/form'

/** Mirrors the server's password policy so users get feedback while typing; the server remains the authority. */
export const passwordRules: Rule[] = [
  { required: true, message: 'Enter a password' },
  { min: 12, message: 'At least 12 characters' },
  { pattern: /[A-Z]/, message: 'Include an upper-case letter' },
  { pattern: /[a-z]/, message: 'Include a lower-case letter' },
  { pattern: /[0-9]/, message: 'Include a digit' },
]

export const PASSWORD_HINT = 'At least 12 characters, with upper-case, lower-case and a digit.'
