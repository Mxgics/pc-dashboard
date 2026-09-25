# PC Dashboard

Personal Windows desktop application built with .NET 10, WPF and Blazor Hybrid. The shipped V1 UI is read-only: inventory, update evidence, categories, and the latest local snapshot. V2's core approval boundary is under development; no update executor or update UI is registered. No uninstall, telemetry, accounts, background service, or arbitrary command execution.

Verified commands from this directory: `dotnet restore PcDashboard.slnx --locked-mode -m:1`, `dotnet build PcDashboard.slnx -c Release --no-restore -m:1`, `dotnet run --project tests/PcDashboard.Tests -c Release --no-restore`. Serial solution restore/build avoids a silent WPF/MSBuild parallel-build failure observed during verification. Keep local databases, inventory, probe output, build artifacts, usernames, and machine-specific paths out of Git.

Preserve explicit source/freshness states and the last successful snapshot across provider failures. Update plans, ADRs, change notes, tests, and runbooks with substantive changes. Do not commit, push, publish, or change installed software without user authorization.
