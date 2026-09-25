# Test plan and evidence

## Automated

Run `dotnet run --project tests/PcDashboard.Tests -c Release --no-restore`. The executable harness exits nonzero on a failed assertion. On 17 September 2026, 33 checks passed: the V1 identity, persistence, failure, cancellation and category cases plus V2 exact-evidence approval, missing/expired approval, changed installed version and overlapping action rejection.

Serialized release solution build (`dotnet build PcDashboard.slnx -c Release --no-restore -m:1`) passed with zero warnings/errors. Dependency vulnerability lookup remains unverified.

## Native provider evidence

Native provider verification previously covered registry and packaged inventory plus structured WinGet evidence without elevation. Personal inventory counts and probe output are intentionally excluded.

## UI and release checklist

- Verified in the packaged release: native launch and refresh, visible focus indicator, keyboard search replacement, no-match empty state, text search results, update-status filtering, available-update count, and expandable evidence details.
- Release artifact verified at `artifacts/win-x64/PcDashboard.Desktop.exe` with adjacent dependencies and README. The package is framework-dependent and requires .NET 10 Desktop Runtime plus WebView2.
- Narrow-window layout remains unverified because the native automation could not reliably resize the WPF window. Full screen-reader audit, long-duration timer soak and a separate clean Windows machine are also not verified. Do not claim those from code inspection.
- Native UI verification has not been repeated for the privacy changes.


## Privacy verification

The privacy change passed 41 behavioral checks, including bounded SQLite diagnostics, complete legacy import, retry deduplication, preservation on failed import, and no fallback log/probe files. Locked restore and serialized Release build succeeded with NU1900 warnings because the vulnerability service was unreachable. Native UI checks were not repeated.
