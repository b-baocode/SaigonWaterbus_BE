# Infrastructure Simplification Report (Backend)

## Scope
This report documents infrastructure simplification applied to the backend project to reduce operational complexity while keeping the current Clean Architecture structure.

## Goals
- Remove destructive database initialization flow.
- Make backend-to-frontend integration safer for separate repositories.
- Keep local development straightforward.

## Implemented Changes

| Area | Previous State | Updated State | Benefit |
| --- | --- | --- | --- |
| Database initialization | Startup used `EnsureDeletedAsync()` then `EnsureCreatedAsync()` in development. | Startup now uses: `MigrateAsync()` when migrations exist, otherwise `EnsureCreatedAsync()`. | Prevents accidental data loss and prepares project for migration-based lifecycle. |
| CORS policy | API allowed any origin, header, and method globally. | API now uses named policy `FrontendClientPolicy` with allow-list origins from config. | Safer integration for FE in another repository and clearer environment control. |
| Runtime pipeline | CORS configured inline in `Program.cs`. | Pipeline now applies named policy via `UseCors("FrontendClientPolicy")`. | Cleaner startup and easier policy maintenance. |
| Configuration | No explicit CORS settings in appsettings. | Added `Cors:AllowedOrigins` in appsettings for local FE hosts. | Configuration-driven behavior, easier to adapt per environment. |

## Files Updated
- `src/Infrastructure/Data/ApplicationDbContextInitialiser.cs`
- `src/Web/DependencyInjection.cs`
- `src/Web/Program.cs`
- `src/Web/appsettings.json`

## Why This Is Simpler
- Fewer destructive assumptions during startup.
- One clear CORS policy for FE/BE split-repo development.
- Configuration changes can be made without touching backend code.

## Current Constraint
- The target database `SaigonWaterbusDb` does not exist yet in local PostgreSQL.
- Because of this, runtime DB connection still fails until DB is created.

## Recommended Next Actions
1. Create local database:
   - `createdb -h localhost -p 5432 -U postgres SaigonWaterbusDb`
2. Add first EF migration in Infrastructure project.
3. Run update database and verify API startup.
4. Add environment-specific appsettings (Development/Production) for CORS and connection strings.

## Validation Checklist
- [x] Backend code updated for non-destructive initialization.
- [x] CORS moved to explicit named policy.
- [x] Appsettings includes allowed FE origins.
- [ ] Database created locally.
- [ ] Migration created and applied.
