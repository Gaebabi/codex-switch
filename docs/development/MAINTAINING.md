# Maintaining Codex Switch

Codex Switch is a lightweight account switcher and quota dashboard. Prefer focused fixes, clear unavailable states and recovery over dependencies, background services or controls that do not help a user complete a task.

## Roles and decisions

`@Gaebabi` maintains this repository and owns releases. A name in Contributors records commit attribution; it does not grant collaborator access. A fork or PR also grants no write permission. Grant new collaborators only the role their actual responsibility needs, after reviewing their work. Keep account access and signing credentials separate from repository membership.

Anyone can report a problem, propose a change or review a PR. Discuss changes to switching, credentials, platform support and dependencies in an issue first. Maintainers decide scope, resolve disagreements with technical reasons, and may decline changes that add unnecessary weight. Credit contributors and preserve their original authorship.

## A change from report to delivery

1. Search for an existing issue. Record the observed problem, acceptance criteria, relevant platform and private-data-free application evidence. Use the small [issue metadata set](ISSUES.md). Assign an implementer when work starts, rather than implying every roadmap item is in progress.
2. Use a focused branch from current `main` and a linked PR. New PR titles and maintainer commit subjects are English; maintainer commits use `[n/n English task] English description`. Discussion and translated documents may use other languages. Do not rewrite published history or original contributor commits merely to change their language.
3. Review the diff, dependencies, process ownership, credential/data preservation and privacy. For an external fork, inspect workflow/build changes before approving its Actions run. Never give an untrusted PR signing credentials, owner tokens, personal data or a self-hosted runner.
4. Wait for all six protected checks on the latest head and current base: Repository checks, Windows isolated checks, both Mac compilations and both Linux native keyring jobs. Resolve conversations and record the review. Failed or missing checks are not permission to bypass protection.
5. Prefer squash for a focused change, using its English PR title as the merge subject. Preserve original ancestry with a reviewed merge commit when integrating coupled dependency or contributor branches. Recheck the expected head immediately before merging and verify the resulting tree. An approved run is not approval to merge.
6. Close an issue only when its acceptance is met. Publish a binary release separately using the applicable release guide; a merged PR is not an installed or released app. Merged working branches are automatically deleted; local checkouts and forks are separate.

`main` uses strict required checks, administrator enforcement and resolved conversations; force pushes and branch deletion are disabled. The current single-maintainer arrangement requires **zero independent approvals**. An author's review comment is self-review, never independent approval. CODEOWNERS requests maintainer attention but is not a mandatory independent review gate. When a second trusted maintainer can actually review changes, consider requiring one independent approval with stale-review dismissal; do not lock a sole maintainer out by enabling a gate nobody can satisfy.

Actions use read-only default tokens, cannot approve PRs, and require full-length SHA-pinned actions. All external fork contributors need run approval, including returning contributors. Neither run approval nor green CI certifies a change is safe; review code before both running and merging. Dependabot proposes updates; it does not automatically merge or release them.

## Triage and recoverable cleanup

Review open PRs and failed CI before starting duplicate work. Keep issue bodies concise: current acceptance first, historical evidence in details, dependencies linked once. For a blocked item record the missing environment, decision or verification and a concrete next step. Do not close or post recurring empty comments just because an issue is old. Consolidate a duplicate only after copying every unique unmet criterion and linking its successor.

Before deleting an old remote branch, check its exact head against current main and merged PR metadata. A squash-merged head may not be an ancestor: require the exact merged PR head, not just a matching branch name. Preserve a verified private Git bundle and a head/PR manifest, then use an expected-old-head deletion so concurrent work cannot be erased. Keep unmatched or unfinished branches and backup refs until their unique work has been reviewed; do not delete local worktrees, dirty files, release tags or recovery history as housekeeping. Private recovery archives must never be attached to public issues.

Periodically read back protection, collaborator permissions, fork-run approval, default token rights, action pinning and branch auto-deletion. Repository files describe policy; they cannot prove that server settings remain enabled. A change to settings must include a fresh read-back. Never make admin bypass the normal repair path for a failed check.

## Common first-maintainer mistakes

- Treating a contributor, fork or star as permission to push. Check collaborator roles separately.
- Merging because tests are green without reading the code, or approving a fork workflow before inspecting what it executes.
- Giving CI write tokens or account secrets to fix a permissions error. Keep PR verification separate from release publication.
- Squashing dependency integrations that need original ancestry, or deleting a branch because its name looks familiar. Inspect the exact commits first.
- Posting an account screenshot, diagnostic archive or local recovery bundle publicly. Follow [privacy and security reporting](../../SECURITY.md).
- Closing a broad Linux-support issue after a container or package test. Preserve the remaining real-desktop acceptance and support limits.
- Releasing after a docs-only management change. Delivery should reflect an actual product change, with verified packages and honest platform claims.

See [contribution checks](../../CONTRIBUTING.md), [CI boundaries](../CI.md), [dependency review](DEPENDENCY_REVIEW.md) and [operations](OPERATIONS.md).
