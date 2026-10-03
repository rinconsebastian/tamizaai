# Proposal

## Why

The metadata schema is defined today by EF Core migrations generated from the C# model. The SQL that reaches the database is a by-product of the model, the project has to carry the generated designer and snapshot files, and the `analytics` service, which reads and writes the same database, has no SQL source of truth to look at. The team wants every schema change, now and in every later change (runs queue, ingestion, audit log), to be a plain, reviewed SQL script applied by [DbUp](https://dbup.readthedocs.io/en/latest/). Switching now, with two EF migrations and no release yet, is the cheapest point to do it.

## What Changes

- **New migrations project `Tamiza.DbUp` in `tamiza-api/src/dbUp`.** It holds the SQL scripts as embedded resources and the code that applies them with DbUp (`dbup-postgresql`). It is also a console app, so a developer can migrate a database without starting the API.
- **Initial script `0001_initial_schema.sql`.** Creates schema `tamiza` with `users`, `projects`, `project_members`, `project_invitations` and `sampling_frames` exactly as the current EF Core migrations leave them: same columns, types, defaults, keys, foreign keys, check constraints, indexes and object names.
- **The API applies DbUp scripts on startup** instead of calling EF Core `Migrate`. Startup behavior stays as specified: pending scripts run before `api` reports healthy, a failure is logged and the process exits, and `TAMIZA_MIGRATE_ON_STARTUP=false` still skips the step. Each script runs in its own transaction, scripts are applied once and in name order, and a database lock keeps two processes from migrating at the same time.
- **Existing databases are adopted, not rebuilt.** A database whose EF Core history shows the latest EF migration (`AddProjects`) gets the initial script recorded as applied without running it, and the EF history table is dropped. A database at an older EF state makes startup fail with a message that says how to recreate it.
- **EF Core migrations are removed.** The `Data/Migrations` folder, the design-time factory, `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` tool go away. EF Core stays as the data-access layer; the `DbContext` mapping must match the SQL scripts, and a test fails when they drift apart.
- **Project convention updated.** "Database migrations with EF Core" becomes "Database migrations with DbUp SQL scripts" in the project context and the brief.

Not in this change: the per-project schemas (`p_<id>_raw`, `p_<id>_clean`, `p_<id>_output`, `p_<id>_indicators`). Their tables depend on each project's Kobo form and are created at run time by `api` and `analytics`, not by versioned scripts. Down migrations are also out: DbUp only moves forward, and a fix ships as a new script.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `deployment`: the "Automatic metadata schema migrations" requirement gains the rules operators can observe: each migration applied once, in order and atomically; an already current database starts without changes; concurrent starts do not apply a migration twice; a database created by the earlier EF Core migrations is upgraded in place.

## Impact

- **Code:** new project `tamiza-api/src/dbUp/Tamiza.DbUp.csproj` (added to `Tamiza.sln`, referenced by `Tamiza.Api`). `DatabaseRegistration.MigrateDatabaseAsync` calls DbUp. `Data/Migrations/`, `DesignTimeDbContextFactory.cs` and the EF migrations history table configuration are deleted.
- **Dependencies:** adds `dbup-postgresql` (MIT; brings `dbup-core`, uses the Npgsql 10 the API already has). Removes `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` local tool.
- **Database:** new journal table `tamiza.schema_versions`; `tamiza.__ef_migrations_history` is dropped on existing databases. Table definitions do not change.
- **Build:** the `tamiza-api` Dockerfile restores and publishes the new project.
- **Tests:** integration tests for the journal, re-runs, concurrent runs, EF adoption and model/schema drift; the existing database and health tests keep passing.
- **Docs:** `docs/operations.md` (upgrade note), a short "how to add a migration" section for contributors, `openspec/config.yaml` and `docs/tamiza-brief.md` conventions.
