# ZUtility

Device collector for the **ZK attendance platform**. ZUtility talks to ZKTeco
biometric terminals over their COM SDK, turns raw punches into check-in /
check-out sessions, and pushes those sessions to
[ZKAPI](https://github.com/Heritina-sys/ZKAPI).

It is the only component that touches hardware, and the only one that must run
on Windows.

---

## Why this exists as a separate service

ZKTeco terminals are not HTTP devices. They buffer punches locally and expose
them through `zkemkeeper`, a **registered 32-bit Windows COM library**. Loading
it forces `<PlatformTarget>x86` on the host process.

Rather than pin the whole platform to 32-bit Windows, that constraint is isolated
here. ZKAPI and FrontZK run anywhere; only the collector pays the cost.

```
┌──────────────────────┐
│  8 × ZKTeco terminal │   192.168.8.35 → .42, TCP 4370
└──────────┬───────────┘
           │  zkemkeeper COM (poll)
           ▼
┌──────────────────────┐
│  ZUtility ← this repo│   Windows x86. Poll, pair punches, push.
└──────────┬───────────┘
           │  HTTP POST /api/Attendance
           ▼
┌──────────────────────┐
│        ZKAPI         │   ASP.NET Core + EF Core + MySQL
└──────────┬───────────┘
           │  HTTP GET
           ▼
┌──────────────────────┐
│       FrontZK        │   Next.js dashboard
└──────────────────────┘
```

| Repository | Role | Platform |
|---|---|---|
| **ZUtility** | Device collector | **Windows x86 only** |
| [ZKAPI](https://github.com/Heritina-sys/ZKAPI) | REST API, MySQL schema | Any (.NET 10) |
| [FrontZK](https://github.com/Heritina-sys/FrontZK) | Web dashboard | Any (Node 20+) |

Full data flow and design rationale: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Stack

| | |
|---|---|
| Runtime | .NET 10 (`net10.0`), ASP.NET Core |
| Platform target | **x86** — required by the COM SDK |
| Device SDK | `zkemkeeper` COM (`tlbimp`, embedded interop types) |
| Hosting | `BackgroundService` + minimal REST surface |
| API docs | Swashbuckle / Swagger UI |

---

## Prerequisites

Unlike its sibling repositories, ZUtility cannot be built or run on Linux or
macOS. The COM reference will not resolve.

- **Windows** — the SDK is a Windows COM component
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **ZKTeco standalone SDK installed and registered.** `zkemkeeper.dll` must be
  registered with `regsvr32`; the build generates `Interop.zkemkeeper.dll` from
  the type library via `tlbimp`. See [`docs/DEVICE-SDK.md`](docs/DEVICE-SDK.md).
- Network reachability to the terminals on TCP 4370

---

## Quick start

```powershell
git clone https://github.com/Heritina-sys/ZUtility.git
cd ZUtility
dotnet restore
dotnet run --project ZKUtility
```

Swagger UI is served in development at the URL printed on startup.

**Before the first run**, read [Configuration](#configuration) — device
addresses and the ZKAPI endpoint are currently hardcoded, and the defaults only
work on one specific network.

---

## How it works

### Polling

`AttendanceWorker` is a `BackgroundService` that polls all eight terminals
concurrently. Addresses are generated rather than configured:

```csharp
private static readonly (string Ip, int Port)[] Devices = Enumerable
    .Range(35, 8)
    .Select(i => ($"192.168.8.{i}", 4370))
    .ToArray();
```

Each cycle connects, drains the device log buffer, and disconnects. An
unreachable terminal logs a warning and is skipped — one dead device does not
stall the others, and the `finally` block guarantees disconnection even on
error.

### Reading punches

`DeviceService` wraps the COM object. `GetLogs` calls `ReadAllGLogData` to load
the buffer, then drains it record by record with `SSR_GetGeneralLogData`,
yielding `DeviceLog { EnrollNumber, LogTime, VerifyMode }`. Records whose enroll
number is not numeric are skipped.

### Pairing punches into sessions

A terminal emits one row per scan and does not say whether a scan is an arrival
or a departure. `AttendanceService.ProcessLogs` derives that with a per-employee
toggle:

- No previous session, or the last one already closed → this punch **opens** a
  session (`CheckIn`).
- Last session open (has `CheckIn`, no `CheckOut`) → this punch **closes** it.

Two guards filter noise:

| Guard | Rule |
|---|---|
| Replay | Punches at or before the last recorded time are dropped. |
| Double-scan | Punches within 300 s of the last one are dropped. |

A per-device high-water mark keyed by IP means each terminal's cursor advances
independently.

### Pushing

Every session transition fires an HTTP `POST` to ZKAPI, using the terminal's
**enroll number** as the identifier — ZKAPI resolves it to a database key on
arrival. See
[ZKAPI's API reference](https://github.com/Heritina-sys/ZKAPI/blob/master/docs/API.md#post-apiattendance).

---

## REST surface

ZUtility is primarily a background worker; the HTTP endpoints are for
inspection.

| Method | Route | Status |
|---|---|---|
| `GET` | `/api/Device/c` | Works — returns in-memory sessions |
| `POST` | `/api/Device/sync` | **Broken** — see limitation 3 below |
| `GET` | `/api/WeatherForecast` | Template leftover |

---

## Configuration

There is effectively none. Every operational value is compiled in:

| Value | Hardcoded in |
|---|---|
| Device IPs `192.168.8.35`–`.42`, port `4370` | `Workers/AttendanceWorker.cs` |
| ZKAPI push URL `http://localhost:5159/api/Attendance` | `Services/AttendanceService.cs` |
| Double-scan window `300 s` | `Services/AttendanceService.cs` |
| COM password `0` | `Services/DeviceService.cs` |

`appsettings.json` contains only log levels. Deploying to a different network
requires editing source and rebuilding. Moving these into configuration is the
highest-value contribution available — see
[CONTRIBUTING.md](CONTRIBUTING.md#especially-welcome).

---

## Known limitations

Present in the code today, documented rather than left to be discovered.

| # | Issue | Impact |
|---|---|---|
| 1 | **The poll loop delay is 1 millisecond.** `await Task.Delay(1, stoppingToken)` — almost certainly a typo for `1000`. | The worker re-polls eight COM devices as fast as the network allows, forever. Pointless CPU, network and device load. **Fix this before any real deployment.** |
| 2 | **Session state is in-memory and static.** `_data` and `_lastSyncPerDevice` are `private static` fields. | On restart, pairing context is lost: the first punch for a mid-shift employee opens a spurious new session. Worse, the read cursor resets to `DateTime.MinValue`, so every device buffer is re-read from the beginning and re-pushed — and ZKAPI has no idempotency key, so history duplicates. |
| 3 | **`POST /api/Device/sync` always throws.** `DeviceController` declares `private readonly HttpClient _http` but the constructor only assigns `_attendanceService`, leaving `_http` null. | `NullReferenceException` on every call. The automatic push path in `AttendanceService` uses a properly injected client and does work — only this manual endpoint is dead. |
| 4 | **Pushes are fire-and-forget with no retry.** `_ = PushAsync(...)`; failures are logged, never retried. | Any punch pushed while ZKAPI is down is lost permanently — the in-memory session is already marked sent. |
| 5 | **`AttendanceService` is registered twice**, once via `AddSingleton` and once via `AddHttpClient<AttendanceService>()`, then `AddSingleton` again. `DeviceService` is `Scoped` while consumed from a singleton worker scope. | Confusing lifetimes. It happens to work; it is not what anyone intended. |
| 6 | **Large commented-out blocks** remain in `Program.cs`, `AttendanceService.cs` and `AttendanceWorker.cs` — including a previous pairing implementation based on fixed 07:00–08:00 / 16:00–18:00 windows. | The file is roughly twice its useful length. Git already remembers the old version. |
| 7 | **`WeatherForecast` scaffolding** is still present from the project template. | Dead code, live endpoint. |
| 8 | **`Interop.zkemkeeper.dll` and build output were committed** — 61 tracked artefacts, 70% of the repository's weight. | Untracked in September 2026; still in git history. |

---

## Repository layout

```
ZKUtility.slnx
ZKUtility/
├── Program.cs                     Host, DI registration
├── Workers/
│   └── AttendanceWorker.cs        BackgroundService — the poll loop
├── Services/
│   ├── DeviceService.cs           zkemkeeper COM wrapper
│   └── AttendanceService.cs       Punch pairing, dedup, push to ZKAPI
├── Controllers/
│   ├── DeviceController.cs        Inspection endpoints
│   └── WeatherForecastController.cs   ← template leftover
├── Models/
│   ├── DeviceLog.cs               One raw punch from a terminal
│   └── Attendance.cs              One paired session
└── ZKUtility.csproj               PlatformTarget=x86, COMReference
docs/
├── ARCHITECTURE.md                Platform data flow, state ownership
└── DEVICE-SDK.md                  Registering zkemkeeper, verify modes
```

---

## Documentation

| Document | Contents |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Three-repository data flow, pairing logic, state ownership |
| [`docs/DEVICE-SDK.md`](docs/DEVICE-SDK.md) | SDK registration, COM troubleshooting, verify-mode values |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Branching, commits, what is most wanted |
| [`SECURITY.md`](SECURITY.md) | Reporting, and the biometric-data considerations |
| [`CHANGELOG.md`](CHANGELOG.md) | Release history |

---

## License

[MIT](LICENSE).
