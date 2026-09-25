# Change notes

## V1 implementation and completion

Problem: no unified view of Windows software inventory, update evidence and personal categories.

Implemented native inventory providers, structured WinGet correlation, SQLite snapshot/preferences, WPF/Blazor UI and while-open refresh. No software mutation API is exposed.

Cause/fix records:
- Blank WebView: internal navigation host allowlist missed the framework host; allow only its internal addresses.
- Persistent loading message: Blazor autostart had been disabled; enable startup per the official WPF template.
- WinRT iteration failed: use indexed collection access for the installed projection.
- Machine apps appeared unsupported: map WinGet System scope to Windows Machine, with regression coverage.
- Duplicate current-user entries: compare shared registry-view identity/version/publisher before suppressing the duplicate.
- Battle.net categorized as Coding: give gaming rules priority and require token boundaries for Git/.NET.

Verification: Release compilation and 26 behavioral checks passed. Earlier native UI checks demonstrated rendering, search, empty state and expanded evidence. Final packaging/UI evidence is recorded in TEST-PLAN.
## 2026-09-16 — V2 action boundary

- Cause: V2 needs a separate, approval-gated path for changing installed software.
- Fix: Added `UpdateRequest`, `UpdateActionResult`, `IUpdateExecutor` and `UpdateService`. Requests are rejected unless the app identity, package ID, source, installed version and target version exactly match current evidence and an approval token is present. A single action gate prevents overlapping updates.
- Verification: `dotnet run --project tests/PcDashboard.Tests/PcDashboard.Tests.csproj --no-restore` — 29 behavioral checks passed.

## 2026-09-17 — full review corrections

- Problem: the initial V2 approval object never expired, documentation still called V2 wholly unimplemented, and the documented default parallel solution command can fail without useful diagnostics.
- Cause: the first boundary validated future timestamps but omitted an age limit; V2 code and documentation landed in the same commit without phase reconciliation; WPF/MSBuild parallel solution orchestration is unreliable for this workflow.
- Change: reject approvals older than five minutes; add expired, changed-install and overlapping-action coverage; update the phase, ADR, runbook and test evidence; use serialized solution restore/build commands.
- Verification: 33 behavioral checks passed; serialized Release solution build passed with zero warnings/errors. Dependency vulnerability and new native UI checks remain unverified.


## 2026-09-22 — Public baseline privacy

- Problem: tracked verification notes contained real inventory counts; logs and probe exports could persist private data outside SQLite.
- Change: remove personal evidence, replace file logging with bounded SQLite diagnostics, disable command-line exports, and import selected legacy evidence before removing source files. Keep runtime profiles explicitly documented. Map compiler source paths to a neutral root.
- Compatibility: diagnostic tables are additive; snapshot and category data are retained. Older binaries do not enforce this privacy boundary.
- Verification: locked restore and serialized Release build succeeded; 41 behavioral checks passed, including retention, legacy import retry, lock failures and no fallback files. NuGet vulnerability checks were unavailable (NU1900). Native UI verification remains outstanding.
