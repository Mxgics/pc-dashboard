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
