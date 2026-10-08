# Reports & Analytics: module integration

Reports reads the other modules **only** through the reporting contracts in `Tms.SharedKernel.Contracts` (`Reporting.cs`). It references no other module, copies no transaction data, and has no foreign key to another module's table (the architecture tests enforce the first). All five modules are merged in this repository, each with a provider; the demonstration provider stands in for any that is absent, so the module also runs alone.

## The contract

A provider returns flat, read-only **facts** for a `ReportingWindow(From, To, TransporterId?)`. The window's company is a hint to read less; Reports filters and limits again, so ignoring it is safe. A value a module cannot supply is `null` and Reports reads it as *not measurable*, never zero.

| Contract | Implemented by | Facts | Source of truth for |
|---|---|---|---|
| `IPlanningReportingProvider` | Shipments (`ShipmentsReportingProvider`) | `ShipmentReportFact`, `PlanningRunFact`, `PlanVehicleFact`, `UnplannedOrderFact`, `TenderFact` | Plans, vehicle utilisation, planned cost, consolidation and planning savings, tenders, shipment timestamps |
| `ITransporterReportingProvider` | Transporters (`TransportersReportingProvider`) | `TransporterFact`, `ScorecardFact`, `PlacementFact`, `ExecutionFact`, `ExceptionFact` | Scorecards and their parts, placements, planned/actual pickup with delay attribution, transporter alerts |
| `IPodReportingProvider` | Deliveries (`DeliveriesReportingProvider`) | `DeliveryFact`, `PodFact`, `DiscrepancyFact`, `ExceptionFact` | Delivery status and on-time judgement, proof status and SLA test, shortage and damage, delivery exceptions |
| `ITrackingReportingProvider` | Tracking (`TrackingReportingProvider`) | `TrackFact`, `DeviationFact`, `DwellFact`, `GapFact`, `ExceptionFact` | Tracking health, risk, ETAs, distance, dwell, deviations, gaps, tracking exceptions |
| `IFreightContractReportingProvider` | Contracts (`ContractsReportingProvider`) | `ContractFact`, `RateFact`, `DphFact`, `RatingFact`, `CoverageFact` | Contracts and renewal, rates and slabs, diesel clauses, kept ratings |

`ExecutionFact` lets Transporters fill what Planning cannot know (the planned pickup time and who a late pickup belongs to); `ReportFacts.Shipments()` merges it by shipment reference.

## Reference identifiers

Facts are joined only by references, never by internal ids of other modules: `ShipmentRef` (shipment number), `TripRef`, `TransporterId` and its name, `VehicleRef` (registration), customer name, `ContractRef` (contract number and revision), `RunRef`, `DeliveryRef`, `PodRef`. **Shipment 360** is built by joining every provider's facts on `ShipmentRef` for one shipment.

## Cross-module data model

No reporting table copies a transaction. The only persistent reporting data are the module's own `rpt_*` tables: definitions (`report_definitions`, `report_columns`, `report_filters`, `report_groupings`, `report_sorts`), `kpi_definitions`, `report_jobs`, `report_subscriptions`, `report_audit`, `report_settings`, `user_preferences`, `data_scopes` and the summary table `daily_transport_kpi` (numerator and denominator per KPI per day). Historical values are the owning module's: ratings and scorecards are read as they were kept, so a changed contract or master never alters a past figure.

## How a provider is chosen

`ReportingProviderResolver` uses, per contract, the provider the owning module registered; if there is none (module not merged) or the organisation chose **Demo** in Report settings, `DemoReportingData` (the local demonstration provider, `LocalDemoDataProvider` in the brief) serves that contract. `GET /api/v1/reports/data-source` says which is which, every report result says `Live`, `Demo` or `Mixed`, and the web page shows a banner for demonstration data. `AddReportsModule` is registered **last** in `Program.cs`.

## Writing or changing a provider

1. Implement the contract inside the owning module (`Integration/…`), using its own `DbContext`, with projections (no entity graphs sent out) and the module's own rules for every status and judgement.
2. Register it in the module's `Add<Module>Module` (`services.AddScoped<IPlanningReportingProvider, …>()`).
3. Read only what you own; for another module's names (transporter, vehicle type) use the existing directories (`ITransporterDirectory`, `IVehicleTypeDirectory`).
4. Do not run two queries at once on one `DbContext`: the engine awaits providers one after another.
5. Return `null`, not `0`, for what you do not know. A status string must be one of those listed in `Reporting.cs`.
6. Add the case to `ReportsApiTests.Every_report_also_runs_against_the_real_modules_data…`.

## Merge instructions

Files this module **adds**: everything under `backend/src/Modules/Tms.Modules.Reports`, `Tms.SharedKernel/Contracts/Reporting.cs`, `Tms.SharedKernel/India/IndiaRegions.cs`, the five `Integration/*ReportingProvider.cs` files, `web/src/features/reports`, tests under `Reports` / `ReportsApiTests.cs`, and the three docs.

Shared files **modified** (each a few lines):

| File | Change |
|---|---|
| `backend/Tms.slnx`, `src/Tms.Api/Tms.Api.csproj`, three test `.csproj` | project reference |
| `src/Tms.Api/Program.cs` | `AddReportsModule` (last), `MapReportsEndpoints`, `InitialiseReportsAsync`, a health check |
| `Shipments/ShipmentsModule.cs`, `Transporters/TransportersModule.cs`, `Deliveries/DeliveriesModule.cs`, `Tracking/TrackingModule.cs`, `Contracts/ContractsModule.cs` | one registration line each |
| `tests/Tms.ArchitectureTests/ModuleBoundaryTests.cs` | the new assembly in the boundary rules |
| `tests/Tms.IntegrationTests/Infrastructure/TmsApiFactory.cs` | four `Reports:*` test settings |
| `web/src/App.tsx`, `web/src/layouts/navigation.tsx`, `web/src/lib/api/queryKeys.ts` | routes, menu, query keys |

Nothing in another module's domain, handlers, tables or migrations was changed. If another branch adds a provider with the same contract, keep one registration; the resolver takes the first.

After merging: `dotnet ef migrations` is not needed (migration `ReportsInitial` is committed); start the API with `Database:MigrateOnStartup` on, sign in, open **Reports & Analytics**. Grant `reports.*` permissions to roles (the Administrator role has them); give a transporter's external role `reports.self`. Optional configuration: `Reports:PublicBaseUrl` (links in emails), `Reports:WorkerPollSeconds`, `Reports:SchedulerPollSeconds`, `Reports:AggregationEnabled`, `Reports:AggregationIntervalMinutes`, `Reports:SummaryDays`.
