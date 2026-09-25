# PC Dashboard

A local Windows desktop dashboard for understanding installed applications, versions, available updates, and personal categories.

The current UI is deliberately read-only. It runs as the current Windows user, stores its latest snapshot locally, and does not install, update, or remove software. V2's approval and execution contracts are being built behind that UI boundary; no update executor is registered yet.

## Build and verify

Requirements: the .NET SDK pinned in `global.json` and the WebView2 runtime.

```text
dotnet restore PcDashboard.slnx --locked-mode -m:1
dotnet build PcDashboard.slnx -c Release --no-restore -m:1
dotnet run --project tests/PcDashboard.Tests -c Release --no-restore
```

Package locally with `powershell -NoProfile -File scripts/Publish.ps1`, then launch `artifacts/win-x64/PcDashboard.Desktop.exe` with all adjacent files present. Requires Windows 11 x64 build 26100+ and .NET 10 Desktop Runtime. No administrator rights are required.

[Project context](docs/PROJECT-CONTEXT.md) · [Project plan](docs/PROJECT-PLAN.md) · [Architecture decisions](docs/ADRS.md) · [Runbook](docs/RUNBOOK.md) · [Test evidence](docs/TEST-PLAN.md) · [Change notes](docs/CHANGE-NOTES.md)

V2 approved updates are in progress; only the guarded core boundary exists. V3 cleanup/setup/backups remain roadmap only. The app has no telemetry backend; WinGet and WebView2 retain their own diagnostics policies and global settings.

## Privacy

Application inventory, categories and diagnostics stay in `%LOCALAPPDATA%/PcDashboard/dashboard.db`. SQLite is not encrypted. WebView2 maintains a separate local runtime profile. No diagnostic file or probe export is produced. Never publish the database, runtime profile, legacy reports, or unreviewed build artifacts. See the runbook for recovery and migration.

Public-source preparation and remaining publication gates are documented in [PUBLIC-RELEASE.md](docs/PUBLIC-RELEASE.md).
