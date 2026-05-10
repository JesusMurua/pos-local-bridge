# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project purpose

`pos-local-bridge` is a long-running local agent that runs on client Windows machines (gym receptions, POS stations). It bridges a browser-based Angular UI (and, later, a cloud API) to physical hardware attached to the same machine:

- Biometric fingerprint scanners (USB)
- ESC/POS thermal printers (USB / network)
- Cash drawers (typically driven through the printer)
- COM-port turnstiles / access control

The bridge is the only component that talks to hardware. Web apps never touch the devices directly.

## Stack and architecture

- **.NET 8 LTS**, C#, `Microsoft.NET.Sdk.Worker`. Do not change the target framework without an explicit ask.
- **Worker Service** model (`IHostedService` / `BackgroundService`). The host runs as a Windows background process; it must survive reboots, sleeps, and device unplug events.
- **Solution layout**: `PosLocalBridge.slnx` at the root, single executable project `PosLocalBridge.Host/`. Additional class libraries (e.g., `PosLocalBridge.Hardware`, `PosLocalBridge.Transport`) should be added as separate projects under the same solution as the surface grows — do not stuff everything into `Host`.
- **Transport (planned)**: SignalR client connecting outbound to the cloud API. The bridge is the client, not a server. Local web pages talk to it via a small local HTTP/WebSocket endpoint or directly through the cloud round-trip — pick per feature, document the choice.
- **Logging (planned)**: Serilog with rolling file sinks under a known local path. Default Microsoft logging is acceptable only until Serilog is introduced.

## Non-negotiable rules

These are the constraints behind this codebase. Violating them produces real outages on machines we cannot SSH into.

1. **Hardware I/O must be wrapped in resilient try/catch.** USB disconnects, COM port `IOException`, printer `PrinterOffline`, and biometric driver faults are *expected operating conditions*, not exceptions. They must never bubble up to crash the host or stop the `BackgroundService`. Log, surface a structured error to the caller, and keep running.
2. **All hardware operations are `async` and non-blocking.** No `Thread.Sleep`, no synchronous `SerialPort.Read`, no blocking USB reads on the host thread. Use `CancellationToken` on every public hardware method and honor it. A single stuck device must not block other devices or the SignalR connection.
3. **Log generously and structurally.** We do not have direct access to client machines. Every hardware attempt, retry, disconnect, and reconnect is logged with device identifier and correlation ID. Prefer structured properties over interpolated strings so logs are queryable once shipped.
4. **`dotnet build` must pass with zero warnings.** Warnings-as-errors is currently a convention, not enforced in the csproj — treat any new warning as a build failure anyway. If a warning is unavoidable, suppress it narrowly with justification in a comment, not project-wide.
5. **Nullable reference types stay enabled.** `<Nullable>enable</Nullable>` is on. Don't disable it per-file to silence warnings — fix the nullability.

## Commands

Run from the repository root.

- Restore + build: `dotnet build PosLocalBridge.slnx`
- Run the worker locally (console mode, Ctrl+C to stop): `dotnet run --project PosLocalBridge.Host`
- Publish a self-contained Windows binary (once we get there): `dotnet publish PosLocalBridge.Host -c Release -r win-x64 --self-contained`
- Tests (no test project yet — add `PosLocalBridge.Tests` with `xunit` when the first non-trivial logic lands)

## Known follow-ups

These are deliberately not done yet; do them when the feature requires them, not preemptively:

- Add `Microsoft.Extensions.Hosting.WindowsServices` and call `UseWindowsService()` to install as a real Windows Service rather than a console app.
- Add Serilog (`Serilog.AspNetCore` or `Serilog.Extensions.Hosting` + file sink).
- Add SignalR client (`Microsoft.AspNetCore.SignalR.Client`) and a reconnection policy that assumes the cloud is sometimes unreachable for hours.
- Add hardware abstraction projects per device family rather than coupling drivers directly to `Worker.cs`.

## What lives where

- `PosLocalBridge.Host/Program.cs` — host bootstrap. Keep it thin: registrations only.
- `PosLocalBridge.Host/Worker.cs` — placeholder `BackgroundService`. Will be split into per-concern hosted services (transport, device supervisors) as features land. Don't grow it into a god-class.
- `PosLocalBridge.Host/appsettings.json` — runtime config. Device serial ports, printer names, and cloud endpoints belong here, not hardcoded.
