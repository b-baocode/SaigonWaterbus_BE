# SaigonWaterbus

Backend skeleton using Clean Architecture foundations (Web, Application, Domain, Infrastructure) with no business-specific modules.

## Build

Run `dotnet build` to build the solution.

## Run

To run the application:

```bash
dotnet run --project .\src\AppHost
```

The Aspire dashboard will open automatically, showing the application URLs and logs.

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

## Test

The solution contains unit, integration, and functional tests.

To run the tests:
```bash
dotnet test
```
