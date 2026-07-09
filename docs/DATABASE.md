# Database Migrations

AutoDev Runner uses PostgreSQL through EF Core migrations. Startup no longer
uses `EnsureCreated`; every schema change must be represented by a checked-in
migration under `src/AutoDevRunner/Migrations/`.

## Tooling

Install the EF CLI once on a development machine:

```powershell
dotnet tool install --global dotnet-ef
```

This project currently uses EF Core 8 packages. If a machine has a newer global
tool, a matching temporary tool can be used instead:

```powershell
dotnet tool install dotnet-ef --version 8.0.10 --tool-path "$env:TEMP\autodev-dotnet-ef-8.0.10"
```

## Adding A Migration

Make the model change first, then generate a migration from the repository root:

```powershell
dotnet ef migrations add <Name> -p src/AutoDevRunner -s src/AutoDevRunner -o Migrations
```

Inspect the generated `Up()` and `Down()` before committing. Do not hand-edit
tables in a live database and do not use destructive migrations unless the
migration preserves existing data.

## Startup Initialization

`Data/DatabaseInitializer.cs` chooses one of three safe paths:

- Fresh database or missing `Projects` table: run `Database.MigrateAsync()`.
- Legacy `EnsureCreated` database with tables but no `__EFMigrationsHistory`:
  create the history table, insert the `InitialCreate` migration row, then run
  migrations so only newer migrations execute.
- Already migrated database: run `Database.MigrateAsync()` as an idempotent
  no-op unless pending migrations exist.

If initialization fails, startup logs a critical error and rethrows. The app
must not run against a half-migrated schema.

Provider-state seeding and orphaned-run cleanup still run after database
initialization succeeds.

## Resetting A Dev Database

Use the repository helper when available:

```powershell
.\scripts\reset-db.ps1
```

Or drop and recreate the configured local PostgreSQL database manually, then
start the app so migrations rebuild the schema:

```powershell
dropdb autodev
createdb autodev
dotnet run --project src/AutoDevRunner
```
