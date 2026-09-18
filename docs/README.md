# KafkaLens

KafkaLens is a cross-platform desktop client for Apache Kafka, built with .NET and Avalonia.

## Repository Overview

- `AvaloniaApp/AvaloniaApp.Desktop`: Main desktop application entry point and publish scripts.
- `Core`, `GrpcApi`, `RestApi`, `ViewModels`, `Shared`: Core libraries and services used by the app.
- `*.Tests` projects: Unit/integration test projects.
- `Installer`: Windows installer resources (`install_windows.iss`).
- `Releases`: Build artifacts output directory.
- `docs`: Project website assets and static documentation pages.

## Prerequisites

- .NET SDK `10.0.100` (see `global.json`)
- Git
- Bash-compatible shell for release scripts
- Windows installer builds only: Inno Setup 6 (`ISCC.exe`) installed at:
  - `C:/Program Files (x86)/Inno Setup 6/ISCC.exe`

## Build

From the repository root:

```bash
dotnet build KafkaLens.slnx -c Release
```

## Test

```bash
dotnet test KafkaLens.slnx -c Release
```

## Publish Artifacts

Run all platform publish scripts:

```bash
./publish.sh
```

Run a single platform from `AvaloniaApp/AvaloniaApp.Desktop`:

```bash
./publish_windows.sh
./publish_linux.sh
./publish_macos.sh
```

## Release Tagging

Create and push a version tag (after validating branch, clean workspace, and installer/app version sync):

```bash
./release-new-version.sh
```

## Website Docs

The project website is static HTML served via GitHub Pages (custom domain `kafkalens.com`).

- `index.html` — landing page
- `download.html` + `download/{windows,macos,linux}/` — downloads and OS install steps
- `guide/` — user documentation (multi-page guide with a shared `guide/nav.html` sidebar)
- `usage.html` — redirect to `/guide/` (kept for inbound links)
- `navbar.html` / `footer.html` — shared fragments injected by `assets/fragments.js`
- All site-internal URLs are root-absolute (`/guide/...`, `/assets/...`) so pages work at any depth.

The file `docs/README.md` is intended for repository documentation, not website rendering.

## Contributing

1. Create a feature branch from `master`.
2. Keep `Directory.Build.props` and `Installer/install_windows.iss` versions aligned.
3. Run build/tests before opening a PR.
4. Include relevant screenshots or logs for UI/release-related changes.

## License

Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0). See `LICENSE`.