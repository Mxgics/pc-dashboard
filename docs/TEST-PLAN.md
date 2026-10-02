# Test plan and evidence

## Automated

Run `dotnet run --project tests/PcDashboard.Tests -c Release --no-restore`. The executable harness exits nonzero on a failed assertion. On 2 October 2026, 84 checks passed: the V1/V2 cases; V3 setup profiles; temporary-cleanup safety cases; encrypted-backup format, limits, preview, tamper/passphrase, scope, safety-copy and rollback; and cross-capability operation exclusion.

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

## V3 setup-profile evidence

- Release compilation covers the profile editor, exact-scope package selection, review modal and shared global-operation integration. A representative fixture using the shipped CSS was reviewed at desktop and 420-pixel widths: the modal remained bounded and scrollable, metadata stacked, the sticky actions remained available, and the background was inert.
- Behavioral checks use a deterministic provider to prove agreement rejection, immediate second resolution, drift rejection, one-at-a-time execution, restart-required stopping, and transactional SQLite profile round trips/deletion.
- Code review confirms profile search cannot execute, only `User` and `Machine` scopes persist, blocked items disable UI approval, complete resolution/agreement data is displayed, and the existing native executor disables effective cancellation after installation begins.
- The browser accessibility tree exposed only the modal while open, with its name, focused heading, complete per-package metadata, agreements, named checkbox and disabled approval action. Native create/edit/delete/search, modal keyboard/focus/screen-reader behavior and narrow-window layout remain unverified. No WinGet install or update was invoked on the host. User/machine scope, UAC, update, cancellation, failure, drift and restart-required journeys remain disposable-VM release gates.

## V3 temporary-cleanup evidence

- Behavioral fixtures use an isolated generated directory, old and young files, a metadata-drifted file and a file held with exclusive sharing. They prove default-empty selection, seven-day filtering, one delete/one skip/one failure with exact bytes, continued processing, deletion of the resulting empty nested directory, tampered-path rejection and pre-action cancellation without deletion.
- Code review confirms production construction derives only the current user's `%TEMP%`, rejects a reparse root, never follows reparse entries, limits results to 50,000 and shares the refresh/mutation gate. Execution rechecks the root and every selected candidate plus its parent chain immediately before deletion.
- A representative fixture using the shipped CSS passed desktop and 420-pixel review: the modal remained bounded/scrollable, metadata stacked, long paths wrapped, and sticky actions remained available. Its accessibility tree exposed only the named modal, focused heading, incomplete-scan warning, definition-list summary, selected-file region, irreversible-action checkbox, Cancel and disabled Delete action.
- Native scan/list/review keyboard, focus, screen-reader and narrow-window behavior is not yet verified. A separate disposable filesystem fixture for live reparse/inaccessible-entry races is also unverified. The host's real TEMP contents were not scanned or deleted.

## V3 encrypted backup/restore evidence

- Behavioral checks create only synthetic categories, assignments and profiles in an isolated generated database. They confirm the versioned magic, absence of known plaintext records, authenticated record-count preview, one generic error for wrong passphrase and ciphertext tampering, exact supported-data replacement, inventory exclusion, encrypted pre-restore safety backup, no leftover `.tmp` files and full rollback after a relational insert failure.
- Code review confirms PBKDF2-HMAC-SHA256 at 600,000 iterations, 16-byte random salt, 12-byte random nonce, AES-256-GCM with a 16-byte tag, authenticated header fields, 10 MiB archive/5 MiB plaintext limits, bounded records/fields, exact encrypted-file digest recheck and encrypted-only same-directory atomic writes. Key, plaintext and archive buffers are cleared where owned after use; passphrase strings are cleared from component state after operations.
- Release compilation covers the native save/open dialogs, passphrase confirmation, authenticated preview modal, explicit replacement acknowledgement, shared global operation exclusion, encrypted safety-backup reporting, settings reload and post-restore inventory refresh. A representative fixture using the shipped CSS passed desktop and 420-pixel review: the dialog remained bounded/scrollable, long archive names wrapped, count metadata stacked, and sticky actions remained available. Its accessibility tree exposed only the named modal, focused heading, complete definition-list counts, scope exclusions, named replacement checkbox, Cancel and disabled Restore action.
- Native file-dialog traversal, modal focus restoration, screen-reader announcements, narrow-window behavior and an external safety-backup recovery drill remain unverified. Test archives were generated under the isolated test directory and removed; no personal dashboard backup was created or restored.

## V3 integration-readiness evidence

- A shared-coordinator behavioral check holds Cleanup active and proves Backup is rejected before creating an archive. Existing checks cover refresh/update mutual exclusion, setup sequencing, cleanup isolation and backup/restore transactions.
- Source review confirms assignment, category-create and category-rename controls now disable during refresh, review and maintenance interaction; setup, cleanup and backup components already receive the same parent disabled state. Passive search, filters and evidence controls remain usable where they cannot mutate shared configuration.
- A representative fixture using the shipped CSS passed desktop and 420-pixel review. The navigation pills wrapped without clipping, capability cards retained their layout, and the inventory table remained intentionally horizontally scrollable at narrow width. The accessibility tree exposed a named `Dashboard sections` landmark with links to Applications, Setup profiles, Temporary files, Backup & restore and Categories, plus disabled conflicting controls.
- Native keyboard activation/scroll targeting and screen-reader landmark announcements remain unverified and are release gates.

## UI and release checklist

- Verified in the packaged release: native launch and refresh, visible focus indicator, keyboard search replacement, no-match empty state, text search results, update-status filtering, available-update count, and expandable evidence details.
- Release artifact verified at `artifacts/win-x64/PcDashboard.Desktop.exe` with adjacent dependencies and README. The package is framework-dependent and requires .NET 10 Desktop Runtime plus WebView2.
- Narrow-window layout remains unverified because the native automation could not reliably resize the WPF window. Full screen-reader audit, long-duration timer soak and a separate clean Windows machine are also not verified. Do not claim those from code inspection.
- Native UI verification has not been repeated for the privacy changes.


## Privacy verification

The privacy change passed 41 behavioral checks, including bounded SQLite diagnostics, complete legacy import, retry deduplication, preservation on failed import, and no fallback log/probe files. Locked restore and serialized Release build succeeded with NU1900 warnings because the vulnerability service was unreachable. Native UI checks were not repeated.
