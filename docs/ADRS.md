# Architecture decisions

## 001 — WPF and Blazor Hybrid (accepted)

WPF hosts Razor components through WebView2; DI supplies native C# services directly. Angular plus a companion process adds another application boundary; native XAML was an alternative but HTML/CSS fits this dashboard. No HTTP listener. Pin .NET 10 and the Windows 26100 SDK target required by current WinGet interop. Ship x64 framework-dependent for the user's Windows PC. External WebView navigation is blocked. Blazor autostart stays enabled.

[Microsoft WPF Blazor tutorial](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/tutorials/wpf?view=aspnetcore-10.0)

## 002 — Inventory and update evidence (accepted)

Enumerate uninstall records in HKCU/HKLM, both views, plus current-user non-framework/non-resource packages. Deduplicate identical shared HKCU registrations, keeping machine-view and side-by-side identities distinct. Retain technical display names if Windows provides them.

Avoid Win32_Product because enumeration can trigger installer consistency checks. Use structured WinGet COM APIs and indexed WinRT collection access. Correlate product codes/package-family names and scope, never display-name equality alone. Normalize System to Machine. Preserve version strings; missing/inconsistent installed versions or ambiguous candidates return Unknown. Use IsUpdateAvailable, not custom version ordering. None reported is not a guarantee of the latest publisher release. Unsupported means no exact match; Failed indicates provider failure. Pin details are explicitly unavailable in this adapter.

Sources: [Microsoft inventory guidance](https://learn.microsoft.com/en-us/powershell/scripting/samples/working-with-software-installations?view=powershell-7.5), [WinGet API](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl), [upgrade semantics](https://learn.microsoft.com/en-us/windows/package-manager/winget/upgrade).

## 003 — SQLite and failure preservation (accepted)

Single transactional snapshot plus relational categories/assignments/aliases. Aliases preserve future suggestions when a built-in category is renamed. Keep overrides for removed identities in case they return. Schema 1 rejects newer databases rather than downgrading them. No inventory history. Data stays in the Windows user's LocalApplicationData/PcDashboard directory. Bounded SQLite warning/error diagnostics support troubleshooting and are not uploaded. WinGet/WebView2 retain their own Microsoft diagnostics policies; the app does not change global settings.

## 004 — V2 update action boundary (accepted, implementation incomplete)

Inventory evidence and update execution use separate interfaces. A request represents one package and freezes the application identity, package/source identity, installed version and offered target version that the user reviewed. The core gate rejects missing or older-than-five-minute approval, changed evidence and overlapping actions before invoking an executor. This token is an intent correlation value inside the trusted desktop process, not an authentication credential. The future executor must resolve the structured WinGet package again, report progress and cancellation limits, and rescan inventory after completion. No executor or action UI is currently registered.

## 005 — Private application data stays in SQLite (accepted)

Persist inventory, preferences and diagnostics only in the local database. File logging and command-line probe exports are removed. Diagnostics retain 256 entries of at most 4,096 characters; explicitly imported legacy evidence is hash-deduplicated and preserved in a separate table before source deletion. Database failures never trigger file logging. The diagnostic tables are additive to schema 1 and do not change snapshot contracts. Redacted file logs were considered but rejected because redaction cannot reliably identify every personal value. SQLite is not encryption; WebView2 retains its separate runtime-managed profile. Older binaries must not be used if this privacy boundary is required.
