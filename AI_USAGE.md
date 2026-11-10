# AI Usage

## Tools used
- An AI coding assistant was used to help scaffold the project structure, draft backend/frontend code, and prepare documentation.

## Where it was used
- **Backend:** initial ASP.NET Core/EF Core project scaffolding, entity/DTO/controller/service structure, hold expiration background worker, and test scaffolding.
- **Frontend:** React + Vite project structure, API client, UI layout/state handling for polling, countdown/expiry display, and browser persistence of the student identifier.
- **DevOps/docs:** Dockerfiles, `docker-compose.yml`, and documentation drafts (`README.md`, `ASSUMPTIONS.md`).

## Mistakes and corrections
- **Frontend scaffolding in a non-interactive environment:** an automated Vite scaffolding command did not complete interactively, so the frontend files (`package.json`, Vite config, HTML entry, React source files) were created manually with the required React 18/Vite setup.
- **API contract casing/shape:** DTOs were reviewed and adjusted with explicit JSON property naming to ensure request/response fields match the assignment contract exactly (e.g. `holdId`, `classId`, `studentId`, `expiresAt`, seat count fields).
- **Concurrency and expiry handling:** hold creation/confirmation logic was implemented with database transactions and row-level locking on class/hold records, plus time-based expiration checks and a background expiry pass, to satisfy seat-limit, single-active-hold, and automatic-expiration requirements.
- **Testing environment assumptions:** backend tests (including the concurrency test) were written to run against PostgreSQL via a configurable test connection string; instructions were documented for running them with Docker/.NET SDK when not installed locally.

## Verification performed
- Reviewed the implementation against the required API contract and business rules.
- Validated `docker-compose.yml` configuration.
- Added automated backend tests covering class creation/listing, idempotent confirmation, and concurrent hold creation under seat limits.
