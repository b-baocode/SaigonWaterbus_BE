<div align="center">

# FPT UNIVERSITY

### CAPSTONE PROJECT REPORT

## Waterbus

**System for providing information and booking tickets to visit Saigon River**

`GSU26SE05` &nbsp;&middot;&nbsp; `SU26SE114`

</div>

---

## Project Information

| | |
|---|---|
| **University** | FPT University |
| **Group** | GSU26SE05 |
| **Capstone project code** | SU26SE114 |
| **Supervisor** | Nguyen Ngoc Lam |
| **Project** | Waterbus |

## Group Members

| No. | Student name | Student ID | Role |
|:---:|---|:---:|:---:|
| 1 | Ung Mai Thi Hong Nga | DE180704 | Frontend Developer |
| 2 | Ngo Gia Bao | SE184840 | Frontend Developer |
| 3 | Nguyen Huu Hoang | SE170060 | Backend Developer |
| 4 | Ngo Gia Bao | SE181581 | Backend Developer |

## About The Project

Waterbus is a platform for discovering Saigon River routes and managing the
complete ticket-booking journey. The system supports regular waterbus trips,
sightseeing services, schedules, seat selection, payments, tickets, customer
points, insurance, trip operations, and incident handling.

## Backend Structure

```text
src/
|-- Domain/          Core entities, enums, and business rules
|-- Application/     Use cases, validation, commands, and queries
|-- Infrastructure/  Database, authentication, integrations, and services
`-- Web/             HTTP endpoints, Swagger, SignalR hubs, and configuration

tests/
|-- Domain.UnitTests/
|-- Application.UnitTests/
`-- Integration.Tests/
```

## Technology

- ASP.NET Core 9
- Entity Framework Core
- PostgreSQL
- MediatR and FluentValidation
- JWT authentication
- SignalR
- Swagger / OpenAPI

## Getting Started

### Prerequisites

- Git
- .NET SDK 9
- PostgreSQL 15 or newer, or Docker Desktop

Check the installed tools:

```bash
git --version
dotnet --version
```

### 1. Clone The Repository

```bash
git clone https://github.com/b-baocode/SaigonWaterbus_BE.git
cd SaigonWaterbus_BE
```

### 2. Start PostgreSQL

Use an existing PostgreSQL server, or start a local container:

```bash
docker run --name saigon-waterbus-postgres \
  --env POSTGRES_DB=saigon_waterbus \
  --env POSTGRES_USER=postgres \
  --env POSTGRES_PASSWORD=postgres \
  --publish 5432:5432 \
  --detach postgres:16
```

If the container already exists, start it with:

```bash
docker start saigon-waterbus-postgres
```

### 3. Configure The Local Environment

Redis is optional for local development. Disable it to use the in-memory
fallback for seat holds and other temporary data.

macOS or Linux:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__SaigonWaterbusDb="Host=localhost;Port=5432;Database=saigon_waterbus;Username=postgres;Password=postgres"
export Redis__Enabled=false
export Jwt__SigningKey="LocalDevelopmentSigningKey-MustBeAtLeast32Chars"
```

Windows PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__SaigonWaterbusDb = "Host=localhost;Port=5432;Database=saigon_waterbus;Username=postgres;Password=postgres"
$env:Redis__Enabled = "false"
$env:Jwt__SigningKey = "LocalDevelopmentSigningKey-MustBeAtLeast32Chars"
```

Keep credentials in environment variables or `src/Web/appsettings.Local.json`.
Do not commit local credentials to Git.

### 4. Restore And Run

```bash
dotnet restore
dotnet run --project src/Web --launch-profile http
```

In Development, the application automatically applies database migrations and
seeds the required roles and reference data.

Open Swagger after the application starts:

```text
http://localhost:5212/swagger
```

Administrative credentials are managed privately by the project team and must
not be committed to the repository. Authorized team members can use
`POST /api/auth/login` to obtain an access token, then select **Authorize** in
Swagger and enter the token.

### 5. Stop Local Services

Press `Ctrl+C` to stop the backend. If PostgreSQL is running in Docker:

```bash
docker stop saigon-waterbus-postgres
```

## Run Tests

```bash
dotnet test SaigonWaterbus.slnx
```

---

<div align="center">

**FPT University &middot; Capstone Project &middot; Summer 2026**

</div>
