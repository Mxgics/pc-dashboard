# PC Dashboard project plan

## Accepted baseline

Personal Windows desktop app: .NET 10, WPF and Blazor Hybrid. V1 is read-only inventory, versions, source-reported updates and editable categories. Local SQLite stores the latest snapshot and preferences. No accounts, hosting, HTTP server, telemetry backend, startup item or background service. Hardware/process monitoring, portable scanning, SDK-specific adapters and launcher libraries are excluded. Software already registered in Windows can appear, including games and SDKs.

## Current phase: V2 approved updates — core boundary in progress

1. **Project foundation — complete.** SDK/direct packages pinned with transitive lockfiles. Follow CONTRIBUTING for branch and release workflow.
2. **Provider feasibility — complete.** Both registry views, current-user packages and structured WinGet queries verified without elevation. Shared HKCU registrations are deduplicated; side-by-side identities remain separate. WinGet System maps to Machine scope. Acceptance: exact native identities and explicit failure/unknown states.
3. **Read-only v1 — implemented.** Search, category/status filters, sorting, counts, evidence details, category creation/renaming/overrides, snapshot persistence, refresh/cancellation. Acceptance: behavioral checks and native UI journeys in TEST-PLAN.
4. **V1 release — complete.** The framework-dependent package was built and its native launch, refresh, keyboard search, filtering, empty state and evidence details were verified. Remaining limits are documented in TEST-PLAN. No installer, public deployment or third-party software changes.

## Refresh acceptance

Cached startup; inventory on launch/every 15 minutes while open; hourly freshness evaluation; 24-hour successful update cache; manual forced refresh. One active refresh. Failures preserve prior data and surface source health. Cancellation keeps the previous complete snapshot. Source connection/search budgets are 45 seconds. Closing the window ends refresh activity.

## Version roadmap and runbook gates

5. **V2 — approved updates, in progress.** The separate action interface and single-action gate now reject missing, expired, stale, overlapping or evidence-mismatched approvals. Remaining: a structured WinGet executor, fresh preflight resolution, progress and cancellation semantics, elevation/reboot policy, post-action inventory verification, partial-failure recovery and the explicit approval UI. Test the full path before adding executable update instructions to the runbook. Inventory evidence never authorizes execution.
6. **V3 — cleanup/setup/backups, not implemented.** Define previews/exclusions and recovery for cleanup, repeatable reviewed desired state for setup, and destination/retention/encryption plus tested restores for backups. Add capability-specific runbooks alongside code. No maintenance commands exist in v1.

Keep phase/acceptance criteria, ADRs, cause/fix/verification change notes, tests and runbooks aligned with each version.

For a consolidated record of the recent delivery context and known verification limits, see [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md).
