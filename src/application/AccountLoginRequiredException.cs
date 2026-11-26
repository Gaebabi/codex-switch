using System;
using System.Text.RegularExpressions;

namespace CodexAccountDesk;

internal sealed class AccountLoginRequiredException() : InvalidOperationException("account.login_required")
{
    private static readonly Regex RejectedCredential = new(
        @"\b(?:HTTP(?: status)?\s+401|status(?: code)?\s*[:=]?\s*401|401\s+Unauthorized|invalid_grant|refresh_token_(?:expired|reused|invalidated|revoked)|token_revoked|token_expired|please log out and sign in again|not logged in|chatgpt (?:login|authentication) (?:is )?required)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool Matches(Exception error) => error is AccountLoginRequiredException ||
        error is CodexRequestException { Operation: "account/read" or "account/rateLimits/read" } request &&
        (request.Code == 401 || RejectedCredential.IsMatch(request.Message));
}
