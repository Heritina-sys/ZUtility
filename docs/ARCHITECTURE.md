# Architecture

How the ZK attendance platform is put together, why the pieces are split the
way they are, and where the design has sharp edges.

This document describes the whole three-repository system. It is duplicated in
[ZUtility](https://github.com/Heritina-sys/ZUtility) and
[FrontZK](https://github.com/Heritina-sys/FrontZK) so that each repository is
readable on its own.

---

## 1. The problem

ZKTeco fingerprint terminals store punches locally and expose them through a
proprietary Windows COM SDK (`zkemkeeper`). They are not HTTP devices, they do
not push, and they have no concept of an employee database — they only know a
numeric **enroll number** per enrolled finger/face.

A deployment has eight of these terminals on a local network. The platform has
to turn "eight boxes holding raw timestamped punches" into "a dashboard showing
who was present today and how many hours they worked this month".

That produces three distinct jobs with three incompatible constraints:

| Job | Constraint | Component |
|---|---|---|
| Talk to the terminals | Windows-only, 32-bit COM interop | **ZUtility** |
| Store and serve records | Cross-platform, needs a real database | **ZKAPI** |
| Present the data | Browser, needs to be reachable by staff | **FrontZK** |

The Windows/32-bit constraint is the reason for the split. `zkemkeeper` is a
registered 32-bit COM component, which forces `<PlatformTarget>x86` on whatever
process loads it. Rather than pin the entire platform to x86 Windows, only the
collector pays that cost.

---

## 2. Data flow

```
     ╔═══════════════════════════════════════════════════════════════╗
     ║  LAN 192.168.8.0/24                                           ║
     ║                                                               ║
     ║   .35   .36   .37   .38   .39   .40   .41   .42               ║
     ║    │     │     │     │     │     │     │     │   TCP 4370     ║
     ║    └─────┴─────┴──┬──┴─────┴─────┴─────┴─────┘                ║
     ╚═══════════════════╪═══════════════════════════════════════════╝
                         │
                         │  ① ReadAllGLogData + SSR_GetGeneralLogData
                         │     (poll, 8 devices in parallel)
                         ▼
              ┌────────────────────────┐
              │      ZUtility          │
              │                        │
              │  DeviceService         │  COM wrapper, one CZKEM per scope
              │  AttendanceService     │  ② punch pairing + dedup
              │  AttendanceWorker      │  BackgroundService, polls forever
              └───────────┬────────────┘
                          │
                          │  ③ POST /api/Attendance
                          │     { userId: <ENROLL NUMBER>, date, checkIn, … }
                          ▼
              ┌────────────────────────┐
              │        ZKAPI           │
              │                        │
              │  AttendanceController  │  ④ enroll number → User.Id
              │  AppDbContext          │
              └───────────┬────────────┘
                          │
                          │  EF Core / Pomelo
                          ▼
                    ┌───────────┐
                    │  MySQL    │   database `att`
                    │           │   Departments / Users / Attendances
                    └─────┬─────┘
                          │
                          │  ⑤ GET /api/Users, /api/Departments, /api/Attendance
                          ▼
              ┌────────────────────────┐
              │       FrontZK          │
              │                        │
              │  lib/api.ts            │  fetch wrapper + derived metrics
              │  hooks/use-attendance  │  SWR caching
              │  app/*                 │  ⑥ status, duration, monthly summary
              └────────────────────────┘
```

---

## 3. Stage by stage

### ① Polling the terminals — ZUtility

`AttendanceWorker` is an ASP.NET Core `BackgroundService`. Device addresses are
generated, not configured:

```csharp
private static readonly (string Ip, int Port)[] Devices = Enumerable
    .Range(35, 8)
    .Select(i => ($"192.168.8.{i}", 4370))
    .ToArray();
```

All eight are polled concurrently with `Task.WhenAll`. Each poll opens a COM
connection, calls `ReadAllGLogData` to load the device's log buffer, drains it
with `SSR_GetGeneralLogData`, and disconnects. An unreachable terminal logs a
warning and is skipped — one dead device does not stall the others.

> **Sharp edge.** The loop delay is `await Task.Delay(1, stoppingToken)` — one
> **millisecond**, not one second. The worker re-polls all eight terminals as
> fast as the network allows, permanently. This is almost certainly a typo for
> `1000` or a leftover from debugging. See ZUtility issue tracker.

### ② Pairing punches into sessions — ZUtility

A terminal emits one row per scan. It does not say whether a scan is an arrival
or a departure. `AttendanceService.ProcessLogs` derives that with a state
machine per employee:

- Find that employee's most recent session, ordered by `CheckIn`.
- If there is none, or the last one already has a `CheckOut` → this punch opens
  a **new session** (`CheckIn`).
- If the last session has a `CheckIn` and no `CheckOut` → this punch **closes**
  it (`CheckOut`).

Two guards protect against noise:

| Guard | Rule |
|---|---|
| Replay | Punches at or before the last recorded time are dropped. |
| Double-scan | Punches within `MIN_INTERVAL_SECONDS` (300 s) of the last one are dropped. |

A per-device high-water mark (`_lastSyncPerDevice`, keyed by IP) means each
terminal's cursor advances independently — re-reading a device's full buffer
does not re-import punches already seen.

> **Sharp edge.** `_data` and `_lastSyncPerDevice` are `private static` in-memory
> collections. They are the *only* record of session state inside ZUtility. On
> restart, the pairing state machine loses all context: the first punch after a
> restart always opens a new session, even for an employee who was mid-shift.
> ZKAPI's database is durable; ZUtility's pairing state is not.
>
> An earlier revision of this logic — still present as a commented block —
> keyed sessions on fixed windows (check-in 07:00–08:00, check-out 16:00–18:00)
> and used a 60-second dedup. It was replaced by the toggle model above, which
> no longer assumes fixed shift hours.

### ③ Pushing to the API — ZUtility

`AttendanceService.PushAsync` fires an HTTP `POST` per session transition to a
hardcoded `BACK_URL`:

```csharp
private const string BACK_URL = "http://localhost:5159/api/Attendance";
```

The call is fire-and-forget (`_ = PushAsync(...)`) — the result is logged but
never awaited and never retried. A push that fails because ZKAPI is down is
lost; the in-memory session is already marked as pushed and will not be
re-sent.

> **Sharp edge.** `DeviceController` declares `private readonly HttpClient _http`
> but its constructor only assigns `_attendanceService`. `_http` is therefore
> always `null`, and `POST /api/Device/sync` throws a
> `NullReferenceException` before it reaches the network. The manual-sync
> endpoint is non-functional. The automatic push path in `AttendanceService`
> uses a properly injected client and does work.

### ④ Translating enroll numbers — ZKAPI

This is the one deliberate asymmetry in the API, and the reason it exists is
worth stating plainly.

The terminals know enroll numbers. The database knows `User.Id`. Nothing on the
device side can know the database's primary keys, so the translation has to
happen at the boundary:

```csharp
// AttendanceController.Create
var user = await _context.Users.FirstOrDefaultAsync(u => u.EnrollNumber == dto.UserId);
if (user == null)
    return BadRequest($"Aucun user avec EnrollNumber {dto.UserId}");

var att = new Attendance { UserId = user.Id, /* … */ };
```

So on `POST /api/Attendance`, the field named `userId` carries an **enroll
number**. On every `GET`, the field named `userId` carries a **`User.Id`**. Same
name, two meanings, depending on direction.

This is a genuine wart. It works, it is intentional, and it will surprise
anyone who reads the DTO without reading this paragraph. The clean fix is a
distinct `enrollNumber` field on the write DTO.

**Consequence:** an employee must exist in `Users` with the right
`EnrollNumber` *before* their punches can be stored. A punch from an unenrolled
terminal ID is rejected with `400` and — because the push is fire-and-forget —
silently discarded.

### ⑤ Storage — ZKAPI

```
Department ──1:∞── User ──1:∞── Attendance
```

Relationships are configured explicitly in `AppDbContext.OnModelCreating`
rather than left to convention. Retry-on-failure is enabled on the MySQL
provider (3 attempts, 5 s cap), which covers transient connection drops.

All `Attendance` fields except `Id` are nullable. That is not sloppiness — it is
how an open shift is modelled: `CheckIn` set, `CheckOut` null.

> **Sharp edge.** No `Migrations/` folder is committed, so the schema is not
> reproducible from the repository. There is also no unique constraint on
> `User.EnrollNumber`, even though the translation in ④ assumes it is unique —
> `FirstOrDefaultAsync` silently picks one if it is not.

### ⑥ Derived metrics — FrontZK

The API stores facts; the dashboard derives judgements. Nothing in ⑥ is
persisted.

| Derived value | Rule | Defined in |
|---|---|---|
| `present` | has `CheckIn` and `CheckOut` | `determineAttendanceStatus` |
| `incomplete` | has `CheckIn`, no `CheckOut` | idem |
| `late` | `CheckIn` after 09:15 | idem |
| `absent` | no `CheckIn` | idem |
| `workDuration` | `CheckOut − CheckIn` | `calculateWorkDuration` |
| monthly totals | weekdays only, capped at today | `calculateMonthlySummary` |

> **Sharp edge — three thresholds disagree.** ZUtility's replaced logic treated
> 07:00–08:00 as the arrival window. FrontZK marks anyone arriving after
> **09:15** as late (`workStartHour = 9` plus a 15-minute grace). Nothing
> reconciles the two, and neither is configurable. The lateness rule is a
> business policy hardcoded in a frontend utility function.

Data fetching goes through SWR with `revalidateOnFocus: false`. A
`USE_MOCK_DATA` boolean at the top of `hooks/use-attendance.ts` switches the
whole dashboard onto `lib/mock-data.ts` fixtures — useful for UI work without a
running backend. It is currently `false`.

---

## 4. State ownership

Being explicit about this matters, because two components hold attendance state
and only one of them is durable.

| State | Owner | Durable? | Lost on restart? |
|---|---|---|---|
| Punch log | ZKTeco terminal | Yes (device buffer) | No |
| Per-device read cursor | ZUtility `_lastSyncPerDevice` | **No** (static field) | **Yes** |
| Open/closed session pairing | ZUtility `_data` | **No** (static field) | **Yes** |
| Attendance records | ZKAPI → MySQL | Yes | No |
| Employees, departments | ZKAPI → MySQL | Yes | No |
| Status, duration, summaries | FrontZK | No — recomputed per render | n/a (by design) |

The two "No" rows are the platform's main fragility. A ZUtility restart resets
the cursor to `DateTime.MinValue`, so the next poll re-reads each device's
entire buffer and re-pushes every punch it finds. ZKAPI has no idempotency key
on `POST /api/Attendance`, so those re-pushes create duplicate rows.

---

## 5. Ports and addresses

Every one of these is hardcoded somewhere. Collected here because they are
currently spread across five files.

| What | Value | Defined in |
|---|---|---|
| Terminals | `192.168.8.35`–`.42`, port `4370` | `ZUtility/Workers/AttendanceWorker.cs` |
| ZKAPI HTTP | `http://localhost:5159` | `ZKAPI/Properties/launchSettings.json` |
| ZKAPI HTTPS | `https://localhost:7171` | idem |
| ZUtility → ZKAPI | `http://localhost:5159/api/Attendance` | `ZUtility/Services/AttendanceService.cs` |
| CORS allow-list | `http://localhost:3000` | `ZKAPI/Program.cs` |
| FrontZK → ZKAPI | `http://localhost:5000/api` | `FrontZK/lib/api.ts` |

> **Sharp edge — the frontend default port is wrong.** `lib/api.ts` falls back to
> `http://localhost:5000/api`, but ZKAPI listens on **5159**. Every request
> fails unless `NEXT_PUBLIC_API_URL` is set. Separately, `app/page.tsx` bypasses
> the wrapper entirely with a hardcoded `fetch("https://localhost:7171/api/Departments")`,
> so that one call works while the rest of the page does not.

---

## 6. What a production hardening pass would change

Not a roadmap — an honest list of what stands between this and a deployment
that could be left unattended.

1. **Authentication on ZKAPI.** There is none. Full CRUD on attendance data is
   open to anyone who can reach the port.
2. **Idempotency on `POST /api/Attendance`.** A natural key of
   `(EnrollNumber, CheckIn)` with a unique index would make re-pushes safe and
   fix the restart-duplication problem.
3. **Durable cursor in ZUtility.** Persist `_lastSyncPerDevice`; drop the static
   collections.
4. **Retry with backoff on push.** Fire-and-forget loses data whenever ZKAPI
   restarts.
5. **Configuration out of source.** Device IPs, ports, URLs, CORS origins and
   the lateness threshold all belong in `appsettings.json` / environment.
6. **The `Task.Delay(1)` typo.** A 1 ms poll loop against eight COM devices.
7. **EF migrations committed**, plus a unique index on `User.EnrollNumber`.
8. **Remove the `WeatherForecast` template scaffolding** from both .NET
   projects.
9. **Swagger behind an environment guard.**

---

## 7. Repository map

| Repository | Contains | Platform |
|---|---|---|
| [ZKAPI](https://github.com/Heritina-sys/ZKAPI) | REST API, EF model, MySQL schema | Any (.NET 10) |
| [ZUtility](https://github.com/Heritina-sys/ZUtility) | Device collector, COM interop, pairing logic | **Windows x86 only** |
| [FrontZK](https://github.com/Heritina-sys/FrontZK) | Next.js dashboard | Any (Node 20+) |
