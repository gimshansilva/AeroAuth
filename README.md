# AeroAuth: Phase 2, database and data access layer

SQL Server schema, seed data and a layered .NET 8 solution (Dapper) for the QA Authorization Management Portal.

## Structure

```
AeroAuth.sln
database/
  schema.sql      tables, constraints, indexes, triggers, stored procedure, reference data (re-runnable)
  seed.sql        two organizations, stations, users, reference values, training material, counters, test staff
  verify.sql      runs 14 checks against the database rules (rolls back, changes nothing)
src/
  AeroAuth.Core            entities, constants, interfaces (repositories, IUnitOfWork, ITenantContext), services
  AeroAuth.Infrastructure  connection factory, unit of work, base repositories, Dapper repositories, DI registration
  AeroAuth.Api             controllers, JWT sign-in (dev), error handling
```

## Run it (VS Code)

1. Install the .NET 8 SDK and SQL Server (LocalDB, Developer or Express).
2. Run `database/schema.sql`, then `database/seed.sql`, then `database/verify.sql` (every row should say PASS). Use the SQL Server extension, SSMS, or `sqlcmd -S "(localdb)\MSSQLLocalDB" -i database/schema.sql` for each file.
3. Check `ConnectionStrings:AeroAuth` in `src/AeroAuth.Api/appsettings.json`.
4. Set your own signing key: `cd src/AeroAuth.Api` then `dotnet user-secrets init` and `dotnet user-secrets set "Jwt:Key" "<long random value>"`.
5. Press F5. Swagger opens at http://localhost:5080/swagger. Use `src/AeroAuth.Api/AeroAuth.Api.http` (REST Client extension) to try each step.

Seed sign-ins: `admin@aeroauth.local` (Admin QA), `viewer@aeroauth.local` (Internal QA), `kac.user@aeroauth.local` (KAC), `ramco.user@aeroauth.local` (RAMCO).
The `.http` file uses ids 1 and 2 for the seeded trainers. If your ids differ, look them up with `GET /api/staff`.

## Step 1 and 2: database

- 20 tables. Master: Organizations, Stations, Users, Staff, StaffAml, StaffScope. Workflow: TrainingMaterials, TrainingSessions, SessionModules, SessionAttendees, AttendeeModuleAcknowledgments, Certificates, ReviewQueue. Audit and admin: CaaslApprovals, RemarksLog, AdminActionsLog, SystemCounters. Reference lists: IssuingNaas (CAASL, GACA, EASA, FAA, DGCA, GCAA, PCAA), AircraftTypes, FreezeReasons (three separate tables with an IsActive flag).
- Tenant isolation in the schema: Staff, TrainingSessions, SessionAttendees, Certificates and ReviewQueue carry `OrganizationId`. Composite foreign keys make the database refuse a station, trainer, session or attendee from another organization.
- Scope rule: an AMLEndorsed row must link to an AML of the same staff member and the NAA that issued it. A MOETaskBased row must have neither.
- Review rule: Pending needs no reviewer, Approved or Rejected needs a reviewer and time. Only one open review item per record.
- Audit: `AdminActionsLog` and `RemarksLog` are append-only (triggers and a DENY for the application role). A Freeze entry needs a staff member and a freeze reason.
- Counters: `usp_GetNextCounter` returns the next number safely under concurrency.
- Reference tables are plural (`IssuingNaas`, `AircraftTypes`, `FreezeReasons`). `Users` and `Certificates` are extra tables the brief did not list: sign-in needs the first, and "generating certificate records" needs the second.

## Step 3 and 4: data access

- **Layers:** Core has no database code. Infrastructure holds all Dapper and SQL. Api only calls interfaces.
- **Transactions:** `IUnitOfWork` (scoped) owns one connection and one `IDbTransaction`. Every repository takes the connection and transaction from it through `BaseRepository`, so any repository call inside `uow.ExecuteInTransactionAsync(...)` is part of the same transaction. Success commits, any exception rolls back, and a nested call joins the running transaction.
  `TrainingApprovalService.ApproveAsync` is the example: close the review queue item, mark the session approved, take a counter number for each attendee and insert the certificate records, all in one transaction. If any step fails, nothing is saved and the counter numbers go back too.
- **Tenant scoping:** `TenantRepository` adds `(@TenantId IS NULL OR alias.OrganizationId = @TenantId)` to every tenant query. The value comes from `ITenantContext.ResolveScope()`, which reads the signed-in user's token: an External AMO user always gets their own organization, internal QA gets null (all). It never comes from the request. Writes call `EnsureWritable`, and updates and deletes carry the same filter. A record of another organization is simply "not found" (404).
- **Multi-mapping:** `StaffRepository.GetWithDetailsAsync` loads a staff member with `StaffAml` and `StaffScope` in one query using `QueryAsync<Staff, StaffAml, StaffScope, Staff>` with `splitOn: "StaffAmlId,ScopeId"`. Two LEFT JOINs multiply the rows, so the mapping function adds each child once.
- **Staff CRUD (`StaffRepository`):** `GetByIdAsync`, `ListAsync`, `SearchAsync`, `GetWithDetailsAsync`, `CreateAsync`, `UpdateAsync`, `DeactivateAsync` (soft delete, because licenses, training and the audit log refer to the person), `AddAmlAsync`, `AddScopeAsync`, `SetFrozenAsync`, `SetReviewDecisionAsync`.

## Not included yet

Certificate PDFs and emails, the scheduled CAASL expiry job, trainer nominations, certificate void and reissue endpoints, and automated tests. The schema already supports the void and reissue rules.
