# Project context

## Current state

V1 is implemented and release-verified as a personal Windows desktop application. V2 adds reviewed WinGet updates. V3 adds schema-3 setup profiles, reviewed temporary-file cleanup, authenticated encrypted backup/restore for categories, aliases, assignments and setup profiles only, and integrated landmark navigation with one shared foreground-operation model. The app still exposes no uninstall, arbitrary cleanup root, plaintext export, web server, account, background service, telemetry backend or unattended software change.

The packaged framework-dependent release was verified at `artifacts/win-x64/PcDashboard.Desktop.exe`, with its adjacent dependencies and README. It requires Windows 11 x64 build 26100+, .NET 10 Desktop Runtime and WebView2. No administrator rights are required.

## Accepted V1 behaviour

- Inventory comes from both Windows registry views and current-user packaged apps. Shared current-user registry registrations are deduplicated; side-by-side applications remain distinct.
- Structured WinGet results provide evidence-backed update states. A differing version string alone never establishes an update; `None reported`, `Unknown`, `Unsupported` and source failures stay explicit.
- Cached data is shown at startup. Inventory refreshes on launch and every 15 minutes while open; update freshness is evaluated hourly with a 24-hour successful update cache. One refresh runs at a time; cancellation and provider failures retain the previous complete snapshot.
- Categories are editable and persistent, with deterministic suggestions only as a fallback.

## Verification and known limits

Historical behavioral, native provider and packaged UI checks covered launch, refresh, keyboard search, filtering, empty state and evidence expansion. Current verification and limitations are recorded in TEST-PLAN.md. Personal machine evidence is not retained in public documentation.

Representative browser review has covered the V2, setup-profile, cleanup, backup/restore and integrated navigation surfaces; native keyboard/screen-reader checks and disposable-VM setup journeys remain unverified. Cleanup automation has run only against an isolated generated test directory; the host's real TEMP files were not scanned or deleted. Native cleanup UI and live link/inaccessible-entry race fixtures remain unverified. Backup/restore automation uses only an isolated generated database and files; native file-dialog/modal accessibility and an external safety-backup recovery drill remain unverified. No personal dashboard backup was created or restored. The source-stack audit passed, but a release/promotion remains blocked by PUBLIC-RELEASE.md and the outstanding native/VM gates.

See the [project plan](PROJECT-PLAN.md), [test evidence](TEST-PLAN.md), [change notes](CHANGE-NOTES.md) and [runbook](RUNBOOK.md) for detail.
