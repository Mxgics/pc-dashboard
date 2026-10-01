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

## 2026-09-29 — V2 immutable update plans and action recovery

- Problem: the provisional V2 request mixed approval with execution input, did not freeze installer/elevation/agreement metadata, and kept neither durable action state nor a shared refresh/mutation gate.
- Cause: the first boundary proved explicit approval and exact evidence matching before the structured WinGet execution contract was designed.
- Change: replace requests with five-minute immutable plans and digest-bound approvals; resolve twice and reject drift/tampering; require agreement acceptance; share one non-blocking operation coordinator across refresh and mutation; add SQLite schema 2 with 100 bounded action records and startup interruption recovery.
- Rationale: reviewed facts remain distinct from live execution facts, concurrent operations fail closed, and a terminated process cannot leave a durable action marked running.
- Verification: locked restore, serialized Release build and 51 behavioral checks passed. NuGet vulnerability lookup remained unavailable with NU1900 warnings. No executor or update UI is registered, so native update execution was not tested.

## 2026-09-29 — V2 structured WinGet execution

- Problem: the approval core had no native resolver/executor, explicit cancellation boundary, result mapping or post-action verification.
- Change: add a structured `Microsoft.Management.Deployment` adapter that resolves one exact source/package/version/scope, rechecks installer/elevation/agreement details, reports progress, forwards cancellation only before installation, maps native outcomes, and always requests fresh inventory after an invoked terminal result. Register the resolver, executor, verifier and update service while keeping the UI read-only.
- Safety: no shell command, retry, rollback, automatic reboot or unattended trigger exists. Native success becomes `SucceededUnverified` when fresh inventory cannot confirm the target version.
- Verification: locked restore and serialized Release build passed; 58 behavioral checks passed. NU1900 remained because vulnerability data was unavailable. No host software was changed; disposable-VM execution and approval UI checks remain unverified.

## 2026-09-29 — V2 explicit approval UI

- Problem: registered execution services had no user-reachable approval surface, progress ownership, agreement gate or accessible focus lifecycle.
- Change: add per-row **Review update**, a native modal containing the complete frozen plan, separate agreement acceptance, UAC/cancellation/restart guidance, one global progress/result card, and disabled conflicting refresh/review controls. Cancel/Escape returns focus to the row; approval moves it to the operation heading.
- Responsive/accessibility: preserve table scrolling, stack dialog metadata/actions at 560 pixels, respect reduced motion, use native modal focus containment, labelled controls and polite status announcements.
- Verification: locked restore and serialized Release build passed; the existing 58 behavioral checks remained green. A hidden WPF startup smoke stayed responsive. Representative desktop/420-pixel browser and accessibility-tree review passed. Native keyboard/screen-reader testing and all real update outcomes remain unverified pending the disposable VM; no host software was changed.

## 2026-09-29 — V3 reviewed setup profiles

- Problem: repeatable workstation setup needs saved intent without turning a profile into standing authorization or weakening the V2 execution boundary.
- Change: add SQLite schema 3 profile CRUD; source-qualified WinGet search; exact user/machine scope choices; fresh `Current`/`Install`/`Update`/`Blocked` previews; five-minute digest-bound approval with complete agreements; immediate per-item revalidation; and sequential install/update execution through the shared structured WinGet path. Add the management, review, progress and stop-condition UI to the existing global-operation surface.
- Safety: search and persistence cannot execute software. Each run needs explicit approval and stops on drift, blocked resolution, failure, cancellation, uncertain interruption or restart requirement. No retry, rollback, automatic reboot, unattended trigger or shell command was introduced.
- Verification: locked restore and serialized Release build passed; 68 behavioral checks passed, including schema migration/CRUD, all four preview classifications, exact-scope validation, agreement gating, per-item revalidation, drift rejection and restart stop. Representative desktop/420-pixel review and accessibility-tree inspection passed. Native profile UI/accessibility and disposable-VM package journeys remain unverified; no host package was changed.

## 2026-10-01 — V3 temporary-file cleanup

- Problem: temporary-file deletion must not broaden from a reviewed list, follow links, remove recently changed files or hide partial failure.
- Change: add a bounded canonical `%TEMP%` scan that skips links/reparse points, inaccessible entries and files younger than seven days; selects nothing by default; reports incomplete coverage; and creates a five-minute digest-bound subset. Revalidate root, canonical path, regular parent chain, age, size, timestamp and reparse state before each deletion. Continue isolated failures, cancel between files, remove only resulting empty candidate-parent directories and report exact counts/bytes.
- UI: add scan/loading/empty/incomplete states, per-file selection, complete deletion review, irreversible-action acknowledgement, focus restoration and the existing global operation/cancellation card.
- Verification: locked restore and serialized Release build passed; 73 behavioral checks passed, including age filtering, empty selection, drift skip, isolated locked-file failure, exact byte accounting, tamper rejection, cancellation and safe empty-directory removal. Representative desktop/420-pixel modal and browser accessibility-tree review passed. Native UI/accessibility and external disposable-fixture checks remain unverified.
