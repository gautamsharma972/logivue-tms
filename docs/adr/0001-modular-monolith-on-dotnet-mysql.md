# ADR 0001 — Modular monolith on .NET + MySQL

**Status:** accepted · **Date:** 2026-10-03

## Context
Solo developer building a 17-module TMS for the Indian market, SaaS, multi-tenant. Customer ecosystems (SAP, Dynamics,
Tally) skew Microsoft. MySQL is the chosen database.

## Decision
- One deployable **modular monolith**: a project per business module with enforced boundaries (architecture tests),
  in-process domain events. Extract a module to a service only when load or team size demands it.
- **.NET 10 LTS + ASP.NET Core minimal APIs**, EF Core 9 with Pomelo for MySQL 8.4.
- **Multi-tenancy** by `tenant_id` column + EF global query filter + write guard (MySQL has no row-level security).
- **No MediatR** (commercial licence); handlers are plain classes injected into endpoints.
- **Auth:** ASP.NET password hashing + our own JWT access tokens (15 min) and rotating, hashed refresh tokens with
  reuse detection. Permissions are claims in the access token, so role changes apply on next refresh.
- **React + Ant Design** for the web UI; mobile apps deferred (module 12).

## Consequences
- Tenant isolation is application-enforced, so it is covered by integration tests against real MySQL.
- EF Core is held at 9.x until Pomelo supports EF Core 10.
- Permission claims make tokens grow with the catalogue; revisit (server-side permission cache) past ~100 permissions.
- Refresh token currently lives in `localStorage`; move to an HttpOnly same-site cookie before production.
