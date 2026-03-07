# ViewModels — Agent Notes

See root [`agents.md`](../agents.md) for project-wide conventions.

## Layer Role

ViewModels are the bridge between the UI (AvaloniaApp) and the domain (Core, clients).
They must remain UI-framework-agnostic except for `Dispatcher.UIThread` usage.

## Toolkit

- `CommunityToolkit.Mvvm` — use `[ObservableProperty]`, `[RelayCommand]`, partial classes.
- `CommunityToolkit.Mvvm.Messaging` — use `IMessenger` for cross-VM communication.
- Serilog for logging (`using Serilog;`), not `ILogger<T>` directly.

## Partial Class Pattern

Large ViewModels are split into multiple files by concern:

```
MainViewModel.cs               # Fields, constructor, core dependencies
MainViewModel.ClusterLoading.cs
MainViewModel.Tabs.cs
MainViewModel.Session.cs
MainViewModel.Menus.cs
MainViewModel.Updates.cs
OpenedClusterViewModel.cs
OpenedClusterViewModel.Fetching.cs
OpenedClusterViewModel.Formatters.cs
OpenedClusterViewModel.Topics.cs
OpenedClusterViewModel.Session.cs
```

When adding behavior to an existing ViewModel, place it in the most relevant partial file or create a new one if the concern is distinct. Never dump everything into the main `.cs` file.

## Key Classes

| Class | Purpose |
|---|---|
| `MainViewModel` | Root VM: clusters, tabs, menus, updates |
| `OpenedClusterViewModel` | An open cluster tab — topics, fetching, formatters |
| `MessagesViewModel` | Message list for a topic/partition |
| `TopicViewModel` / `PartitionViewModel` | Tree node VMs for topics/partitions |
| `ClusterViewModel` / `ClientInfoViewModel` | Sidebar cluster/client nodes |
| `SettingsService` / `TopicSettingsService` | Persistence services |
| `FormatterService` | Message key/value formatting |

## Observable Collections

- Always use `ObservableCollection<T>` for UI-bound lists.
- Mutate collections only on `Dispatcher.UIThread`.

## Commands

- Use `[RelayCommand]` where possible.
- Async commands should return `Task` and accept `CancellationToken`.
- Cancel outstanding commands when the user closes a tab or switches context.

## Testing

Test project: `ViewModels.Tests/`

- Unit tests use xUnit.
- Mock services with interfaces; avoid real Kafka connections.
- Do not depend on `Dispatcher.UIThread` in tests — abstract UI dispatch if needed.
