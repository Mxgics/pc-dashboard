# Test plan and evidence

## Automated

Run `dotnet run --project tests/PcDashboard.Tests -c Release --no-restore`. The executable harness exits nonzero on a failed assertion. On 29 September 2026, 58 checks passed: the V1 identity, privacy, persistence, failure, cancellation and category cases plus V2 immutable plans, digest tampering, deterministic expiry, agreement acceptance, execution-time drift, action persistence/retention, startup interruption recovery, newer-schema rejection, refresh/mutation exclusion, native terminal/cancellation mapping, mandatory terminal refresh, verified success and `SucceededUnverified` fallback.

Serialized Release solution build (`dotnet build PcDashboard.slnx -c Release --no-restore -m:1`) passed with zero errors. NuGet vulnerability lookup remains unverified because the service was unavailable and emitted NU1900 warnings.

## Native provider evidence

Native provider verification previously covered registry and packaged inventory plus structured WinGet evidence without elevation. Personal inventory counts and probe output are intentionally excluded.

The structured WinGet execution adapter compiles against the pinned 1.29.290 COM package. Code review confirms exact source/package/version/scope options, hash-mismatch prohibition, structured progress, cancellation gating at installation start, native outcome mapping and post-terminal refresh. No real package was changed on the host. User-scope, machine-scope/UAC, cancellation, installer failure, restart-required and uncertain interruption scenarios remain unverified pending a user-authorized disposable Windows VM.

## V2 approval UI evidence

- Release compilation passed and the built WPF process remained responsive during a hidden startup smoke test. No update action was invoked.
- A representative browser fixture using the shipped CSS and approval markup was inspected at desktop and 420-pixel widths. The dialog remained bounded/scrollable, metadata stacked at narrow width, actions stayed available in a sticky footer, and the background was inert.
- The accessibility tree exposed the dialog name, review heading, complete definition-list metadata, UAC/cancellation copy, agreement heading, a named checkbox, Cancel, and a disabled Approve action. Initial focus landed on the review heading.
- Source/behavior review confirms Cancel and Escape restore the originating row action after render; approval moves focus to the global operation heading; agreement acceptance gates approval; refresh/review controls are disabled during conflicting states; and one live global card carries progress, cancellation and restart messages.
- Actual native keyboard traversal, Escape/focus restoration, screen-reader announcements and responsive resizing remain unverified because the available UI automation could not inspect the WPF WebView. These are release gates, not inferred passes.

## UI and release checklist

- Verified in the packaged release: native launch and refresh, visible focus indicator, keyboard search replacement, no-match empty state, text search results, update-status filtering, available-update count, and expandable evidence details.
- Release artifact verified at `artifacts/win-x64/PcDashboard.Desktop.exe` with adjacent dependencies and README. The package is framework-dependent and requires .NET 10 Desktop Runtime plus WebView2.
- Narrow-window layout remains unverified because the native automation could not reliably resize the WPF window. Full screen-reader audit, long-duration timer soak and a separate clean Windows machine are also not verified. Do not claim those from code inspection.
- Native UI verification has not been repeated for the privacy changes.


## Privacy verification

The privacy change passed 41 behavioral checks, including bounded SQLite diagnostics, complete legacy import, retry deduplication, preservation on failed import, and no fallback log/probe files. Locked restore and serialized Release build succeeded with NU1900 warnings because the vulnerability service was unreachable. Native UI checks were not repeated.
