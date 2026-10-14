# Issue metadata and evidence

Keep attributes small: one type (`type:bug`, `type:feature`, `type:maintenance`), one main area (`area:repository`, `area:platform`, `area:security`), and one priority (`priority:high`, `priority:normal`). GitHub open/closed tracks status; do not duplicate it with labels. Use a milestone for a concrete delivery group, and reference the parent/dependency issues. Assign an implementer only when work is active.

The roadmap is [#1](https://github.com/Gaebabi/codex-switch/issues/1). Foundation work is [#2](https://github.com/Gaebabi/codex-switch/issues/2). Future platform/core/runtime issues stay open until their acceptance gates pass. Closing the foundation does not close Linux support.

Every issue and PR includes a verification section:

- Changed behavior/layout: actual baseline and after screenshots side by side with revisions/captions.
- New functionality: actual confirmation screenshot, exercised steps and result.
- Planned functionality: actual baseline/problem screenshot now; explicit pending after/native confirmation until implementation. Never fabricate a finished feature screenshot.
- State OS/architecture, source/package/client revision, commands/results, limitations and unrun checks. Screenshots complement automated checks, not replace them.

Capture public repository pages anonymously when possible. Remove account names/emails, private chats, credentials, home paths and avatar identifiers before export. Replace sensitive pixels irreversibly before optional blur; strip private metadata and inspect the exported screenshot. Do not upload originals. Public repository evidence is not a screenshot of native switching.

Store only sanitized evidence under `docs/evidence/` or GitHub issue attachments, referenced with immutable commit URLs. Keep raw captures/receipts ignored. A PR uses `Closes` only for completed acceptance and `Refs` for ongoing work; link the review and CI results before merge.
