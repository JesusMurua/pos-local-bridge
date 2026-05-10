# pos-local-bridge

Local hardware bridge for POS / gym-reception stations. A .NET 8 Worker Service that runs as a background agent on a client Windows machine and brokers access between web applications and the physical devices attached to that machine — biometric fingerprint scanners, ESC/POS thermal printers, cash drawers, and COM-port turnstiles.

## Quick start

```bash
dotnet build PosLocalBridge.slnx
dotnet run --project PosLocalBridge.Host
```

See [CLAUDE.md](CLAUDE.md) for the architectural rules and project conventions.
