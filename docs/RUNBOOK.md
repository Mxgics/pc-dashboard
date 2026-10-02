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

## V2 — reviewed update flow (release verification pending)

The immutable plan/approval contracts, schema 2 action history, shared refresh/mutation gate and structured WinGet resolver/executor/verifier back one explicit UI journey. Refresh first, choose **Review update** on an available row, inspect package/source/version/scope/installer/elevation and every agreement, then accept displayed agreements separately before choosing **Approve and update**. Plans expire after five minutes and are rejected for tampering, changed identity/source/version/resolution, unaccepted agreements, or overlapping work. The most recent 100 action records are retained; a record left planned or running is marked interrupted when the store next opens.

Execution is constrained to one exact configured source, package ID, version and user/machine scope through the WinGet COM API. A machine-scope installer may show UAC; read that Windows prompt before allowing it. Queued/download cancellation prevents installation; the global card disables cancellation once the installer starts because WinGet cannot stop or roll back that phase. The adapter never retries, rolls back or reboots automatically. Every terminal native result triggers inventory/update refresh, and native success remains `SucceededUnverified` unless fresh inventory confirms the target version. If restart is required, save work and restart manually when ready.

Do not promote this build for host use until user-scope, machine-scope/UAC, cancellation, failure and restart-required journeys pass in a user-authorized disposable Windows VM. Native keyboard and screen-reader checks also remain release gates. Inventory evidence never authorizes execution.

## V3 — setup profiles (release verification pending)

Create or edit a profile, search WinGet, then add a result for an exact user or machine scope. Saving changes only local configuration; it never installs software. Choose **Review setup** to resolve every saved item. Do not approve a plan until every `Install` or `Update` item has the expected package ID, source, scope, target, installer, elevation and agreements. `Current` items are skipped. `Blocked` items disable approval and must be fixed or removed.

Approved items run sequentially through structured WinGet. Each item is resolved again immediately before it starts; any changed metadata rejects the plan. Cancellation can stop queued/download work and stops the sequence before the next item, but becomes ineffective after an installer begins. Failure, uncertain interruption and restart requirement also stop the sequence. Save work and restart manually when requested. There is no automatic retry, rollback or reboot. A machine-scope item may show UAC; verify the Windows prompt.

Schema 3 stores profiles in `%LOCALAPPDATA%/PcDashboard/dashboard.db`. Preserve the entire closed-app data directory before manual recovery. Do not downgrade to a schema-2 binary after opening schema 3. Before release, verify create/edit/delete/search, plan review, keyboard/focus/screen-reader behavior, user-scope install, machine-scope/UAC, update, cancellation, drift, failure and restart-required stops in a user-authorized disposable VM. No real setup execution is permitted on the host as a substitute.

### Temporary-file cleanup

Choose **Scan temporary files** to inspect only the canonical current-user `%TEMP%` directory. The scan never follows links/reparse points, excludes files younger than seven days and selects nothing automatically. An incomplete-scan warning means at least one entry was inaccessible or the 50,000-candidate safety limit was reached; unlisted entries cannot be selected or deleted.

Select files individually, choose **Review selected files**, verify the exact root, paths, sizes and timestamps, acknowledge that deletion bypasses the recycle bin, then approve within five minutes. Every file and its directory chain are revalidated immediately before deletion. A changed file is skipped. An inaccessible or failed file is counted while later files continue. Cancel stops before the next file. The result card reports exact deleted, skipped and failed counts plus bytes deleted. Only empty parent directories of successfully deleted files are considered for removal.

For a failed/partial cleanup, preserve the result counts, rescan, and inspect the newly resolved list; do not assume a retry is safe or reconstruct an old approval. There is no rollback. Native keyboard/screen-reader checks and disposable fixtures covering inaccessible entries, reparse points, drift, cancellation and partial deletion remain release gates. Never test deletion against a substituted host folder or broaden the configured root.

### Encrypted backup and restore

Choose a destination ending in `.pcdbak`, enter and confirm a passphrase, then create the backup. The archive contains categories, aliases, assignments and setup profiles only. It does not contain inventory, update evidence, action history, diagnostics, the WebView2 profile or application binaries. The passphrase is not stored and cannot be recovered. Keep the archive and passphrase separately; do not commit either to source control. A successful overwrite atomically replaces the destination with encrypted bytes and creates no plaintext temporary file.

To restore, choose an archive, enter its passphrase and inspect the authenticated preview counts. Approval re-reads the exact archive, creates a uniquely named encrypted safety backup under `%LOCALAPPDATA%/PcDashboard/safety-backups`, then replaces all four supported data groups in one SQLite transaction. Cancellation or failure before commit retains the prior database values. After success, the app reloads settings and refreshes inventory because inventory is intentionally not restored. Preserve the reported safety-backup filename until the restored settings have been checked.

Wrong passphrases and changed/tampered archives intentionally show the same generic error. An unsupported archive version is not migrated automatically. Do not rename arbitrary files to `.pcdbak`, edit archive bytes, reuse a safety backup without preview, or claim the encrypted safety archive is a full-PC/database rollback. If restore fails, keep the original archive and safety backup, close the app, preserve the whole local data directory, and investigate disk space, permissions and schema compatibility before retrying. Native file-picker, keyboard, focus and screen-reader checks plus an external recovery drill remain release gates.

Legacy diagnostics.log is imported transactionally into legacy_diagnostics on startup, then removed. Failed imports preserve the original and stop startup with a generic error. Older probe reports must be explicitly imported using SqliteDiagnostics.ImportLegacyFile into a private database before removing them; there is no export or general migration CLI in the shipped app. Never publish those reports. Legacy records preserve complete evidence separately from the rolling 256 diagnostic entries (4,096 characters each). SQLite is not encrypted. WebView2 maintains its own browser profile outside SQLite; this is runtime-managed data, not application inventory storage.
