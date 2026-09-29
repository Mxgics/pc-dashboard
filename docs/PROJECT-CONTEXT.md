# Project context

## Current state

V1 is implemented and release-verified as a personal Windows desktop application. It uses .NET 10, WPF and Blazor Hybrid with SQLite for the latest inventory snapshot and category preferences. V2 now has an immutable-plan boundary, shared operation coordinator, schema 2 action recovery, structured WinGet resolution/execution/verification services and an explicit per-row approval UI. The app still exposes no uninstall, web server, account, background service, telemetry backend or unattended software change.

The packaged framework-dependent release was verified at `artifacts/win-x64/PcDashboard.Desktop.exe`, with its adjacent dependencies and README. It requires Windows 11 x64 build 26100+, .NET 10 Desktop Runtime and WebView2. No administrator rights are required.

## Accepted V1 behaviour

- Inventory comes from both Windows registry views and current-user packaged apps. Shared current-user registry registrations are deduplicated; side-by-side applications remain distinct.
- Structured WinGet results provide evidence-backed update states. A differing version string alone never establishes an update; `None reported`, `Unknown`, `Unsupported` and source failures stay explicit.
- Cached data is shown at startup. Inventory refreshes on launch and every 15 minutes while open; update freshness is evaluated hourly with a 24-hour successful update cache. One refresh runs at a time; cancellation and provider failures retain the previous complete snapshot.
- Categories are editable and persistent, with deterministic suggestions only as a fallback.

## Verification and known limits

Historical behavioral, native provider and packaged UI checks covered launch, refresh, keyboard search, filtering, empty state and evidence expansion. Current verification and limitations are recorded in TEST-PLAN.md. Personal machine evidence is not retained in public documentation.

A representative browser review covered the real styles and approval markup at desktop and 420-pixel widths, including modal isolation, accessible names, initial heading focus and responsive stacking. The Release WPF build also passed a hidden startup/responding smoke test. A native keyboard/screen-reader pass, long-duration timer soak and disposable-VM execution scenarios remain unverified. V3 cleanup/setup/backups remain incomplete and are not authorization to change software.

See the [project plan](PROJECT-PLAN.md), [test evidence](TEST-PLAN.md), [change notes](CHANGE-NOTES.md) and [runbook](RUNBOOK.md) for detail.
