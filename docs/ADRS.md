# Architecture decisions

## 001 — WPF and Blazor Hybrid (accepted)

WPF hosts Razor components through WebView2; DI supplies native C# services directly. Angular plus a companion process adds another application boundary; native XAML was an alternative but HTML/CSS fits this dashboard. No HTTP listener. Pin .NET 10 and the Windows 26100 SDK target required by current WinGet interop. Ship x64 framework-dependent for the user's Windows PC. External WebView navigation is blocked. Blazor autostart stays enabled.

[Microsoft WPF Blazor tutorial](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/wpf?view=aspnetcore-10.0)

## 002 — Inventory and update evidence (accepted)

Enumerate uninstall records in HKCU/HKLM, both views, plus current-user non-framework/non-resource packages. Deduplicate identical shared HKCU registrations, keeping machine-view and side-by-side identities distinct. Retain technical display names if Windows provides them.

Avoid Win32_Product because enumeration can trigger installer consistency checks. Use structured WinGet COM APIs and indexed WinRT collection access. Correlate product codes/package-family names and scope, never display-name equality alone. Normalize System to Machine. Preserve version strings; missing/inconsistent installed versions or ambiguous candidates return Unknown. Use IsUpdateAvailable, not custom version ordering. None reported is not a guarantee of the latest publisher release. Unsupported means no exact match; Failed indicates provider failure. Pin details are explicitly unavailable in this adapter.

Sources: [Microsoft inventory guidance](https://learn.microsoft.com/en-us/powershell/scripting/samples/working-with-software-installations?view=powershell-7.5), [WinGet API](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl), [upgrade semantics](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade).

## 003 — SQLite and failure preservation (accepted)

Single transactional snapshot plus relational categories/assignments/aliases. Aliases preserve future suggestions when a built-in category is renamed. Keep overrides for removed identities in case they return. Schema 2 adds a bounded 100-record foreground action history and marks unfinished actions interrupted on startup; newer databases are rejected rather than downgraded. No inventory history. Data stays in the Windows user's LocalApplicationData/PcDashboard directory. Bounded SQLite warning/error diagnostics support troubleshooting and are not uploaded. WinGet/WebView2 retain their own Microsoft diagnostics policies; the app does not change global settings.

## 004 — V2 update action boundary (accepted, release verification pending)

Inventory evidence, resolution and execution use separate interfaces. An immutable plan freezes the exact application, package, source, versions, scope, installer, elevation metadata and agreements the user reviewed, with a SHA-256 digest and five-minute expiry. Approval repeats the plan identity and digest; its opaque token correlates explicit intent inside the trusted desktop process and is not an authentication credential. The core resolves the package a second time, rejects tampering, evidence drift, unaccepted agreements and concurrent refresh/mutation, and persists bounded terminal action records.

The structured WinGet adapter uses only `Microsoft.Management.Deployment`: it selects one configured source, one exact package ID, one exact version and an explicit user or machine scope. It rechecks installer, elevation and agreement metadata at execution. Cancellation can prevent queued/download work; once WinGet reports installing, cancellation is no longer forwarded because the native API cannot stop or roll back that phase. Native success, restart-required, installer failure, pre-install cancellation and uncertain post-install interruption remain distinct. Every invoked execution is followed by a fresh inventory/update pass; success becomes `SucceededUnverified` unless that pass confirms the target version. The registered resolver, executor and verifier are reachable only through the explicit approval flow.

The approval UI exposes an action only on rows with source-reported availability. Native HTML `dialog` provides modality and focus containment; the review heading receives initial focus, cancel/Escape restores the originating row control, and approval moves focus to the single global operation card. The dialog displays every frozen resolution field and all agreement text/URLs, with a separate checkbox when agreements exist. Refresh/review actions are excluded while preparation, review or execution is active. Progress announces state politely, disables cancellation at installation, and preserves explicit UAC, restart and unverified-success messaging.

## 005 — Private application data stays in SQLite (accepted)

Persist inventory, preferences and diagnostics only in the local database. File logging and command-line probe exports are removed. Diagnostics retain 256 entries of at most 4,096 characters; explicitly imported legacy evidence is hash-deduplicated and preserved in a separate table before source deletion. Database failures never trigger file logging. The diagnostic tables are additive to schema 1 and do not change snapshot contracts. Redacted file logs were considered but rejected because redaction cannot reliably identify every personal value. SQLite is not encryption; WebView2 retains its separate runtime-managed profile. Older binaries must not be used if this privacy boundary is required.

## 006 — V3 setup profiles are reviewed intent, not standing authority (accepted, release verification pending)

Schema 3 stores named profiles and ordered items containing only a WinGet package ID, configured source and exact `User` or `Machine` scope. Profile CRUD is transactional; duplicate package/source/scope entries and invalid bounds are rejected. Search is discovery only: choosing a result freezes its package and source identity, but does not authorize installation.

Each run resolves a new five-minute plan and classifies every item as `Current`, `Install`, `Update` or `Blocked`. The dialog exposes installed/target versions, scope, installer, elevation and all agreements. Approval binds the complete plan digest and agreement acceptance. The core acquires the shared mutation gate, re-resolves each item immediately before execution, compares the complete item digest, and then reuses the structured WinGet install/upgrade path and its cancellation boundary. Items run sequentially; drift, blocked resolution, failure, cancellation, uncertain interruption or restart requirement ends the sequence. There is no retry, rollback, reboot, unattended trigger or shell command.

## 007 — Temporary cleanup is a frozen, revalidated subset (accepted, release verification pending)

Cleanup has one permitted root: the normalized current-user `%TEMP%` directory. A reparse-point root is rejected. Scanning walks regular directories without following links, skips inaccessible entries and files younger than seven days, caps candidates at 50,000, and reports any incomplete coverage. Scan results select nothing by default.

Review freezes only explicitly selected candidates—canonical path, size, UTC write time and non-reparse state—in a five-minute digest-bound plan. Immediately before each deletion, the core revalidates the unchanged root, lexical canonical path, every parent directory's non-reparse state, file age, size, timestamp and non-reparse state. Changed files are skipped; access/deletion failures are counted and later files continue. Cancellation takes effect between files. Only parent directories of files actually deleted are considered for empty-directory removal, deepest first, with the same boundary/reparse checks. Results and action history report exact deleted/skipped/failed counts and bytes; there is no recycle-bin, rollback, arbitrary root or shell command.

## 008 — Portable settings use authenticated encrypted archives (accepted, release verification pending)

Backup scope is deliberately narrower than the database: categories, category aliases, application assignments and setup profiles only. Inventory snapshots, update evidence, action history, diagnostics, runtime profiles and application files are excluded. The versioned `.pcdbak` binary format authenticates its version, KDF parameters, random 16-byte salt, random 12-byte nonce and ciphertext length as associated data. PBKDF2-HMAC-SHA256 runs for 600,000 iterations to derive a 256-bit key; AES-256-GCM encrypts and authenticates the bounded JSON payload with a 16-byte tag.

Archives are capped at 10 MiB, decrypted payloads at 5 MiB, and every collection and field has schema/record bounds before persistence. Wrong passphrases and authenticated-file tampering return the same generic error. Preview records the encrypted archive digest; restore re-reads, digest-checks and authenticates that exact file. Writes create encrypted bytes in memory, flush a same-directory encrypted temporary file, and atomically move it into place; plaintext is never written to a temporary file. Before replacing settings, the app exports current portable data to a separately encrypted safety archive using the supplied passphrase. The four supported data groups are then replaced in one SQLite transaction; cancellation or failure before commit rolls back all of them. Inventory is outside the archive and is refreshed after a successful restore.
