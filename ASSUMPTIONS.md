# Assumptions

## Scope and environment
- The repository is named `school-admission-system` and includes `backend/`, `frontend/`, `docker-compose.yml`, and the required documentation files at the root.
- `docker-compose up` is the supported way to run the full system. PostgreSQL data is stored in a named Docker volume so stopping with `docker-compose down` and starting again with `docker-compose up` preserves state (while `docker-compose down -v` intentionally removes it).

## Identifiers and headers
- Class IDs and hold IDs are generated server-side as opaque string identifiers (UUID-based) and returned as strings in all responses. Example values in the brief (`c_123`, `h_789`) are illustrative.
- The `X-Student-Id` header identifies the student for hold creation, confirmation, cancellation, and listing their holds. It must be the student's 17-digit birth registration number (exactly 17 numeric digits). The UI normalizes input to digits and enforces this format; the API validates and returns a user-friendly `400` error for missing or invalid identifiers.

## Seat accounting and concurrency
- Seat counts are derived from persisted hold records: `confirmedSeats` counts holds with status `CONFIRMED`; `heldSeats` counts holds with status `HELD` whose `expiresAt` is in the future (and not yet expired); `availableSeats = totalSeats - heldSeats - confirmedSeats`.
- A hold is considered active for the "one active application per class" rule when it is `HELD` and not expired. A `CONFIRMED` admission in the same class also blocks creating another hold for that student.
- Seat limits and single-active-hold rules are enforced in the database using transactions and row-level locking on the target class record so concurrent requests cannot oversubscribe seats.
- Expiration is enforced automatically by a background worker that marks overdue `HELD` records as `EXPIRED`, and by time-based checks when reading/confirming holds, so seats become available without any manual API call.

## API behavior details
- `POST /classes/{classId}/holds` returns `409 Conflict` when no seats are available or when the student already has an active/confirmed hold for the class.
- `POST /holds/{holdId}/confirm` returns `200` with `{ "holdId", "status": "CONFIRMED" }` for an owned, unexpired `HELD` application. Repeated confirmation of an already confirmed application by the same student is treated as idempotent and returns the same confirmed response without increasing `confirmedSeats` again. Confirming an expired, cancelled, missing, or non-owned hold returns an error response.
- `DELETE /holds/{holdId}` cancels an owned `HELD` application and returns `204`. Cancelling an already cancelled/non-active, expired, missing, or non-owned hold returns an error response; the UI surfaces these as user-friendly messages. Cancellation is performed transactionally with row-level locking to avoid races with confirm/expiry.
- `GET /holds/{holdId}` and `GET /me/holds` report the current effective status. If a `HELD` hold has passed `expiresAt` before the background worker runs, it is treated/reported as `EXPIRED`. `GET /holds/{holdId}` may include additional fields (such as `studentId`) beyond the minimal example; required fields are never removed or renamed.
- `HOLD_DURATION_SECONDS` defaults to `600` when the environment variable is not set or invalid.
- Unhandled server errors return a generic JSON error payload so the UI can display a user-friendly message instead of raw/internal details.

## UI behavior
- The student identifier entered at the top of the page is stored in the browser (local storage) so refreshing or reopening the browser preserves it and allows viewing ongoing applications.
- The UI polls class seat counts and the current application view every few seconds, and also updates countdown/expiration state locally so an expired application shows as `EXPIRED` and disables payment without a page reload. A manual refresh control is provided for "My Applications"; the list displays all applications for the entered student (including confirmed/cancelled/expired) with their statuses.
- "Pay Admission Fee" triggers the confirm API and assumes payment succeeds; no payment gateway is integrated.

## Bonus: multi-process safety
- Because seat accounting and hold creation/confirmation are enforced with PostgreSQL transactions and row-level locks on shared class/hold records, running multiple backend instances against the same database preserves the invariant that `held + confirmed <= totalSeats`.
