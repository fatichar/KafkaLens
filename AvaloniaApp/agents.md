# AvaloniaApp — Agent Notes

See root [`agents.md`](../agents.md) for project-wide conventions.

## Projects in This Folder

| Project | Purpose |
|---|---|
| `AvaloniaApp/` | Shared UI: Views (axaml), converters, ViewLocator |
| `AvaloniaApp.Desktop/` | Desktop entry point; publish scripts |
| `AvaloniaApp.Android/` | Android target |
| `AvaloniaApp.Browser/` | Browser/WASM target |

## Views

- Views live in `AvaloniaApp/Views/` as `.axaml` + `.axaml.cs` pairs.
- Code-behind (`.axaml.cs`) should only contain UI wiring — no logic.
- All data binding goes through ViewModels. Never manipulate data in a View.

## ViewLocator

`ViewLocator.cs` maps ViewModel types to View types by naming convention:
`*ViewModel` → `*View`. Follow this convention for new VM/View pairs.

## Dispatcher

Any ViewModel or service that must update UI state from a background thread must use:
```csharp
await Dispatcher.UIThread.InvokeAsync(() => { ... });
```

## Converters

Custom value converters live in `AvaloniaApp/` root:
- `NodeTypeConverter.cs`
- `FetchPositionConverter.cs`

Add new converters here following the same pattern.

## Dialogs

Modal dialogs (e.g., `AddEditClusterDialog`, `UpdateDialog`) are defined as Views and opened
from ViewModels via a service abstraction — do not open dialogs directly from code-behind.

## Assets & Packaging

- Icons and assets must be embedded as Avalonia resources, not loose files.
- Windows installer is built with Inno Setup (`Installer/install_windows.iss`).
- **Version in `Directory.Build.props` must always match `install_windows.iss`.**
- Publish scripts: `publish_windows.sh`, `publish_linux.sh`, `publish_macos.sh` (in `AvaloniaApp.Desktop/`).
