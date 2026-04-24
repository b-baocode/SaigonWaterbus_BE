# SaigonWaterbus

Backend skeleton using Clean Architecture foundations (Web, Application, Domain, Infrastructure) with no business-specific modules.

## Build

Run `dotnet build` to build the solution.

## Run

To run the API:

```bash
dotnet run --project .\src\Web
```

## Local Secret (DB Password)

Set the password via User Secrets (not in source code):

```bash
dotnet user-secrets --project .\src\Web set "ConnectionStrings:SaigonWaterbusDb" "Host=localhost;Port=5432;Database=SaigonWaterbusDb;Username=postgres;Password=YOUR_PASSWORD;"
```

## Code-First Migration

Create a migration:

```bash
dotnet ef migrations add InitialCreate --project .\src\Infrastructure\Infrastructure.csproj --output-dir Data\Migrations --msbuildprojectextensionspath .\artifacts\obj\Infrastructure\
```

Apply migration to database:

```bash
dotnet ef database update --project .\src\Infrastructure\Infrastructure.csproj --msbuildprojectextensionspath .\artifacts\obj\Infrastructure\
```

## Current Scope

- Core infrastructure and architecture plumbing only.
- No authentication/identity module is included.
- No sample Todo/Weather business use-cases are included.

## Add New Business Modules

Implement your own domain entities, application use-cases, and API endpoints based on project requirements.

Recommended flow:

1. Add domain models in `src/Domain`.
2. Add commands/queries in `src/Application`.
3. Add endpoint groups in `src/Web/Endpoints`.
4. Add EF configuration and migrations in `src/Infrastructure`.
