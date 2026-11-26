# Release 1.45.11

## Account access and recovery

- Show **Sign in** for a rejected login and open browser reauthentication for the selected account. Replace stale current quota with a dash while preserving usage history. Brief refresh failures retain the last reading.
- Save Switch-owned Windows account files with an atomic rename and preserve destination access permissions. Reopening a prepared account window skips unchanged settings and unnecessary vault writes.
- Clear an empty manual restart record after verifying that the primary Windows Codex window started later under the same login. Plans with saved chats or credential changes remain recoverable.

## Windows dashboard

- Give every **Focus** button the same style. Drag a tank by its handle or choose **Move card** in its menu. The vault remembers the layout independently of automatic switching priority.
- Show the remaining time at the bottom of each notice. Ordinary notices expire after eight seconds, warnings after fifteen. The close button dismisses the notice immediately. A dismissed recovery notice leaves its action on the account card.
- Remove the internal vertical ticks from the CPU, memory, GPU and VRAM meters.

## Verification scope

The Windows desktop checks cover rejected logins, hidden stale quota, preserved recovery records, account-window file replacement under Windows access controls, saved dashboard order and notice dismissal. Local Windows pixel comparisons use identical fictional hardware readings for both themes and languages. The screenshots are renders of production controls with fictional data.

[Tank order and matching Focus buttons](../media/release-1.45.11/dashboard-reordered-ko.png) · [Notification timer and close button](../media/release-1.45.11/notification-countdown-ko.png) · [Earlier hardware meters](../media/release-1.45.11/hardware-before.png) · [Hardware meters with ticks removed](../media/release-1.45.11/hardware-after.png)

Windows x64 and both macOS architectures have self-contained downloads. Both Linux architectures use the existing preview package scope.
