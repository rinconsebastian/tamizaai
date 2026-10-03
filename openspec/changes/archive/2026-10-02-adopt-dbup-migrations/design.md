# Design

## Context

See `proposal.md` for motivation. The requirement is the modified "Automatic metadata schema migrations" in `specs/deployment`.

Current state in `tamiza-api`:

- `TamizaDbContext` maps five tables in schema `tamiza` with `EFCore.NamingConventions` (snake_case columns, `pk_`/`fk_`/`ix_` names) and two explicit check constraints (`ck_project_members_role`, `ck_project_invitations_role`). Two `jsonb` columns per list property go through `JsonColumn`.
- Two EF migrations, `20261002214441_InitialCreate` and `20261003010233_AddProjects`, plus designer and snapshot files. History table `tamiza.__ef_migrations_history` with columns `migration_id` and `product_version` (checked on a running dev database, which is at `AddProjects`).
- `DatabaseRegistration.MigrateDatabaseAsync` runs `Database.MigrateAsync()` before `app.RunAsync()`, honors `TAMIZA_MIGRATE_ON_STARTUP`, sets `MigrationStatus` for the readiness check and logs `Database migration failed` (asserted by `HealthTests`) before the process exits with code 1.
- `DesignTimeDbContextFactory`, `Microsoft.EntityFrameworkCore.Design` and the `dotnet-ef` local tool exist only for generating migrations.
- Integration tests use one Testcontainers PostGIS container per run and one database per test.
- Latest packages checked on NuGet: `dbup-postgresql` 7.0.1 (depends on `dbup-core` 6.1.1 and `Npgsql` ≥ 10.0.1, the major version the EF provider already uses). `dbup-core` 6 accepts a `Microsoft.Extensions.Logging` `ILogger` directly.

## Goals / Non-Goals

**Goals:**

- One place for every metadata schema change: SQL files in `tamiza-api/src/dbUp/Scripts`.
- Keep EF Core as the data-access layer, with a test that fails when the `DbContext` mapping and the SQL schema disagree.
- Keep the startup contract (`MigrationStatus`, `TAMIZA_MIGRATE_ON_STARTUP`, exit on failure) so `compose.yaml`, the healthchecks and the operations guide stay valid.
- Upgrade the existing dev databases in place.

**Non-Goals:**

- Per-project schemas (`p_<id>_*`). Their DDL is generated at run time from each form; versioned scripts cannot describe it. When `add-kobo-ingestion` needs shared objects (for example a function or a `runs` table), those go in DbUp scripts.
- Down migrations, a migrations UI, or a separate migration container.
- Changing any table definition. The initial script reproduces today's schema exactly.

## Decisions

### D1. Project `Tamiza.DbUp` in `src/dbUp`, referenced by the API

`tamiza-api/src/dbUp/Tamiza.DbUp.csproj` (assembly and root namespace `Tamiza.DbUp`, following the `Tamiza.*` convention; the folder name is the one requested). It is a console app (`OutputType Exe`) with:

- `Scripts/*.sql` as embedded resources.
- `SchemaMigrator` (public): `Migrate(string connectionString, ILogger logger, CancellationToken cancellationToken)`. It throws `SchemaMigrationException` with DbUp's error when the upgrade fails.
- `Program.cs`: reads the connection string from the first argument or from `ConnectionStrings__Tamiza` (the variable the API already uses), logs to the console, and exits 0 on success, 1 on failure. This is DbUp's recommended console workflow, for developers and CI (`dotnet run --project src/dbUp -- "<connection string>"`).

Packages: `dbup-postgresql` 7.0.1, plus a `FrameworkReference` to `Microsoft.AspNetCore.App` for console logging. The shared framework rather than the `Microsoft.Extensions.Logging.Console` package, because the API's publish output takes logging from that framework; with the package, `dotnet Tamiza.DbUp.dll` cannot load it from the API image. `Tamiza.Api` gets a `ProjectReference` to it; the project is added to `Tamiza.sln` under `src`. The dependency goes one way: `Tamiza.DbUp` does not reference `Tamiza.Api` or EF Core.

Alternatives considered:
- *One-shot `migrate` service in Compose* (`api` depends on `service_completed_successfully`). Rejected: one more image and service, a change to the deployment topology and the four-service requirement, and nothing the startup step does not already give.
- *Class library only, no console.* Rejected: the user asked for a DbUp project and DbUp's model is a runnable app; the console costs one file.

### D2. Script conventions

- File name `NNNN_description.sql`: four-digit, zero-padded sequence, then lowercase snake_case. DbUp applies scripts in ordinal name order.
- Embedded with `LogicalName="%(Filename)%(Extension)"`, so the journal stores `0001_initial_schema.sql`, not `Tamiza.DbUp.Scripts.0001_initial_schema.sql`. Renaming the folder or namespace then cannot make DbUp re-run old scripts.
- A script is immutable once merged to `main`. DbUp journals names, not content, so an edit would never reach databases that already ran the script. Fixes go in a new script.
- Objects are always schema-qualified (`tamiza.users`). Names follow the existing convention so EF maps them without overrides: snake_case tables and columns, `pk_<table>`, `fk_<table>_<principal>_<column>`, `ix_<table>_<columns>`, `ck_<table>_<rule>`.
- No down scripts.
- A unit test enforces the file name pattern and that sequence numbers are unique (two branches adding `0005_*` fail CI after the second merge).

### D3. DbUp engine configuration

```csharp
DeployChanges.To
    .PostgresqlDatabase(connectionString, "tamiza")          // creates schema tamiza if missing, before scripts
    .JournalToPostgresqlTable("tamiza", "schema_versions")
    .WithScriptsEmbeddedInAssembly(typeof(SchemaMigrator).Assembly, name => name.EndsWith(".sql"))
    .WithTransactionPerScript()
    .WithVariablesDisabled()
    .LogTo(logger)
    .Build();
```

- **Journal `tamiza.schema_versions`**, next to the tables it describes, instead of DbUp's default `public.schemaversions`.
- **Transaction per script.** PostgreSQL DDL is transactional, so a failing script leaves nothing behind and is not journaled (the "Failed migration" scenario). Earlier scripts in the same run stay applied. Statements that cannot run in a transaction (`CREATE INDEX CONCURRENTLY`, `VACUUM`) are not allowed in scripts; if one is ever needed, that change decides how.
- **Variables disabled.** DbUp's `$name$` substitution would collide with PostgreSQL dollar quoting (`$body$`) in future function bodies.
- `PerformUpgrade()` returns a result instead of throwing; `SchemaMigrator` turns an unsuccessful result into `SchemaMigrationException`. DbUp's API is synchronous; it runs before the host starts, so blocking there is fine.

Alternative considered: *a single transaction for the whole run* (`WithTransaction()`). Rejected: one bad script would roll back the good ones before it, and a long upgrade would hold one large transaction.

### D4. One migrator at a time: advisory lock

`SchemaMigrator` opens its own Npgsql connection and takes `pg_advisory_lock(<fixed 64-bit key>)` before anything else, holding it until DbUp and the adoption step (D6) finish. A second process blocks on the lock, then finds nothing pending. The lock is session-scoped, so it is released if the process dies. Lock acquisition honors the cancellation token (startup cancels on shutdown).

Alternative considered: *`LOCK TABLE schema_versions`*. Rejected: the table does not exist on an empty database, which is exactly when two processes are most likely to race.

### D5. Initial script `0001_initial_schema.sql`

Generated, then cleaned by hand, before the EF migrations are deleted:

1. `dotnet ef migrations script 0 20261003010233_AddProjects` produces the exact SQL EF runs today.
2. Remove every `__ef_migrations_history` statement and the transaction wrappers (DbUp adds its own).
3. Fold `ALTER TABLE users ADD email_verified boolean NOT NULL DEFAULT FALSE` into `CREATE TABLE tamiza.users`.
4. Keep every object name, type, default, `ON DELETE` rule, check expression and index as generated. Start with `CREATE SCHEMA IF NOT EXISTS tamiza;` so the script also works when run alone with `psql`.

The drift test (D7) then proves the script matches the model.

### D6. Adopting databases created by EF Core

Inside the lock, before `PerformUpgrade()`:

| State | Action |
|---|---|
| `tamiza.schema_versions` exists | Normal upgrade. If `tamiza.__ef_migrations_history` is still there (an adoption interrupted after marking), drop it. |
| No journal, `__ef_migrations_history` contains `migration_id = '20261003010233_AddProjects'` | `engine.MarkAsExecuted("0001_initial_schema.sql")` (DbUp creates the journal and records the script without running it), then `DROP TABLE tamiza.__ef_migrations_history`, then normal upgrade. |
| No journal, history exists without `AddProjects` | Throw `SchemaMigrationException`: the database was created by an earlier development build and must be recreated (`docker compose down -v`, or drop schema `tamiza`). Nothing is changed. |
| Neither table | Fresh database: normal upgrade. |

`AddProjects` is the last EF migration and the schema it leaves is, by construction (D5, D7), the schema `0001` creates. No release ever shipped with EF migrations, so only development databases take this path; the code can be removed once none are left (see Open Questions).

Alternative considered: *idempotent `0001` (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`)*. Rejected: PostgreSQL has no `IF NOT EXISTS` for `ADD CONSTRAINT`, the script would grow conditional blocks for every constraint, and an EF database at `InitialCreate` would still need special handling.

### D7. EF Core keeps data access; a drift test keeps it honest

Removed: `Data/Migrations/` (both migrations, designers, snapshot), `DesignTimeDbContextFactory.cs`, the `MigrationsHistoryTable(...)` option in `DatabaseRegistration.Configure`, the `Microsoft.EntityFrameworkCore.Design` package and `.config/dotnet-tools.json` (its only tool is `dotnet-ef`). `TamizaDbContext`, entities, `JsonColumn` and the naming convention stay.

`MigrateDatabaseAsync` keeps its shape and messages; only `db.Database.MigrateAsync(...)` becomes `SchemaMigrator.Migrate(connectionString, logger, ct)`, with the connection string read the same way `AddTamizaDatabase` reads it.

Drift test: in the shared Postgres container, database A is built by `SchemaMigrator`, database B by `TamizaDbContext.Database.EnsureCreatedAsync()` with the API's `DatabaseRegistration.Configure`. A catalog snapshot of schema `tamiza` (journal excluded) must be equal:

- tables;
- columns: name, `data_type`, `character_maximum_length`, `is_nullable`, `column_default` (column order ignored);
- constraints by name with `pg_get_constraintdef` (primary, foreign, unique, check);
- indexes by name with `pg_get_indexdef`.

The test prints the differing entries. Workflow for later changes: write the next SQL script, update the entity and its mapping, run the test.

Alternative considered: *drop EF Core and use Dapper/raw Npgsql.* Rejected: out of scope; all endpoints use the `DbContext`, and DbUp does not require it.

### D8. Build and documentation

- `tamiza-api/Dockerfile`: copy `src/dbUp/Tamiza.DbUp.csproj` before `dotnet restore`; the existing `COPY src/ src/` and publish of `Tamiza.Api` bring in the rest. Referencing an `Exe` project from another framework-dependent `Exe` is supported by the SDK; the build task verifies the published API still starts.
- `tamiza-api/src/dbUp/README.md`: how to add a migration (D2 rules, the drift-test workflow, running the console against a local database). Linked from the README Development table.
- `docs/operations.md` Upgrade: the journal table, and that the first start of this version adopts an existing database once.
- `openspec/config.yaml` context and `docs/tamiza-brief.md`: "Database migrations with DbUp: versioned SQL scripts in `tamiza-api/src/dbUp`; EF Core is the data-access layer (`api` owns the metadata schema)."

### D9. Test strategy

| Level | What |
|---|---|
| Unit (`Tamiza.Api.UnitTests`) | Script file names match `^\d{4}_[a-z0-9_]+\.sql$`, sequence numbers unique, every embedded resource is a `.sql` script. |
| Integration, new `Database/SchemaMigratorTests.cs` | Empty database: every script journaled. Re-run: nothing applied, a seeded row survives. Concurrency: two parallel `Migrate` calls on one empty database both succeed and each script is journaled once. Failure: an engine built with the same configuration plus a script that creates a table then divides by zero leaves no table and no journal row (`InternalsVisibleTo` exposes the builder). Adoption: a database prepared with `0001`'s SQL, an EF history table holding both rows and a user row ends with `0001` journaled, no history table, the user row intact. Unsupported state: history holding only `InitialCreate` throws the "recreate" message and creates no journal. Drift (D7). |
| Existing integration | `DatabaseTests` (tables, check constraint, unique index, column types) and `HealthTests` (including `Database migration failed` on an unreachable database) pass unchanged. |

## Risks / Trade-offs

- [Someone edits a merged script] → It silently does nothing on existing databases. Mitigation: rule in the project README, script immutability called out in review; the drift test catches the case where the edit was meant to fix the model.
- [`DbContext` mapping and SQL diverge] → Runtime errors on queries. Mitigation: drift test (D7) runs in CI.
- [A future data migration is slow] → It runs in one transaction at startup and holds the advisory lock; the healthcheck's 60 s `start_period` may expire. Mitigation: acceptable at MVP data sizes; a change with a heavy data migration documents it or raises the start period.
- [Rolling back to a build with EF migrations after adoption] → That build sees no EF history and fails to start. Accepted: no release used EF migrations; dev databases can be recreated.
- [Exe referenced by Exe] → SDK edge cases on publish. Mitigation: the Docker build is part of the tasks' verification.

## Migration Plan

1. Generate and clean `0001_initial_schema.sql` from the EF migrations (D5) before deleting them.
2. Add `Tamiza.DbUp`, switch startup to it, delete EF migration artifacts, add tests.
3. Deploy as a normal upgrade (`docker compose up -d --build --wait`). The first start adopts the existing database (D6). Fresh installs run `0001`.

Rollback: restore the previous image together with a database backup taken before the upgrade, or recreate the dev database.

## Open Questions

- When to remove the EF adoption path (D6). Once every development database has started on this version, a later change can delete it together with its tests.
