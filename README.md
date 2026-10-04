# Transport Management System (TMS)

Multi-tenant SaaS TMS for shippers in India: planning, transporters, freight procurement, contracts, tracking,
POD, freight billing & audit, and claims. See [docs/roadmap.md](docs/roadmap.md) for scope and status.

| Part | Stack |
|---|---|
| `backend/` | .NET 10, ASP.NET Core minimal APIs, EF Core 9 + Pomelo, MySQL 8.4 — modular monolith |
| `web/` | React 19, TypeScript (strict), Vite, Ant Design 6, TanStack Query |

## Run locally

Prerequisites: .NET 10 SDK, Node 22+, a local MySQL 8.4+.

```bash
# 1. Database (once)
mysql -uroot -e "
  CREATE DATABASE tms_dev CHARACTER SET utf8mb4;
  CREATE DATABASE tms_test CHARACTER SET utf8mb4;
  CREATE USER 'tms_app'@'localhost' IDENTIFIED BY 'tms_dev_password';
  GRANT ALL ON tms_dev.* TO 'tms_app'@'localhost';
  GRANT ALL ON tms_test.* TO 'tms_app'@'localhost';"

# 2. API  (migrates the DB and seeds a demo tenant in Development)
cd backend && ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Tms.Api --urls http://localhost:5080
#    API explorer: http://localhost:5080/scalar

# 3. Web
cd web && npm install && npm run dev      # http://localhost:5173
```

Uploaded files go to `backend/src/Tms.Api/storage` in development (`Storage:RootPath`; swap `IFileStore` for cloud storage in production).

Create a customer organisation (operator command; prints a single-use set-password link and emails it):

```bash
cd backend && dotnet run --project src/Tms.Api --no-launch-profile -- tenant:create \
  --code ACME --name "Acme Freight Ltd" --admin-email admin@acme.com --admin-name "A. Admin"
```

Configuration that must come from the environment/secret store outside development: `Jwt__SigningKey`,
`FieldEncryption__Keys__1` (base64, 32 bytes), `ConnectionStrings__Default`, `Email__Provider=Smtp` with `Email__Host/User/Password`,
and `Email__AppBaseUrl`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export traces and metrics.

Demo login (development only): organisation `DEMO`, `admin@demo.tms` / `Admin@12345678`.

## Test

```bash
cd backend && dotnet test --solution Tms.slnx   # unit + architecture + integration (needs tms_test DB)
cd web && npm test && npm run typecheck && npm run lint
```

Override the integration-test database with `TMS_TEST_CONNECTION`.

## Migrations

```bash
cd backend
dotnet ef migrations add <Name> --project src/Modules/Tms.Modules.Platform --context PlatformDbContext \
  --output-dir Infrastructure/Persistence/Migrations
```

Production does not migrate at startup (`Database:MigrateOnStartup` is false); apply migrations as a deploy step.

## Planning demo data

With the API running and a development tenant (default `DEMO`), load a realistic planning scenario:

```bash
node tools/seed-planning-demo.mjs
```

It is safe to re-run and only for development tenants (it switches off the approval steps for demo carriers and contracts).
See `docs/planning-module.md` for what it creates and how to read the results, and for running OSRM for road distances
(`Routing__OsrmBaseUrl`); without it, distances are labelled estimates.

Plans created from the web app are calculated in a background worker (`Planning:WorkerEnabled`, default on) so a large run shows progress instead of blocking; plans and the KPI dashboard export to CSV, Excel and PDF.
