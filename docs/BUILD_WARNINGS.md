# Remaining Build Warnings

## Fixed (57 → 1)

All code warnings have been fixed except the one listed below.

## Remaining Warning

### 1. CS0618: `OpenFolderDialog` is obsolete — `MainView.axaml.cs:48`

**Warning:** `'OpenFolderDialog' is obsolete: 'Use Window.StorageProvider API or TopLevel.StorageProvider API'`

**File:** `AvaloniaApp/AvaloniaApp/Views/MainView.axaml.cs`

**Current code:**
```csharp
var dialog = new OpenFolderDialog { Title = "Select a folder" };
var result = dialog.ShowAsync(mainWindow);
var path = await result;
```

**Suggested fix:** Migrate to the `StorageProvider` API:
```csharp
var topLevel = TopLevel.GetTopLevel(this);
if (topLevel == null) return;

var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
    new FolderPickerOpenOptions
    {
        Title = "Select a folder",
        AllowMultiple = false
    });

if (folders.Count == 0) return;
var path = folders[0].Path.LocalPath;
```

**Why not auto-fixed:** This is an API migration that changes the method signature and return type. It requires:
- Adding `using Avalonia.Platform.Storage;`
- Testing that the folder picker works correctly on all platforms (Windows, macOS, Linux)
- The `StorageProvider` API returns `IStorageFolder` objects instead of plain strings, so downstream code may need adjustment
