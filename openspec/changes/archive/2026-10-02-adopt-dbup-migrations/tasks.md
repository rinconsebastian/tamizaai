# Tasks

## 1. DbUp project and initial script

- [x] 1.1 While the EF tooling still exists, run `dotnet ef migrations script 0 20261003010233_AddProjects` and turn the output into `tamiza-api/src/dbUp/Scripts/0001_initial_schema.sql` per design D5 (no history-table statements or transaction wrappers, `email_verified` folded into `CREATE TABLE tamiza.users`, `CREATE SCHEMA IF NOT EXISTS tamiza` first, every name, type, default, `ON DELETE` rule, check and index kept); verify by running it with `psql` on an empty database and comparing `\d tamiza.*` with a database migrated by the current EF code
- [x] 1.2 Create `tamiza-api/src/dbUp/Tamiza.DbUp.csproj` (console app, root namespace `Tamiza.DbUp`, `dbup-postgresql` 7.0.1, `FrameworkReference` to `Microsoft.AspNetCore.App` for console logging, `Scripts/*.sql` embedded with `LogicalName="%(Filename)%(Extension)"`) and add it to `Tamiza.sln` under `src`; verify `dotnet build` of the solution succeeds and the built assembly lists `0001_initial_schema.sql` as a manifest resource name
- [x] 1.3 Implement `SchemaMigrator.Migrate(connectionString, logger, cancellationToken)` with the engine configuration of design D3 (schema `tamiza`, journal `tamiza.schema_versions`, transaction per script, variables disabled, `ILogger`), wrapping an unsuccessful `PerformUpgrade()` in `SchemaMigrationException`, and expose the engine builder to the integration tests through `InternalsVisibleTo`; verify an integration test migrates an empty database and finds `0001_initial_schema.sql` in the journal
- [x] 1.4 Take a session advisory lock on a dedicated connection around the whole migration (design D4), honoring the cancellation token; verify an integration test running two `Migrate` calls in parallel on one empty database: both succeed and the journal has exactly one row per script
- [x] 1.5 Implement the EF adoption step of design D6 (journal present → drop leftover history; history at `AddProjects` → `MarkAsExecuted("0001_initial_schema.sql")` then drop the history table; older history → `SchemaMigrationException` saying the database must be recreated, nothing changed; neither → normal upgrade); verify integration tests: a database prepared with `0001`'s SQL, an EF history table holding both migration ids and a user row ends with `0001` journaled, no history table and the user row intact; a history holding only `InitialCreate` throws the recreate message and leaves no journal table
- [x] 1.6 Add `Program.cs` for the console runner (connection string from the first argument or `ConnectionStrings__Tamiza`, console logging, exit 0 on success and 1 on failure); verify `dotnet run --project src/dbUp -- "<connection string>"` migrates a local empty database and a second run reports no scripts to execute

## 2. API switches to DbUp

- [x] 2.1 Reference `Tamiza.DbUp` from `Tamiza.Api` and replace `db.Database.MigrateAsync(...)` in `DatabaseRegistration.MigrateDatabaseAsync` with `SchemaMigrator.Migrate(...)`, keeping `TAMIZA_MIGRATE_ON_STARTUP`, `MigrationStatus` and the `Database migration failed` critical log; verify the existing `DatabaseTests` and `HealthTests` pass unchanged
- [x] 2.2 Delete `Data/Migrations/`, `DesignTimeDbContextFactory.cs`, the `MigrationsHistoryTable(...)` option, the `Microsoft.EntityFrameworkCore.Design` package reference and `.config/dotnet-tools.json`; verify `dotnet build` and `dotnet test` pass and `grep -rn "EntityFrameworkCore.Design\|MigrationsHistoryTable\|dotnet-ef" tamiza-api` finds nothing
- [x] 2.3 Update `tamiza-api/Dockerfile` to copy `src/dbUp/Tamiza.DbUp.csproj` before `dotnet restore`; verify `docker compose build api` succeeds and `docker compose up -d --wait` brings `api` to healthy on a fresh volume

## 3. Tests

- [x] 3.1 Add the unit test for script conventions (design D2/D9: name pattern `^\d{4}_[a-z0-9_]+\.sql$`, unique sequence numbers, only `.sql` resources); verify it passes, and fails when a temporary `0001_duplicate.sql` is added
- [x] 3.2 Add the failure test: an engine with the production configuration plus a script that creates a table and then divides by zero; verify the upgrade reports failure, the table does not exist and the failing script is not in the journal, while `0001` stays applied
- [x] 3.3 Add the re-run test: migrate, insert a user, migrate again; verify no script is executed the second time and the user row is unchanged
- [x] 3.4 Add the drift test of design D7 (database built by `SchemaMigrator` vs. one built by `EnsureCreatedAsync()` with `DatabaseRegistration.Configure`; tables, columns, constraints and indexes compared by catalog, journal excluded, differences printed); verify it passes, and fails with a readable diff when a temporary `HasMaxLength` change is made to `User.Name`

## 4. Documentation and conventions

- [x] 4.1 Write `tamiza-api/src/dbUp/README.md` (naming, immutability, schema-qualified names and constraint naming, no non-transactional statements, updating the entity mapping and running the drift test, using the console runner) and link it from the README Development table; verify the links resolve
- [x] 4.2 Update `docs/operations.md` (Upgrade: migration journal `tamiza.schema_versions`, one-time adoption of an existing database, the recreate message for older development databases) and the `api` row in the README architecture table if its wording changes; verify the described commands match `compose.yaml`
- [x] 4.3 Replace "Database migrations with EF Core" in `openspec/config.yaml` and `docs/tamiza-brief.md` with the DbUp convention of design D8; verify `openspec validate adopt-dbup-migrations --strict` passes and `grep -rn "migrations with EF Core"` finds nothing outside archived changes

## 5. End-to-end check

- [x] 5.1 Upgrade the running dev stack (database at EF `AddProjects`) with `docker compose up -d --build --wait`; verify `api` is healthy, `tamiza.schema_versions` holds `0001_initial_schema.sql`, `tamiza.__ef_migrations_history` is gone, and the existing projects are still listed in the UI
