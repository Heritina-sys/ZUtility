# ZKTeco device SDK

Everything about talking to the terminals: registering the COM library, the
calls ZUtility uses, and what goes wrong.

---

## Why COM, and why x86

`zkemkeeper` is ZKTeco's standalone SDK — a **32-bit Windows COM library**. There
is no HTTP API on these terminals, no cross-platform SDK, and no supported way
to read the punch buffer without it.

Consequences, in order of how much they hurt:

1. **ZUtility only runs on Windows.** The `COMReference` in `ZKUtility.csproj`
   cannot resolve on Linux or macOS. `dotnet build` fails there — this is
   expected, not a misconfiguration.
2. **The process must be 32-bit.** `<PlatformTarget>x86</PlatformTarget>` is set
   for this reason. Removing it produces a `BadImageFormatException` at runtime,
   not a build error.
3. **The SDK must be registered on the machine**, not merely present. It is a
   machine-level dependency that no package manager tracks.

This is the entire reason the platform is split across three repositories. See
[`ARCHITECTURE.md`](ARCHITECTURE.md).

---

## Installing and registering

### 1. Obtain the SDK

Download the **ZKTeco Standalone SDK** from ZKTeco's official developer
downloads. Do not commit it to this repository — it is a third-party
redistributable with its own licence terms.

### 2. Register the COM library

From an **elevated** command prompt:

```cmd
cd C:\path\to\zkemkeeper
regsvr32 zkemkeeper.dll
```

Some SDK packages ship an installer that registers every dependency
(`commpro.dll`, `comms.dll`, `rscagent.dll`, `tcpcomm.dll`, `usbcomm.dll`,
`zkemsdk.dll`). Prefer the installer where one is provided — registering only
`zkemkeeper.dll` by hand can leave transport DLLs unregistered, which surfaces
later as a connection that silently fails.

### 3. Verify registration

```cmd
reg query HKCR\CLSID\{00853A19-BD51-419B-9269-2DABE57EB61F}
```

That CLSID is `CZKEM`, the class ZUtility instantiates. If the key is absent,
registration did not take.

### 4. Build

```powershell
dotnet restore
dotnet build
```

`tlbimp` generates `Interop.zkemkeeper.dll` from the registered type library at
build time. It is machine-specific and **must not be committed** — `.gitignore`
excludes `Interop.*.dll`.

The COM reference in `ZKUtility.csproj`:

```xml
<COMReference Include="zkemkeeper">
  <WrapperTool>tlbimp</WrapperTool>
  <Guid>fe9ded34-e159-408e-8490-b720a5e632c7</Guid>
  <VersionMajor>1</VersionMajor>
  <VersionMinor>0</VersionMinor>
  <EmbedInteropTypes>true</EmbedInteropTypes>
</COMReference>
```

---

## The SDK surface ZUtility uses

Four calls, all in `Services/DeviceService.cs`.

### `SetCommPassword(int)`

Sets the device communication password before connecting. ZUtility passes `0`
(no password).

```csharp
_zk.SetCommPassword(0);
```

If a terminal has a comm password configured in its own menu, connection fails
with no useful error until this matches.

### `Connect_Net(string ip, int port)`

Opens a TCP connection. Returns `false` on failure — it does not throw.

```csharp
return _zk.Connect_Net(ip, port);   // port 4370
```

`AttendanceWorker` treats `false` as "skip this device this cycle" and logs a
warning.

### `ReadAllGLogData(int machineNumber)`

Loads the device's general log (all attendance records) into the SDK's internal
buffer. Returns `false` if there is nothing to read.

Nothing is transferred to your process by this call — it only fills the buffer
that the next call drains.

### `SSR_GetGeneralLogData(...)`

Drains one record per call, returning `false` when the buffer is empty. Twelve
parameters, mostly `out`:

```csharp
while (_zk.SSR_GetGeneralLogData(
    machineNumber,
    out enrollNumber,   // string — the enrolled user ID
    out verifyMode,     // int    — how they were identified
    out inOutMode,      // int    — device's own in/out flag (unused here)
    out year, out month, out day,
    out hour, out minute, out second,
    ref workCode))
```

Two things worth knowing:

- **`enrollNumber` is a string.** ZUtility parses it with `uint.TryParse` and
  skips records that fail — some terminals allow alphanumeric IDs.
- **`inOutMode` is ignored.** The device's own in/out flag is unreliable in
  practice (it depends on per-device configuration that is easy to get wrong),
  so ZUtility derives direction itself from punch ordering. See
  [`ARCHITECTURE.md § 3②`](ARCHITECTURE.md#-pairing-punches-into-sessions--zutility).

### `Disconnect()`

Always called from a `finally` block, so a mid-read exception cannot leak the
connection.

---

## `machineNumber`

Every call takes a `machineNumber`. ZUtility passes `1` everywhere.

This is a device-local identifier, meaningful when several terminals share one
serial bus. Over TCP each device is addressed by IP, so `1` is correct for this
topology — the eight terminals are distinguished by address, not by machine
number.

---

## Verify modes

`verifyMode` is how the person was identified. Stored as `CheckInMode` /
`CheckOutMode` and mirrored in FrontZK's `checkModeLabels`.

| Value | Method |
|---|---|
| `0` | Fingerprint |
| `1` | Card |
| `2` | Password |
| `3` | Face |
| `4` | Manual entry |

Nothing validates the range. Values outside it are stored as-is and will render
as blank in the dashboard.

---

## Troubleshooting

### `Retrieving the COM class factory ... failed`

The SDK is not registered, or is registered for the wrong bitness. Re-run
`regsvr32` from an elevated prompt. On 64-bit Windows, a 32-bit COM DLL must be
registered by the 32-bit `regsvr32` at `C:\Windows\SysWOW64\regsvr32.exe`.

### `BadImageFormatException` at startup

The process is running 64-bit. Confirm `<PlatformTarget>x86</PlatformTarget>` in
`ZKUtility.csproj` and that your run configuration honours it.

### `Connect_Net` returns `false`

In the order worth checking:

1. `ping <device-ip>` — is it on the network at all?
2. `Test-NetConnection <device-ip> -Port 4370` — is the port open?
3. Comm password — does the device expect a non-zero one?
4. Concurrent connections — most terminals accept a limited number, and a stale
   session from a crashed process can hold a slot until it times out.

### `ReadAllGLogData` returns `false` every cycle

Usually correct behaviour: the buffer is empty because every punch has already
been read. Confirm by punching on the device and polling again.

### Interop DLL not found after clone

Expected. `Interop.zkemkeeper.dll` is generated at build time and deliberately
untracked. Run `dotnet build` on a machine with the SDK registered.

### Builds fail on Linux or macOS

Expected, and not fixable. The COM reference cannot resolve off Windows. Build
ZKAPI and FrontZK there instead.

---

## Network topology

The current deployment expects eight terminals at consecutive addresses:

| Device | Address | Port |
|---|---|---|
| 1–8 | `192.168.8.35` … `192.168.8.42` | `4370` |

Generated in `AttendanceWorker`:

```csharp
Enumerable.Range(35, 8).Select(i => ($"192.168.8.{i}", 4370))
```

A different network means editing that line and rebuilding. Moving it into
`appsettings.json` is
[the most wanted contribution](../CONTRIBUTING.md#especially-welcome).
