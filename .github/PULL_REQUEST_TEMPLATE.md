# Summary

<!-- What changes, and why. One or two sentences. -->

Closes #

## Type

- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation
- [ ] Refactor (no behaviour change)
- [ ] Tooling / CI

## Areas touched

- [ ] `DeviceService` — COM interop with `zkemkeeper`
- [ ] `AttendanceService` — punch pairing and dedup
- [ ] `AttendanceWorker` — poll loop
- [ ] Push to ZKAPI
- [ ] Controllers / HTTP surface
- [ ] Configuration
- [ ] Documentation or tooling only

## If you changed the pairing logic

`AttendanceService.ProcessLogs` decides whether a punch opens or closes a
session. A mistake here produces silently wrong attendance — no exception, no
log, just incorrect hours. Confirm the behaviour after your change:

- [ ] Employee punches once and never again that day → ?
- [ ] Punch at 299 s after the previous one → dropped; at 301 s → accepted
- [ ] Punch with a timestamp *earlier* than the last recorded one → ?
- [ ] Service restarts mid-shift → ?

<!-- Answer any that your change affects. -->

## Verification

<!-- What you actually ran. "Should work" is not verification. -->

- [ ] `dotnet build` clean on Windows
- [ ] `dotnet format --verify-no-changes` passes
- [ ] **Tested against real ZKTeco terminals** — how many, which models
- [ ] Tested with terminals unreachable (the skip path)
- [ ] Exercised `ProcessLogs` with hand-built `DeviceLog` input

> CI builds on `windows-latest` but **cannot verify COM interop** — the SDK is
> not registered on GitHub runners. A green build does not prove device code
> works. Say what you tested on real hardware.

## Downstream

ZUtility pushes to [ZKAPI](https://github.com/Heritina-sys/ZKAPI). Nothing
compiles across that boundary.

- [ ] No change to the push payload or target
- [ ] Changed — ZKAPI needs a matching update (describe below)

## Documentation

- [ ] `docs/ARCHITECTURE.md` updated (pairing, data flow, or state ownership changed)
- [ ] `docs/DEVICE-SDK.md` updated (SDK usage or setup changed)
- [ ] `CHANGELOG.md` updated under `## [Unreleased]`
- [ ] A resolved item struck from README § Known limitations
- [ ] No documentation change needed

## Checks

- [ ] No `bin/`, `obj/`, `.vs/`, `Interop.*.dll` or `*.user` files added
- [ ] No credentials or real customer device addresses in the diff
- [ ] Commented-out code deleted rather than added
- [ ] One concern per pull request
