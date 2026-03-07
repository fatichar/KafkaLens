# AGENTS.md

Canonical reference for all AI agents working on KafkaLens (Claude, Codex, Gemini, etc.).
Module-level detail lives in sub-folder `agents.md` files — see links below.

---

## Project Overview

KafkaLens is a cross-platform desktop Kafka browser and message inspector.
Built with .NET 10 and Avalonia UI, targeting Windows, Linux, macOS (and experimentally Android/Browser).

## Repository Layout

```
AvaloniaApp/
  AvaloniaApp/          Views, converters, ViewLocator  → see AvaloniaApp/agents.md
  AvaloniaApp.Desktop/  Desktop entry point, publish scripts
  AvaloniaApp.Android/  Android target
  AvaloniaApp.Browser/  WASM/Browser target
Core/                   Kafka consumer infrastructure    → see Core/agents.md
ViewModels/             MVVM ViewModels                  → see ViewModels/agents.md
GrpcApi/                gRPC server (Kafka-facing)
GrpcClient/             gRPC client library
RestApi/                REST API (alternative backend)
RestClient/             REST client library
LocalClient/            Local saved-messages client
Shared/                 Shared models, interfaces, data-access abstractions
Formatting/             Message key/value formatters
Updater/                Auto-update service
Installer/              Windows Inno Setup scripts
docs/                   Static docs and design notes
*.Tests/                xUnit test projects (one per module)
```

## Tech Stack

- .NET 10 (`net10.0`), see `global.json`
- Avalonia UI (MVVM)
- CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `IMessenger`)
- Confluent.Kafka
- gRPC (Grpc.AspNetCore / Grpc.Net.Client)
- Serilog (logging throughout — use `Serilog` not `Microsoft.Extensions.Logging` directly)
- Microsoft.Extensions.DependencyInjection
- xUnit (testing)

## Build & Test

```bash
# Build
dotnet build KafkaLens.slnx -c Release

# Test
dotnet test KafkaLens.slnx -c Release
```

Do **not** use the `AvaloniaApp.sln` — use `KafkaLens.slnx`.

## Architecture Guidelines

- Follow MVVM strictly: Views → ViewModels → Core/Clients.
- No business logic inside Views.
- Keep ViewModels UI-agnostic except for `Dispatcher.UIThread` calls.
- Prefer composition over inheritance.
- Avoid static/shared mutable state unless absolutely necessary.
- Maintain existing folder and namespace conventions.

## Async & Threading

- UI updates must run on `Dispatcher.UIThread`.
- Avoid blocking calls (`.Result`, `.Wait()`).
- Prefer `async/await` end-to-end.
- For async-safe locking prefer `SemaphoreSlim.WaitAsync()` over `lock`.
- All background operations must accept and honour `CancellationToken`.

## Performance Considerations

- Assume large topics (millions of messages) and high throughput.
- Avoid unnecessary allocations in hot paths.
- Use UI virtualization for large lists.
- Stream or paginate message fetches — never load everything at once.

## Versioning & Packaging

- Windows installer: Inno Setup (`Installer/install_windows.iss`).
- **Version in `Directory.Build.props` must always match `install_windows.iss`.**
- Other platforms: zip distribution via publish scripts.
- Icons and assets must be embedded as Avalonia resources.

## Communication & Workflow Rules

- **Discuss before doing.** Answer questions and propose an approach before writing code.
- **Do not run the application.** The user runs it themselves.
- **Verify before editing.** Read a file before modifying it.

## Change Approval Policy (Mandatory)

All agents MUST obtain explicit approval before implementing any non-trivial change.

Non-trivial changes include:
- Architectural modifications
- Public API changes
- Adding or removing dependencies
- Cross-cutting refactors
- Threading model changes
- Persistence or serialization changes
- Build, CI/CD, or packaging modifications
- Changes affecting multiple modules

Before implementation, provide a concise proposal:
1. Problem statement
2. Proposed approach
3. Alternatives considered (brief)
4. Impacted components/modules

Do not proceed until clear approval is granted.

## Known Design Notes & Tech Debt

- **Concurrent fetch plan:** `docs/concurrent-fetch-plan.md` — consumer pool refactor for
  concurrent multi-tab fetches on the same cluster. Do not add new locking patterns that
  conflict with this plan.
