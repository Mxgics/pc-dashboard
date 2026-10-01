# PC Dashboard project plan

## Accepted baseline

Personal Windows desktop app: .NET 10, WPF and Blazor Hybrid. V1 is read-only inventory, versions, source-reported updates and editable categories. Local SQLite stores the latest snapshot and preferences. No accounts, hosting, HTTP server, telemetry backend, startup item or background service. Hardware/process monitoring, portable scanning, SDK-specific adapters and launcher libraries are excluded. Software already registered in Windows can appear, including games and SDKs.

## Current phase: V3 temporary-file cleanup — implementation complete, release verification pending

1. **Project foundation — complete.** SDK/direct packages pinned with transitive lockfiles. Follow CONTRIBUTING for branch and release workflow.
2. **Provider feasibility — complete.** Both registry views, current-user packages and structured WinGet queries verified without elevation. Shared HKCU registrations are deduplicated; side-by-side identities remain separate. WinGet System maps to Machine scope. Acceptance: exact native identities and explicit failure/unknown states.
3. **Read-only v1 — implemented.** Search, category/status filters, sorting, counts, evidence details, category creation/renaming/overrides, snapshot persistence, refresh/cancellation. Acceptance: behavioral checks and native UI journeys in TEST-PLAN.
4. **V1 release — complete.** The framework-dependent package was built and its native launch, refresh, keyboard search, filtering, empty state and evidence details were verified. Remaining limits are documented in TEST-PLAN. No installer, public deployment or third-party software changes.

## Refresh acceptance

Cached startup; inventory on launch/every 15 minutes while open; hourly freshness evaluation; 24-hour successful update cache; manual forced refresh. One active refresh. Failures preserve prior data and surface source health. Cancellation keeps the previous complete snapshot. Source connection/search budgets are 45 seconds. Closing the window ends refresh activity.

## Version roadmap and runbook gates

5. **V2 — approved updates, implementation complete; release gates pending.** Immutable five-minute plans freeze full resolution metadata, bind approval to a digest, require agreement acceptance, re-resolve before execution, persist bounded action outcomes and share an exclusion gate with refresh. The structured WinGet adapter resolves and executes an exact package/source/version/scope, reports progress and explicit native outcomes, stops honoring cancellation once installation begins, and refreshes inventory after terminal execution. Per-row review opens a modal with complete metadata and agreements, explicit agreement acceptance, UAC/restart guidance, one global progress card, conflicting-control exclusion and deterministic focus restoration. Remaining: user-authorized disposable-VM execution, native keyboard/screen-reader verification and the final public-release audit. Inventory evidence alone never authorizes execution.
6. **V3 setup profiles — implementation complete; release gates pending.** SQLite schema 3 stores profile CRUD with exact package/source/user-or-machine scope choices. Search returns source-qualified WinGet packages. A fresh five-minute plan classifies every item as `Current`, `Install`, `Update` or `Blocked`, exposes target/installer/elevation/agreements, and is digest-bound to explicit approval. Every item is re-resolved immediately before it runs through the shared V2 native path; execution is sequential and stops on drift, failure, cancellation or restart requirement. Remaining: native UI/accessibility and disposable-VM install/update/restart/failure journeys.
7. **V3 temporary-file cleanup — implementation complete; release gates pending.** Scan only canonical `%TEMP%`, skip links/reparse points, inaccessible entries and files younger than seven days, report incomplete scans, and select nothing by default. Five-minute approval freezes an explicit subset. The core revalidates root, canonical path, regular parent chain, age, size, timestamp and reparse state before each individual deletion; isolated failures continue, cancellation stops before the next file, resulting empty candidate-parent directories are removed safely, and exact partial counts/bytes are reported. Remaining: native UI/accessibility and disposable-fixture verification.
8. **V3 encrypted backup/restore — not implemented.** Add versioned encrypted configuration backup/restore with strict limits, atomic writes, safety backup and tested rollback.

Keep phase/acceptance criteria, ADRs, cause/fix/verification change notes, tests and runbooks aligned with each version.

For a consolidated record of the recent delivery context and known verification limits, see [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md).
