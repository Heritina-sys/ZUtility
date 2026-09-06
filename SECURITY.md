# Security policy

## Reporting a vulnerability

Report privately — **do not open a public issue.**

Use GitHub's private vulnerability reporting:
[**Report a vulnerability**](https://github.com/Heritina-sys/ZUtility/security/advisories/new).
If unavailable, contact the maintainer via their
[GitHub profile](https://github.com/Heritina-sys).

Include the affected commit, reproduction steps, and what an attacker gains.
First response within 7 days.

## Supported versions

| Version | Supported |
|---|---|
| `master` (unreleased) | ✅ |
| Tagged releases | ❌ — none published yet |

---

## What this component handles

ZUtility reads **biometric attendance events** off physical terminals: an enroll
number, a timestamp, and the verification method (fingerprint, face, card,
password) used to identify a person.

It does not read, store, or transmit biometric templates — no fingerprint or
face data ever leaves the terminal. What it handles is the *derived* record: proof
that an identifiable person was physically at a specific door at a specific
time. In most jurisdictions that is personal data, and in several it is
specially regulated.

Nothing in this repository implements retention limits, access logging, or
anonymisation. Stated so it is a known gap rather than an assumption.

---

## Known issues in the current design

### 1. Unauthenticated HTTP surface — unresolved

ZUtility registers controllers and Swagger with no authentication scheme.
`GET /api/Device/c` returns every in-memory attendance session to any caller
that can reach the port.

**Mitigation:** bind to `localhost` only. This service has no reason to be
reachable from the network — it initiates outbound connections to the terminals
and to ZKAPI, and needs no inbound traffic beyond local inspection.

### 2. Attendance data crosses the network in plaintext — environment-dependent

Pushes go to `http://localhost:5159/api/Attendance` over plain HTTP. On a single
host this is contained. If the collector and ZKAPI are ever split across
machines, attendance records — names implied by enroll number, timestamps,
verification methods — cross the network unencrypted.

The push URL is a hardcoded constant in `Services/AttendanceService.cs`, so
switching to HTTPS requires a code change.

### 3. Device communication password is zero — unresolved

`DeviceService.Connect` calls `SetCommPassword(0)`, meaning the terminals are
expected to have no communication password set.

```csharp
_zk.SetCommPassword(0);
```

Anyone with network access to TCP 4370 on a terminal can connect with the same
SDK and read its full punch history, or modify enrolled users. This is a
**device configuration** weakness rather than a code flaw, but the code assumes
and depends on it.

**Mitigation:** put the terminals on an isolated VLAN with no route from general
office traffic. Setting a real comm password requires making it configurable
here first.

### 4. Data loss on push failure — unresolved

Pushes are fire-and-forget (`_ = PushAsync(...)`) with no retry. A punch pushed
while ZKAPI is unavailable is lost: the in-memory session is already marked
sent and will not be re-attempted.

This is a data-integrity issue rather than a classic vulnerability, but for a
system whose output may be used in payroll or workplace disputes, silently
dropped records matter.

### 5. Restart duplicates history — unresolved

`_lastSyncPerDevice` is a static in-memory field. On restart it resets to
`DateTime.MinValue`, so every terminal's buffer is re-read from the beginning
and re-pushed. ZKAPI has no idempotency key on `POST /api/Attendance`, so
duplicate rows are created.

The integrity consequence: attendance history can silently gain duplicate
sessions after any restart, inflating counted hours.

### 6. Build artefacts and interop DLL were committed — resolved

`ZKUtility/bin/`, `ZKUtility/obj/`, `.vs/` (including `.suo`, design-time caches
and Copilot semantic indices) and `*.csproj.user` were tracked — 61 artefacts,
about 70% of the repository's weight. Untracked on 7 September 2026 and now
covered by `.gitignore`. Still present in git history.

No credentials were found in the removed files. Unlike
[ZKAPI](https://github.com/Heritina-sys/ZKAPI/blob/master/SECURITY.md),
this repository's `appsettings.json` never contained a connection string.

---

## Deployment guidance

Given the above, a defensible deployment looks like:

1. Terminals on an **isolated VLAN**, no route from general office traffic.
2. ZUtility on a host in that VLAN, bound to `localhost` for its HTTP surface.
3. ZKAPI reachable from ZUtility, ideally over HTTPS, itself behind
   authentication (see
   [ZKAPI SECURITY.md](https://github.com/Heritina-sys/ZKAPI/blob/master/SECURITY.md)).
4. Swagger disabled outside development.

---

## Reporting scope

In scope: authentication flaws, injection, data exposure, COM interop
vulnerabilities, dependency vulnerabilities with a practical exploitation path.

Out of scope: the issues listed above (report *new* findings), and anything
requiring pre-existing administrative access to the host or the terminals.
