# Credit history and quota protection

[Feature129](https://github.com/Gaebabi/codex-switch/issues/129) adds a separate **Credits** metric in History and an **Allow credit use** toggle in the Auto Switch menu. [Bug130](https://github.com/Gaebabi/codex-switch/issues/130) corrects the priority of still-usable subscriptions with past reported expiry dates. Source/native verification is tracked in their PR; a source change does not replace an existing published or installed package.

## What the graph measures

Credits have their own units and graph, separate from quota percentages and forecasts. The view shows the last observed balance and balance decreases within the selected range. It uses the existing usage refresh; it adds no network request, service, polling interval or runtime dependency.

The pending History update adds a30-day preset and custom inclusive Korean-calendar start/end dates to both metrics. Custom bounds remain fixed during refreshes and metric changes; selecting a preset restores the rolling range. Only retained observations are available. Credit totals exclude intervals that cross the selected start or end.

When a bucket's quota remains capped at100% while its recorded credit balance decreases, History joins those observations into the orange **Credit range** above the quota boundary. The upper axis and tooltip show actual credit quantities, with a separate scale from the ordinary quota's0–100%. Cumulative observed consumption starts with the exhausted cycle's first numeric balance. It needs no percentage denominator, including for a reported zero or negative balance. A source percentage above100 cannot be converted into a credit quantity without balance records; that amount remains explicitly unavailable. Original quota records and forecast inputs remain unchanged.

The owner-requested gap supplement estimates only intervals bounded by two recorded numeric balances. It uses their positive net decrease minus consumption already counted within that interval. Net top-ups do not invent negative spending. An estimate is a balance difference, not an authoritative billing ledger: grants, expiry or adjustments can affect it. Unknown intervals without both endpoints are not extrapolated. Custom-range totals include only complete intervals inside that range.

Observed and estimated quantities remain separate in the metrics and tooltip. The combined quota/credit chart draws estimates with an orange dashed line; the Credits chart uses dashed orange outline bars. A quota reset starts a new credit cycle, and a gap cannot be assigned across an unknown/reset quota cycle. Existing source observations are never overwritten by these derived estimates.

Optional `RecoveredCreditHistory` entries hold historical balance observations recovered from the same account's encrypted backups, with original observation time and a source SHA256. They supplement a copy for presentation; existing observations win at matching timestamps and conflicting recovered values are ignored. Snapshot copies and the existing version1 encrypted vault retain this separate list. Recovery must preserve account identity, credentials, preferences, original observations and vault protection. It does not authorize restoring an old vault over current data.

These are observed balance decreases, not an authoritative billing ledger or task-level cost. Purchases, promotional grants, expiry, adjustments and activity on other devices or eligible products can affect a shared balance. A positive balance change never becomes negative consumption or cancels an earlier observed decrease. Unknown/unlimited balances, an initial reading and collection gaps longer than five minutes do not invent consumption. A reported negative balance is retained as reported rather than clamped to zero. Ambiguous bucket IDs stay unknown instead of being merged.

Tracking starts from available observations; historical percentage records cannot reconstruct earlier credit spending. Each credit bucket remains separate. History retains up to30days, with minute resolution for the most recent two days and15-minute compaction for older readings. Compaction preserves observed decreases, and a hard8192-point limit per bucket bounds fragmented histories. Intervals crossing the selected range's start are not attributed to that range. Times are KST, consistent with the existing History view.

Optional credit records are encrypted in the existing version1 vault. Existing accounts, quota history, key identity and credential data remain intact. Reauthentication retains the same account object/history; UI account snapshots copy their credit lists.

## Allow credit use

The default is **On**, including when loading existing preferences that omit the new field. The owner's saved Auto Switch setting is not enabled or changed by this feature.

When credit use is Off and fresh unambiguous ordinary quota reports0%, the utility first tries a currently feasible eligible automatic account, honoring the existing threshold, custom order, cooldown, window matching, freshness and restriction guards. If automatic switching is disabled or unavailable, it saves the current chat checkpoint and normally closes local Codex. Busy, stale, ambiguous, failed or unknown observations cannot close a client. If the desktop connection is unavailable, protection reports that it is waiting rather than pretending shutdown succeeded.

This is local client protection, not an OpenAI billing setting. It is evaluated on usage refreshes and does not block charges from other devices or cloud activity. It never installs a driver, changes a payment method, force-kills Codex or clears authentication.

## Stop and resume safely

Credit protection uses the existing chat capture, name backup, identity checks and normal shutdown backend. Its checkpoint is saved before shutdown. Successful pause does not apply credentials, reopen Codex or send Continue. An intentional paused state is distinct from a failed switch, and startup cannot blindly replay it into credit consumption.

If Auto Switch is enabled and fresh ordinary quota or another eligible account becomes available, recovery can resume those saved chats. When changing the target, all saved chat contexts are checked for that target before account application; original chat IDs and Continue markers are retained. A manually reopened depleted client can be normally paused again without duplicate backups or Continue sends. Manual recovery into exhausted/unknown quota requires explicitly allowing credits first. Incomplete shutdown remains recoverable after its two persisted additional retries; cancellation or a failed protected apply never falls back to reopening a known depleted source.

Account mismatch and trust/context refusal stop the operation. Ambiguous Continue delivery remains observation-only. Existing manual and automatic handoffs retain their prior default behavior when credit use is allowed.

## Past expiry dates

Among already eligible accounts, default ordering uses the earliest known subscription date, including dates in the past while fresh usage still qualifies. Unknown dates follow known dates. This prevents a usable account in a billing grace period from being permanently placed behind every future expiry. Explicit custom ranks still win.

A date is not an entitlement check. Past dates never override explicit server usage denial, a spending/limit restriction, insufficient quota, stale observations or invalid windows. The utility does not extend a date, change billing state or infer payment status from a token.

## Verification boundaries

Portable tests cover precise decimal observations, top-ups, missing/unlimited values, gaps, ordering, retention/compaction, legacy preferences, priority, shutdown/recovery and context refusal. The old past-date ranking is reproduced before correction. Local Windows/shared builds and all9catalogs are checked separately.

Hosted native Windows/Mac evidence exercises actual credit selectors, permission toggles, paused-state controls, past-date target projection, empty histories and minimum-window scrolling with fictional inputs. An additional native Windows test normally closes an owned disposable parent/child fixture without apply/launch/Continue. It does not stop a real account or certify remote/cloud billing protection. Existing protected six-job CI, unchanged trust/fixture checks, author review and source/merge equality remain required. [Inspected original native screenshots and source-bound receipts](../evidence/credits/README.md) confirm the fictional application UI. Final-head protected checks and author review remain tracked by the PR.

Source1.43.1: fresh numeric0% remains eligible to leave when its reset timestamp arrives, matching the existing exhaustion rule. This source-only exception does not qualify elapsed-reset destinations or infer replenishment. [Native confirmation and regression scope](../evidence/reset-boundary/README.md).
