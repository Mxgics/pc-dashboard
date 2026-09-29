# Security

Report suspected vulnerabilities privately to the repository owner through GitHub's private reporting channel when available.

Never attach or commit local inventory snapshots, SQLite databases, usernames, absolute user paths, probe output, credentials, or other machine-specific data. Update and setup execution require reviewed approval surfaces and are limited to structured WinGet APIs plus exact package/source/version/scope plans. Setup profiles are preferences, not authority to install: every run requires a fresh digest-bound plan, complete agreement review and immediate per-item revalidation. Keep expiry, tamper/drift checks, agreement acceptance, sequential stop conditions and operation exclusion enforced below the UI; do not introduce shell commands, arbitrary command execution, unattended changes, automatic retries, rollback claims, or automatic reboot.
