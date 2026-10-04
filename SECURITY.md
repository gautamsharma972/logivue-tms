# Security

## Reporting a vulnerability
Email the maintainers privately; do not open a public issue. Include steps to reproduce and the affected version.

## Controls in place
- Tenant isolation enforced in the data layer (query filter + write guard), covered by integration tests.
- Passwords hashed with PBKDF2 (ASP.NET Core Identity v3 format); lockout after 5 failures; generic login errors.
- Short-lived access tokens; rotating, hashed, single-use refresh tokens with theft detection, delivered as an HttpOnly cookie.
- Role-based access with privilege-escalation checks; vendor-portal users are scoped to their own company.
- Uploads validated by file signature, size-limited, stored under generated keys.
- Sensitive financial fields (bank account numbers) encrypted at rest; audit trail never stores secrets.
- Dependency vulnerability scanning (NuGet and npm) runs in CI.

## Operational requirements
Secrets (JWT signing key, field-encryption keys, database and SMTP credentials) come from the environment or a secret
store, never from committed files. Rotate the encryption key by adding a new key id and re-encrypting.
