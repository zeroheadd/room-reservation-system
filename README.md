# Room reservation system

An office meeting-room booking application: browse the rooms, book them for meetings, manage
the reservations, and see how the rooms are *actually* used so the office can spot rooms that
are booked often but rarely occupied.

Built for the IDT internship technical task.

- **Backend** — ASP.NET Core (net9.0) Web API with EF Core **in-memory** database
- **Frontend** — React 18 + TypeScript (Vite)
- **Tests** — 73 xUnit tests (backend) and 16 Vitest tests (frontend)
- **No authentication**, as the task specifies

---

## Contents

- [Screens and features](#screens-and-features)
- [Booking rules R1-R7](#booking-rules-r1-r7)
- [Architecture and decisions](#architecture-and-decisions)
- [Running the application](#running-the-application)
- [API reference](#api-reference)
- [Tests](#tests)
- [Local toolchain notes](#local-toolchain-notes)
- [What is deliberately not done](#what-is-deliberately-not-done)

---

## Screens and features

### Rooms

Lists every meeting room with its **name**, **unique identifier** (e.g. `KA-201`), **capacity**,
**floor/location**, **equipment** (projector, TV screen, video conferencing, whiteboard) and
**status** (active / inactive). Inactive rooms are shown but marked, and cannot be booked.

Filtering: minimum capacity, equipment (a room must have *all* the selected items) and floor.
Filtering is executed by the backend (`/api/rooms?minCapacity=20&equipment=Projector,Whiteboard&floor=3rd`),
so the contract holds for larger data sets too.

### Users

Lists first name, last name, unique identifier, department, e-mail and **how many active
reservations** each person currently has. Searchable by name, identifier, e-mail or department.
"Active" excludes cancelled reservations.

### Reservations

Lists every reservation with room, organizer, meeting title, start, end, number of attendees and
status (`Booked`, `Completed`, `Cancelled`).

Actions: **create**, **edit**, **cancel**, and **end a meeting early** (which is also how actual
attendance is recorded — see the usage report).

Filtering: room, organizer, status and date range. The date-range filter keeps reservations that
*intersect* the window, which is what people expect when they ask "what is booked next week".

A **room timeline** below the table shows the selected room's week, with each reservation drawn
at its real start/end time.

### Room usage report

The point of the report is to answer: *which rooms are often booked but rarely used?*

Every room gets reserved hours, actually used hours, wasted hours, booked vs actual attendees,
a utilisation ratio and a verdict. Rooms are sorted worst-utilisation-first, and underused rooms
are highlighted. The whole report can be exported to PDF.

---

## Booking rules R1-R7

All rules are validated **in the backend** on every create and edit, before anything is saved.
A rejected request returns HTTP 400 with a stable code plus a human readable message, and the
frontend displays it.

| # | Rule | Code returned |
|---|------|---------------|
| R1 | A reservation may not last longer than **7 days** (exactly 7 days is allowed) | `R1_MAX_DURATION_EXCEEDED` |
| R2 | A room may not hold two overlapping active reservations (cancelled ones do not block) | `R2_ROOM_OVERLAP` |
| R3 | A user may not organize two overlapping meetings, even in different rooms | `R3_ORGANIZER_OVERLAP` |
| R4 | The start must be in the future and the end must be after the start | `R4_INVALID_TIME_RANGE` |
| R5 | Attendees may not exceed the room capacity | `R5_CAPACITY_EXCEEDED` |
| R6 | A reservation may not start more than **90 days** in the future | `R6_BOOKING_HORIZON_EXCEEDED` |
| R7 | Inactive rooms cannot be booked | `R7_ROOM_INACTIVE` |

All applicable rules are reported together, so a user fixing a reservation sees every problem at
once instead of one per attempt.

The 7-day and 90-day limits are **configurable**, not hard-coded — `appsettings.json`:

```json
"BookingSettings": {
  "MaxReservationDurationDays": 7,
  "BookingHorizonDays": 90,
  "UnderusedThreshold": 0.5,
  "MinimumMeetingsForUsageVerdict": 3
}
```

Other codes the API can return: `ROOM_NOT_FOUND`, `USER_NOT_FOUND`, `RESERVATION_NOT_FOUND`,
`RESERVATION_NOT_MODIFIABLE`, `RESERVATION_NOT_CANCELLABLE`, `RESERVATION_NOT_ENDABLE`,
`RESERVATION_NOT_STARTED`, `VALIDATION_FAILED`.

---

## Architecture and decisions

```
room-reservation-system/
├── src/RoomReservation.Api/            ASP.NET Core Web API
│   ├── Controllers/                    HTTP surface + filtering
│   ├── Services/                       booking rules, reservation operations, report
│   ├── Data/                           EF Core context + demo seeder
│   ├── Domain/                         error codes and the error envelope
│   ├── Dtos/                           request/response shapes + mapping
│   └── Models/                         Room, User, Reservation, BookingSettings
├── tests/RoomReservation.Api.Tests/    xUnit unit + integration tests
├── client/                             React + TypeScript SPA
│   └── src/{api,components,pages,utils,test}
├── tools/nuget-bridge.js               build-environment helper (see below)
├── build.cmd                           backend build/test/run helper
└── publish-client.cmd                  builds the SPA into the API's wwwroot
```

### Where the logic lives

**All booking-rule logic is in the backend** (`BookingRuleValidator`), which is the single source
of truth. The React form performs only trivial completeness checks (required fields, end after
start) so the user gets fast feedback; it never decides whether a booking is allowed. The rule
codes it receives are displayed verbatim, which keeps the two sides from drifting apart.

The layers are deliberately thin and explicit:

- **Controllers** parse and validate query strings, then shape HTTP responses.
- **Services** hold the business rules and are the only thing tests need to exercise.
- **`ReservationService`** is the single gateway for writes: it builds a *candidate* reservation,
  runs the validator against it, and only then mutates the tracked entity. An invalid reservation
  therefore cannot reach the database, and a rejected edit leaves the stored row untouched.

### Decisions worth calling out

**A single validator returning a list of violations.** Rather than one method per rule, one class
collects every violation into a `List<ValidationError>`. This makes "report all broken rules at
once" natural, keeps every rule visible in one file, and makes rules trivially testable.

**Stable error codes as a contract.** Each violation carries a code (`R5_CAPACITY_EXCEEDED`) and a
message. The frontend switches on codes, never on message text, so wording can change freely.

**`TimeProvider` instead of `DateTime.UtcNow`.** Time is injected, so tests run against a fixed
clock and rule R4 ("start must be in the future") is verified deterministically instead of by
racing the wall clock.

**Reservation lifecycle.** `Booked → Completed` (the meeting happened) or `Booked → Cancelled`.
A small `BackgroundService` (`ReservationStatusUpdater`) transitions reservations whose end time
has passed into `Completed`, so the status filter never shows stale "Booked" rows for meetings
that already happened.

**The in-memory database is seeded**, but only in the app: setting `Database:Seed=false` lets the
integration tests build their own deterministic fixtures instead of colliding with demo data.

**Timestamps are UTC** end to end. Conversion to local wall-clock time happens only at the UI
boundary, in `client/src/utils/dates.ts`, so the "meeting moved by two hours" class of bug cannot
creep in.

### The room usage report: the one place the brief left a gap

"How the rooms are **actually used**" is not defined in the task, and a naive implementation
(comparing reservations against the clock) tells you nothing useful — a room that is booked and
left empty looks identical to a room that was used.

So usage is measured in a way the data can actually support:

- When a meeting is **ended early**, the organizer records how many people attended. This is the
  only honest source of "was this room really needed?".
- `ReservedHours` counts every active (non-cancelled) reservation.
- `ActualHours` counts finished meetings, using the real end time when a meeting was ended early
  (a 3-hour slot abandoned after 30 minutes counts as 0.5 h used, not 3 h).
- `Utilization` = actual attendees ÷ booked attendees over finished meetings **that recorded
  attendance**.
- A room is flagged **"Often booked, rarely used"** when utilization is below
  `UnderusedThreshold` (default 50%) over at least `MinimumMeetingsForUsageVerdict`
  (default 3) meetings. The minimum stops a single small meeting from triggering a verdict — rooms
  without enough data report "Not enough data" instead.

Utilization is attendee-based rather than hour-based because hour-based ratios punish rooms that
are legitimately booked for buffers, while attendee counts directly express "nobody came".

### PDF export

The backend renders the same report as a **self-contained, print-optimised HTML document**
(`/api/reports/room-usage/print`) that opens the browser's print dialog, where "Save as PDF"
produces the file.

This was chosen over a server-side PDF library on purpose: no native dependency to build or
licence, nothing to go stale, and the exported document is guaranteed to show the same numbers as
the screen. The trade-off is that the PDF is produced by the user's browser rather than stored
server-side; if the office ever needs scheduled, unattended PDF generation, that is the point to
introduce a library such as QuestPDF.

---

## Running the application

### Prerequisites

- .NET SDK 9.0 (this checkout can install it locally — see [Local toolchain notes](#local-toolchain-notes))
- Node.js 18+ (developed against Node 25)

### Option 1 — two processes (normal development)

Backend on `http://localhost:5080`:

```cmd
build.cmd run
```

Frontend dev server on `http://localhost:5173` with hot reload:

```cmd
cd client
npm install
npm run dev
```

Vite proxies `/api` to the backend, so open <http://localhost:5173>.

Swagger UI is at <http://localhost:5080/swagger>.

### Option 2 — one process

Build the SPA into the API's `wwwroot` and run the API alone:

```cmd
publish-client.cmd
build.cmd run
```

Then open <http://localhost:5080>. Client-side routing works, so deep links such as
`/reservations` survive a refresh.

### Configuration

Edit `src/RoomReservation.Api/appsettings.json` to change the booking limits. For example, to
check that a 100-day-away booking is accepted but a 101-day-away one is not:

```json
"BookingHorizonDays": 100
```

The API exposes the effective values at `GET /api/settings`, and the Reservations page displays
them, so the UI always states the rules it is actually running under.

---

## API reference

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/rooms` | List rooms. Query: `minCapacity`, `equipment` (comma separated, all required), `floor`, `activeOnly` |
| `GET` | `/api/rooms/{id}` | One room |
| `GET` | `/api/equipment` | The equipment vocabulary |
| `GET` | `/api/users` | List users with active reservation counts. Query: `search` |
| `GET` | `/api/users/{id}` | One user |
| `GET` | `/api/reservations` | List reservations. Query: `roomId`, `organizerId`, `status`, `from`, `to` |
| `GET` | `/api/reservations/{id}` | One reservation |
| `POST` | `/api/reservations` | Create (validated against R1-R7) |
| `PUT` | `/api/reservations/{id}` | Edit (validated against R1-R7) |
| `POST` | `/api/reservations/{id}/cancel` | Cancel |
| `POST` | `/api/reservations/{id}/end` | End early, optionally recording `actualAttendees` |
| `GET` | `/api/reports/room-usage` | Usage report as JSON. Query: `lastDays` |
| `GET` | `/api/reports/room-usage/print` | The same report as a printable HTML page |
| `GET` | `/api/settings` | The effective booking limits |
| `GET` | `/health` | Health probe |

Dates are ISO 8601 UTC, e.g. `2026-03-10T09:00:00Z`.

### Example: a rejected booking

```http
POST /api/reservations
{
  "roomId": 6, "organizerId": 1, "title": "Renovation room",
  "start": "2026-03-11T09:00:00Z", "end": "2026-03-11T10:00:00Z", "attendees": 4
}
```

```http
HTTP/1.1 400 Bad Request
{
  "code": "R7_ROOM_INACTIVE",
  "message": "Room 'Fjord' is inactive and cannot be booked.",
  "errors": [
    { "code": "R7_ROOM_INACTIVE", "message": "Room 'Fjord' is inactive and cannot be booked." }
  ]
}
```

When several rules are broken at once, `code` is `VALIDATION_FAILED` and `errors` lists each
violation with its own code.

---

## Tests

```cmd
build.cmd test          REM backend, 73 tests
cd client && npm test   REM frontend, 16 tests
```

**Backend (73)** — `tests/RoomReservation.Api.Tests`

- `BookingRuleValidatorTests` — every rule R1-R7, including the boundary cases the brief calls out
  (exactly 7 days allowed, 7 days + 1 minute rejected, exactly on the 90-day horizon allowed,
  91 days rejected), cancelled reservations not blocking a room, back-to-back bookings being
  allowed, different rooms being allowed to overlap, an edited reservation not clashing with
  itself, and multiple broken rules all being reported.
- `BookingSettingsTests` — the configured limits really drive R1 and R6, and the messages quote
  the configured numbers.
- `ReservationServiceTests` — create/edit/cancel/end-early behaviour, including that a rejected
  edit leaves the stored reservation untouched, ending before the start is refused, and actual
  attendance cannot exceed the booked number.
- `RoomUsageReportServiceTests` — the underused-room verdict, the minimum-meetings rule, cancelled
  reservations being ignored, early-ended meetings counting as shorter usage, the `lastDays`
  filter, totals, a custom threshold, and that the printable HTML is self-contained.
- `ReservationsApiTests` / `SeedDataTests` — end-to-end over real HTTP: status codes, the error
  envelope with rule codes, filtering, the report endpoints, and the shipped demo data.

**Frontend (16)** — `client/src/test`

- `dates.test.ts` — the local/UTC conversions the whole app depends on.
- `components.test.tsx` — rule errors rendered with their codes, tags, and the API client's error
  envelope and query building.
- `ReportPage.test.tsx` — the report renders totals, flags the underused room, explains the
  threshold, offers PDF export, and shows an error state.

---

## Local toolchain notes

This checkout was built on a Windows machine with some restrictions, so the repository carries a
few helpers. **None of them are needed on a normal development machine that already has the .NET
SDK and working TLS.**

- **Local SDK.** `.dotnet/` holds a locally installed .NET 9 SDK and is gitignored. On a machine
  with a system-wide SDK, delete `build.cmd`'s `DOTNET` variable or just use `dotnet` directly.
- **`tools/nuget-bridge.js`.** On this host the .NET CLI could not complete a TLS handshake
  (`No credentials are available in the security package`), while Node's HTTP stack worked fine.
  The bridge exposes the subset of the NuGet v3 protocol that `dotnet restore` needs over plain
  localhost HTTP and forwards it upstream. `NuGet.Config` points at it (with `nuget.org` left
  commented out as an alternative — leaving both enabled makes restore retry the unreachable
  source for minutes). Package hashes are still verified; only the optional
  repository-signature fetch is skipped. **Delete `NuGet.Config` on a normal machine.**
- **`vite.config.js` is plain JavaScript on purpose.** Vite bundles a TypeScript config by
  spawning esbuild as a child process, which is not permitted in every restricted environment.
- **Environment variables** (`DOTNET_CLI_HOME`, `NUGET_PACKAGES`, `TEMP`/`TMP`) are redirected
  by `build.cmd` so the toolchain writes only inside the repository.

---

## What is deliberately not done

The brief says the app "could not be 100% finished", so the gaps are named rather than hidden:

- **No authentication or authorization**, as instructed. Every user can act as any organizer.
- **No persistence beyond process lifetime** — the database is the required EF Core in-memory
  provider, so restarting the API resets the data to the seed.
- **No pagination.** Filtering is server-side, but the list endpoints return everything that
  matches. With thousands of reservations this needs paging.
- **Concurrency is last-write-wins.** Two simultaneous bookings of the same slot could both pass
  validation; a real deployment needs a transaction or a unique constraint on the time range.
- **Attendance is optional and self-reported**, so utilisation is only as good as the data people
  enter when they close a meeting.
- **No browser automation tests.** Backend behaviour is covered end to end over HTTP and the
  frontend logic by component tests; a Playwright layer would be the next step.
