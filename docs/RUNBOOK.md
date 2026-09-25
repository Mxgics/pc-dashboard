# Operational runbook

## V1 — implemented

Requirements: Windows 11 x64 build 26100+, .NET 10 Desktop Runtime, Microsoft Edge WebView2 Runtime. Build with the pinned SDK. App Installer/WinGet provides update evidence; its absence must not prevent inventory. No Docker or hosting required. Run as the normal user.

From the repository root:

```powershell
dotnet restore PcDashboard.slnx --locked-mode -m:1
dotnet build PcDashboard.slnx -c Release --no-restore -m:1
dotnet run --project tests/PcDashboard.Tests -c Release --no-restore
powershell -NoProfile -File scripts/Publish.ps1
```

Expected: locked restore succeeds, build has no errors, all behavioral checks pass, and artifacts/win-x64 contains PcDashboard.Desktop.exe plus dependencies. Launch that executable with all adjacent files present. Publishing here means local desktop packaging, not website deployment.

### Refresh / failed sources

Open to see cached inventory. Refresh now forces source checks; Cancel preserves the previous complete snapshot. Closing stops the lifecycle. A failed-source banner means coverage is incomplete; old evidence remains cached. Check network/App Installer from the user's normal session and retry. Never treat Unsupported or None reported as proof that no update exists. Do not automatically reset/install/upgrade WinGet or accept source agreements.

Command-line probe exports are no longer supported. Inspect source health in the app; do not export real inventory into the repository.

### Local data / recovery

Data: `%LOCALAPPDATA%/PcDashboard/dashboard.db`; browser profile: WebView2 subdirectory; bounded warnings/errors: the database diagnostics table. No inventory history or cloud sync.

For startup failure, check runtime prerequisites, data permissions, disk space and database compatibility. A newer-schema error requires the compatible app version. Do not delete/reset data automatically. Before manual recovery, close the app and copy the entire data directory, including any SQLite sidecars, to a user-chosen safe location. Restore only while closed and with a schema-compatible release. Verify categories and refresh after reopening. Resetting the database loses preferences and requires a deliberate user decision.

Rollback: close the app and preserve its data and current release folder. Only run a previous complete release folder if it enforces the same SQLite-only privacy boundary. Never mix DLLs from releases. No third-party software changes occur.

## V2 — foundation only

The core request/result contracts and approval gate are implemented, but no executor or UI is registered and the application still cannot update software. Approvals expire after five minutes and are rejected if identity, source, installed version or target evidence changed, or another action is running. Before release, document and verify structured package resolution, elevation, progress, cancellation limits, restart policy, partial failure and post-update inventory verification. Inventory refresh remains separate from execution.

## V3 — future only

No cleanup/setup/backup engine exists. Before release, document reviewed previews/exclusions, destinations/retention/encryption, tested restores, recovery limits and destructive-action approval. Protecting the dashboard's settings manually is not a PC backup feature.

Legacy diagnostics.log is imported transactionally into legacy_diagnostics on startup, then removed. Failed imports preserve the original and stop startup with a generic error. Older probe reports must be explicitly imported using SqliteDiagnostics.ImportLegacyFile into a private database before removing them; there is no export or general migration CLI in the shipped app. Never publish those reports. Legacy records preserve complete evidence separately from the rolling 256 diagnostic entries (4,096 characters each). SQLite is not encrypted. WebView2 maintains its own browser profile outside SQLite; this is runtime-managed data, not application inventory storage.
