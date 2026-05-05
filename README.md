# SaigonWaterbus

Backend API for Saigon Waterbus using Clean Architecture (`Web`, `Application`, `Domain`, `Infrastructure`) with auth, user management, OTP, and PostgreSQL migrations.

## Build

Run `dotnet build SaigonWaterbus.slnx` to build the solution.

## Run

To run the API:

```bash
dotnet run --project ./src/Web --no-launch-profile
```

## Local Secret (DB Password)

Set the password via User Secrets (not in source code):

```bash
dotnet user-secrets --project ./src/Web set "ConnectionStrings:SaigonWaterbusDb" "Host=localhost;Port=5432;Database=SaigonWaterbusDb;Username=postgres;Password=YOUR_PASSWORD;"
```

## Code-First Migration

Create a migration:

```bash
dotnet ef migrations add <MigrationName> --project ./src/Infrastructure/Infrastructure.csproj --startup-project ./src/Web/Web.csproj --output-dir Data/Migrations --msbuildprojectextensionspath ./artifacts/obj/Infrastructure/
```

Apply migration to database:

```bash
dotnet ef database update --project ./src/Infrastructure/Infrastructure.csproj --startup-project ./src/Web/Web.csproj --msbuildprojectextensionspath ./artifacts/obj/Infrastructure/
```

Safe migrate + seed without deleting data:

```bash
dotnet run --project ./src/Web --no-launch-profile -- db:migrate-seed
```

## Local Verify Before Deploy

Run one command to verify restore, build, test, and fresh-database migration:

```bash
./scripts/verify-local.sh
```

Verify that expired pending registrations are cleaned up from a temporary database only:

```bash
./scripts/verify-pending-cleanup.sh
```

## Deploy Database

Apply migrations and seed built-in roles to a remote database without deleting existing data:

```bash
./scripts/deploy-db.sh '<postgresql-connection-string>'
```

This command will:

- apply pending EF migrations
- seed built-in roles

To seed the default admin and manager accounts on a remote database, enable it explicitly:

```bash
Database__SeedInternalUsers=true ./scripts/deploy-db.sh '<postgresql-connection-string>'
```

By default, remote deploy keeps internal-user seeding disabled. Local development still seeds those accounts.

If your local database was created from an older auth branch, reset it once before running the API again:

```bash
dropdb -h localhost -p 5432 -U postgres SaigonWaterbusDb
createdb -h localhost -p 5432 -U postgres SaigonWaterbusDb
dotnet run --project ./src/Web --no-build --no-launch-profile -- db:reset-seed
```

## Current Scope

- Auth endpoints: register, verify OTP, login, refresh token, forgot/reset password, Google login.
- User management endpoints for manager/admin.
- PostgreSQL code-first migrations and database seed for built-in roles.

## Active Configuration Keys

The repo now uses only these runtime config sections:

- `ConnectionStrings:SaigonWaterbusDb`
- `Database:ResetOnStartup`
- `Database:SeedInternalUsers`
- `Jwt:*`
- `Otp:*`
- `Gmail:*`
- `Brevo:*`
- `OAuth:Google:ClientId`

Legacy `Email:*` keys were removed.

## Add New Business Modules

Implement your own domain entities, application use-cases, and API endpoints based on project requirements.

Recommended flow:

1. Add domain models in `src/Domain`.
2. Add commands/queries in `src/Application`.
3. Add endpoint groups in `src/Web/Endpoints`.
4. Add EF configuration and migrations in `src/Infrastructure`.
