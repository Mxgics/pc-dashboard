# Public release preparation

Use a new, sanitized Git history for public publication. The original private repository, its old branches/tags, discussion attachments and earlier build outputs are not part of the public baseline. Do not make the existing repository public merely by changing visibility.

Before publication:

- Review all staged files in the separate clean baseline; it must have no commits, inherited refs or remote before the initial approved commit.
- Keep the public GitHub handle and noreply attribution; exclude private databases, profiles, logs, exports and build output.
- Run the locked restore, serialized Release build and behavioral checks. Record inaccessible dependency-audit services as an outstanding check.
- Review any package separately, including embedded paths in binaries and symbols. Old local packages may contain absolute build paths. Only share newly built, scanned artifacts.
- Independently inspect any hosting destination's branches, tags, releases, Actions artifacts and issue/PR attachments. Existing private history must remain private.
- Choose a license before representing the source as open source; no license is currently supplied.

Committing, pushing and changing repository visibility require explicit authorization. Source availability does not imply V2 execution is complete. See PROJECT-PLAN.md for remaining product work and TEST-PLAN.md for verification limits.

## 2 October 2026 V3 readiness audit

The intended `develop` product stack was audited after refreshing remote refs:

- Six product commits are introduced above `origin/develop`; every commit uses `Mxgics <Mxgics@users.noreply.github.com>`.
- Introduced Git object names contain no build/output, work-fixture, database, log, archive, inventory/probe, drive-letter or user-profile paths.
- Current tracked paths contain no generated database, log, `.pcdbak`, build-output or work-fixture files. `WindowsInventory.cs` is legitimate source code, not captured inventory.
- Current source, `obj` data and Release outputs contain no embedded local workspace/user-profile path; `Directory.Build.props` maps compiler paths to `/_/`.
- Local `bin`/`obj` and review fixtures are ignored and absent from the intended diff. Test databases and archives were created only under generated test directories and removed.
- GitHub shows no releases, tags or issues. The latest stacked CI run succeeded and reports no artifacts. Visible pull requests are the authored stack, CI repair and Dependabot updates; no file attachments were used in the authored stack descriptions.
- The final readiness diff is limited to navigation, conflicting-control exclusion, one operation-gate test and reconciled documentation; its exact staged content receives the same path/content scan before publication as the earlier layers.

This is a source-stack audit, not permission to publish or promote. The current repository must still not be made public: use a new sanitized history with no inherited refs/remotes, choose a license, merge the CI-action repair and synchronize it into `develop`, restore vulnerability-audit availability, complete native accessibility/disposable-VM/recovery scenarios, and independently recheck the final destination immediately before publication. The current GitHub Actions history is metadata/log history even though the inspected latest run has no artifact; it is not part of a new public baseline.
