namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// Which of the OIDC prompt values an authorize request carries.
/// </summary>
/// <remarks>
/// The one reader of the <c>prompt</c> parameter, for the authorize endpoint and
/// for the organization step, which asks whether a stored request demanded a
/// fresh sign-in (OI-150).
/// </remarks>
internal readonly record struct PromptValues(bool None, bool Login, bool Create)
{
    /// <summary>
    /// The recognised values in a fixed order, as a stored request keeps them,
    /// or null for none. <see cref="TryParse"/> reads it back to the
    /// same values: the tokens it drops are the ones it ignores anyway.
    /// </summary>
    public string? Canonical
    {
        get
        {
            string?[] values = [None ? "none" : null, Login ? "login" : null, Create ? "create" : null];
            var present = values.OfType<string>().ToArray();
            return present.Length == 0 ? null : string.Join(' ', present);
        }
    }

    /// <summary>
    /// Parses the OIDC <c>prompt</c> parameter — a space-delimited list, matched
    /// case-insensitively.
    /// </summary>
    /// <returns>
    /// False when the combination is invalid, which the caller answers with
    /// <c>invalid_request</c>.
    /// </returns>
    public static bool TryParse(string? prompt, out PromptValues values)
    {
        values = default;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return true;
        }

        var tokens = prompt.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var none = false;
        var login = false;
        var create = false;

        foreach (var token in tokens)
        {
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                none = true;
            }
            else if (token.Equals("login", StringComparison.OrdinalIgnoreCase))
            {
                login = true;
            }
            else if (token.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                // OpenID Connect "Initiating User Registration" 1.0: show sign-up
                // rather than sign-in. Without a session, or where a fresh sign-in
                // is demanded (with login, or by a freshness rule); a session that
                // is simply used changes nothing.
                create = true;
            }

            // consent and select_account are recognised OIDC values this provider
            // has no screen for, and unknown values may be client extensions.
            // Both are ignored rather than rejected, as before.
        }

        // OIDC Core §3.1.2.1: "none" may not be combined with any other value.
        if (none && tokens.Length > 1)
        {
            return false;
        }

        values = new PromptValues(none, login, create);
        return true;
    }
}
