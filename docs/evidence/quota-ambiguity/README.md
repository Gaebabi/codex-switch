# Automatic selection with ambiguous quota IDs

[Issue120](https://github.com/Gaebabi/codex-switch/issues/120) / [PR121](https://github.com/Gaebabi/codex-switch/pull/121) / [delivery](../../development/RELEASE_1_42_6.md) / [source/image hashes](evidence.json).

|Existing healthy1.42.5 native menu|1.42.6 skips an ambiguous candidate|1.42.6 refuses an ambiguous source|
|---|---|---|
|![Healthy baseline](before.png)|![English valid fallback](en-auto-switch-ambiguous-candidate.png)|![English unavailable source](en-auto-switch-ambiguous-source.png)|
|Original healthy frame; the exception is not pictured|![Korean valid fallback](ko-auto-switch-ambiguous-candidate.png)|![Korean unavailable source](ko-auto-switch-ambiguous-source.png)|

The [prior healthy baseline](before.json) retains original export bytes. Separately, actual UsageSnapshot.Parse with duplicate JSON object keys reproduces ArgumentException in the unchanged1.42.5 policy. That error receipt and original policy hash establish the failure; the healthy screenshot is not presented as a crash picture.

The new hosted Windows source `0c3cefb6006968a9c0bf54a72215b90ceafd2b97`, Verify40556935313, passes44 native assertions:36 existing account/menu checks plus8 new EN/KO checks. Actual native Auto Switch popups remain usable, display Demo account3 when nearer-expiry Demo account2 has ambiguous quotas, and explicitly show no eligible account for ambiguous current observations. The original two duplicate buckets remain in the snapshot. No quota is merged, inferred or overwritten; valid alternatives retain original eligibility and expiry/custom ranking.125 policy/account/retry contracts also cover low-source refusal, empty IDs, duplicate IDs across slots, preserved data and normal selection.

The four new exported PNGs were individually inspected without scaling/pixel changes, and retain their original bytes/SHA256 with no image metadata. Inputs are fictional and the window/menu HWND belongs to the app on a disposable hosted runner; no personal screen/account, actual switch, client shutdown or Continue is used. Source/capture/native/authenticated scopes remain separate. Final evidence commits are distinct from capture source; implementation and dependency hashes bind the same code. The final release requires six final-head checks, author COMMENT self-review (not independent approval), expected-head normal merge/tree equality and verified public package downloads.

Development-only PR101/102 are integrated with original ancestry. Their official release/registry review and full native export/protocol validation are recorded in [dependency review](../../development/DEPENDENCY_REVIEW.md). The tools add no shipped runtime dependency. Existing trust checks and all original native assertions remain.
