# Security

Report suspected vulnerabilities privately to the repository owner through GitHub's private reporting channel when available.

Never attach or commit local inventory snapshots, SQLite databases, usernames, absolute user paths, probe output, credentials, or other machine-specific data. The shipped UI must remain read-only until the reviewed V2 approval surface is complete. Update execution is limited to structured WinGet APIs and an exact package/source/version/scope plan; do not introduce shell commands, arbitrary command execution, unattended changes, automatic retries, rollback claims, or automatic reboot.
