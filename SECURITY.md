# Security

Report suspected vulnerabilities privately to the repository owner through GitHub's private reporting channel when available.

Never attach or commit local inventory snapshots, SQLite databases, usernames, absolute user paths, probe output, credentials, or other machine-specific data. Update and setup execution require reviewed approval surfaces and exact structured WinGet plans. Temporary cleanup is restricted to the canonical current-user TEMP root; skip links/reparse points and inaccessible or young files, select nothing by default, and revalidate canonical path, parent chain, age, size, timestamp and reparse state immediately before each approved deletion. Keep expiry, tamper/drift checks, agreement acceptance, partial-result reporting, sequential stop conditions and operation exclusion enforced below the UI; do not introduce shell commands, arbitrary paths, unattended changes, automatic retries, rollback claims or automatic reboot.
