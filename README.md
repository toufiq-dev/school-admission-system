# School Admission System

A first-come, first-served school admission system with a .NET 8 Web API, PostgreSQL, and a React (Vite) frontend.

## Prerequisites

- Docker
- Docker Compose

No other local installation is required.

## Run with Docker

From the repository root:

```bash
docker compose up --build
```

This starts:
- PostgreSQL on port `5432`
- Backend API on `http://localhost:8080`
- Frontend UI on `http://localhost:5173`

Open the frontend at: **http://localhost:5173**

To stop:

```bash
docker compose down
```

To stop and remove persisted database data:

```bash
docker compose down -v
```

## Environment variables

Docker Compose supports these variables (defaults shown):

- `POSTGRES_DB=school_admission`
- `POSTGRES_USER=postgres`
- `POSTGRES_PASSWORD=postgres`
- `HOLD_DURATION_SECONDS=600` (hold expiration duration in seconds)
- `VITE_API_BASE_URL=http://localhost:8080` (frontend build-time backend URL)
- `API_BASE_URL=http://localhost:8080` (frontend runtime backend URL written to `config.js`)

Backend database connection is provided via `ConnectionStrings__DefaultConnection` in `docker-compose.yml` (configurable through environment variables).

## Running backend tests

Backend tests are in `backend/tests/SchoolAdmission.Tests` and include a concurrency test that verifies holds do not exceed seat limits.

The tests run against a PostgreSQL database (default: `Host=localhost;Port=5432;Database=school_admission_test;Username=postgres;Password=postgres`). You can override with `TEST_DATABASE_URL`.

Example (with PostgreSQL running locally):

```bash
cd backend
dotnet test tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj
```

If you do not have the .NET SDK installed locally, run tests inside a .NET 8 SDK container after starting PostgreSQL (for example via `docker compose up -d postgres`):

```bash
docker run --rm --network host -v "$PWD/backend":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/SchoolAdmission.Tests/SchoolAdmission.Tests.csproj
```

> Note: when running the test container on Linux Docker networking, use the appropriate host/network settings so the container can reach PostgreSQL.

## API summary

Base URL: `http://localhost:8080`

- `POST /classes`
- `GET /classes`
- `GET /classes/{classId}`
- `POST /classes/{classId}/holds` (`X-Student-Id` header)
- `GET /holds/{holdId}`
- `POST /holds/{holdId}/confirm` (`X-Student-Id` header)
- `DELETE /holds/{holdId}` (`X-Student-Id` header)
- `GET /me/holds` (`X-Student-Id` header)

## Project structure

- `backend/` - ASP.NET Core Web API, EF Core/PostgreSQL data access, services, and tests
- `frontend/` - React + Vite UI
- `docker-compose.yml` - runs PostgreSQL, backend, and frontend together
- `ASSUMPTIONS.md` - documented assumptions
- `AI_USAGE.md` - AI usage notes
