# PC Dashboard

A local Windows desktop dashboard for understanding installed applications, versions, available updates, and personal categories.

The app runs as the current Windows user and stores its latest snapshot locally. V2 adds a per-application **Review update** flow. V3 adds reviewed setup profiles, temporary-file cleanup, and encrypted portable-settings backup/restore. Cleanup scans only the canonical current-user TEMP root, selects nothing by default, and revalidates every explicitly approved file before deletion. Backups contain only categories, aliases, assignments, and setup profiles. No uninstall, unattended change, automatic retry, rollback claim or automatic reboot is provided.

## Build and verify

Requirements: the .NET SDK pinned in `global.json` and the WebView2 runtime.

```text
dotnet restore PcDashboard.slnx --locked-mode -m:1
dotnet build PcDashboard.slnx -c Release --no-restore -m:1
dotnet run --project tests/PcDashboard.Tests -c Release --no-restore
```

Package locally with `powershell -NoProfile -File scripts/Publish.ps1`, then launch `artifacts/win-x64/PcDashboard.Desktop.exe` with all adjacent files present. Requires Windows 11 x64 build 26100+ and .NET 10 Desktop Runtime. No administrator rights are required.

[Project context](docs/PROJECT-CONTEXT.md) · [Project plan](docs/PROJECT-PLAN.md) · [Architecture decisions](docs/ADRS.md) · [Runbook](docs/RUNBOOK.md) · [Test evidence](docs/TEST-PLAN.md) · [Change notes](docs/CHANGE-NOTES.md)

V2 approved updates plus the V3 setup-profile, temporary-cleanup, and encrypted backup/restore layers are implemented but not release-promoted: disposable-VM execution, native accessibility and final public-release gates remain. The app has no telemetry backend; WinGet and WebView2 retain their own diagnostics policies and global settings.

## Privacy

Application inventory, categories and diagnostics stay in `%LOCALAPPDATA%/PcDashboard/dashboard.db`. SQLite is not encrypted. User-created `.pcdbak` files and automatic pre-restore safety backups are encrypted and authenticated, but their passphrases cannot be recovered. WebView2 maintains a separate local runtime profile. No diagnostic file or probe export is produced. Never publish the database, runtime profile, backup passphrases, legacy reports, or unreviewed build artifacts. See the runbook for recovery and migration.

Public-source preparation and remaining publication gates are documented in [PUBLIC-RELEASE.md](docs/PUBLIC-RELEASE.md).
