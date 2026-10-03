# Database migrations

`Tamiza.DbUp` owns the metadata schema `tamiza`. Every table, column, index and constraint in it comes from a SQL script in [`Scripts/`](Scripts/), applied by [DbUp](https://dbup.readthedocs.io/en/latest/). EF Core in `Tamiza.Api` only maps the schema; it never creates or changes it.

The API applies pending scripts on startup, before it reports healthy (unless `TAMIZA_MIGRATE_ON_STARTUP=false`). DbUp records each applied script in `tamiza.schema_versions` and never runs it again.

The per-project schemas (`p_<id>_raw`, `p_<id>_clean`, ...) are not managed here. Their tables depend on each project's form and are created at run time.

## Adding a migration

1. Add `Scripts/NNNN_short_description.sql`, numbered one above the highest existing script: four digits, then lowercase snake_case (for example `0002_add_runs.sql`). Scripts run in name order.
2. Write plain PostgreSQL:
   - Qualify every object with its schema: `tamiza.users`, not `users`.
   - Name tables and columns in snake_case, and name constraints and indexes the way EF Core's naming convention expects: `pk_<table>`, `fk_<table>_<principal>_<column>`, `ix_<table>_<columns>`, `ck_<table>_<rule>`.
   - Each script runs in its own transaction, so a failing script leaves nothing behind. Do not use statements that cannot run in a transaction, such as `CREATE INDEX CONCURRENTLY` or `VACUUM`.
   - DbUp variable substitution is off, so `$$` and `$body$` dollar quoting work as usual.
3. Update the entity and its mapping in `TamizaDbContext` to match.
4. Run `dotnet test`. `SchemaDriftTests` builds one database from the scripts and one from the EF model and lists every table, column, constraint or index that differs. A table that EF deliberately does not map goes in its `Unmapped` list.

## Rules

- **Never edit, rename or delete a script once it is merged.** DbUp tracks scripts by name only, so a changed script never reaches databases that already ran it. Fix mistakes with a new script.
- There are no down scripts. To undo a change, write a new script that reverses it.

## Running the scripts by hand

`Tamiza.DbUp` is also a console app. It takes the connection string as its first argument, or from `ConnectionStrings__Tamiza` like the API:

```sh
cd tamiza-api
dotnet run --project src/dbUp -- "Host=localhost;Port=5432;Database=tamiza;Username=tamiza;Password=..."
```

It exits with 0 when the schema is up to date and 1 when a script fails.

## Databases created by EF Core migrations

Before DbUp, the schema came from EF Core migrations. On its first run against such a database, the migrator checks the EF history. If the database is at the last EF migration (`20261003010233_AddProjects`), it records `0001_initial_schema.sql` as applied without running it and drops `tamiza.__ef_migrations_history`. A database at an older EF migration is refused and must be recreated.
