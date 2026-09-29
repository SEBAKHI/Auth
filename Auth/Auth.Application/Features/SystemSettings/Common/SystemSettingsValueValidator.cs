using System.Globalization;
using System.Text.Json;
using Auth.Application.SystemSettings;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.SystemSettings.Common;

/// <summary>
/// Validates a single override value against its field definition, plus the
/// per-section cross-field rules. Every rule here deliberately mirrors (or
/// is stricter than) the corresponding startup fail-fast so a stored
/// override can never brick a restart.
/// </summary>
internal static class SystemSettingsValueValidator
{
    /// <summary>
    /// Validates one field value; appends errors instead of throwing so a
    /// save reports every problem at once.
    /// </summary>
    public static void ValidateValue(
        SettingFieldDefinition field,
        JsonElement value,
        List<Error> errors)
    {
        switch (field.Kind)
        {
            case SettingKind.Bool:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected true or false."));
                }

                break;

            case SettingKind.Int:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected a whole number."));
                    break;
                }

                if (field.Min is { } min && number < min)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"must be at least {min}."));
                }

                if (field.Max is { } max && number > max)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"must be at most {max}."));
                }

                break;

            case SettingKind.Enum:
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (text is null || field.AllowedValues is null ||
                    !field.AllowedValues.Contains(text, StringComparer.Ordinal))
                {
                    var allowed = field.AllowedValues is null ? string.Empty : string.Join(", ", field.AllowedValues);
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, $"expected one of: {allowed}."));
                }

                break;

            case SettingKind.StringArray:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected an array of strings."));
                    break;
                }

                foreach (var item in value.EnumerateArray())
                {
                    // Blank entries are forbidden by contract: the provider
                    // uses empty strings as array-shrink tombstones, and an
                    // empty ExemptPaths entry would exempt every request.
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "array entries must be non-empty strings."));
                        break;
                    }
                }

                break;

            default:
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(field.Path, "expected a string."));
                }

                break;
        }
    }

    /// <summary>
    /// Cross-field / semantic rules per section. Rules that depend on values
    /// NOT in the payload (e.g. Email:Enabled saved earlier) resolve them via
    /// <paramref name="effectiveValue"/>, so the RESULTING configuration is
    /// validated — mirroring the corresponding startup fail-fasts.
    /// </summary>
    /// <param name="requestOrigin">
    /// The browser Origin of the page making the save, when there is one. A save
    /// may not leave that page outside a filled first-party list: it would lock
    /// the saving console out of password sign-in, with no page left to undo it.
    /// </param>
    public static void ValidateSectionRules(
        SettingSectionDefinition section,
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        List<Error> errors,
        Func<string, string?> effectiveValue,
        string? requestOrigin = null)
    {
        switch (section.Key)
        {
            case "Jwt":
                RequireAbsoluteUrl(values, "Issuer", allowEmpty: false, errors);
                RequireAbsoluteUrl(values, "Audience", allowEmpty: false, errors);
                break;

            case "Gateway":
                ForEachArrayEntry(values, "ExemptPaths", errors, (entry, path) =>
                {
                    if (!entry.StartsWith('/'))
                    {
                        return $"'{entry}' must start with '/'.";
                    }

                    // The middleware prefix-matches any entry ending in '/', so a
                    // bare "/" matches every request path and exempts the whole
                    // API from gateway-token validation — an authentication
                    // bypass disguised as one keystroke.
                    return entry.Trim() == "/"
                        ? "'/' would exempt every path from gateway validation; list the specific prefixes instead."
                        : null;
                });
                break;

            case "IdentityProvider":
                // AccountsBaseUrl has no empty-value fallback in its consumer
                // (unlike PublicBaseUrl, where empty means "derive from the
                // request"): an empty value builds a relative authorize redirect
                // and breaks universal login without any error.
                RequireAbsoluteUrl(values, "AccountsBaseUrl", allowEmpty: false, errors);
                RequireAbsoluteUrl(values, "PublicBaseUrl", allowEmpty: true, errors);
                ValidateFirstPartySpaOrigins(values, section, errors, effectiveValue, requestOrigin);
                break;

            case "DataRetention":
                // PolicyVersion is stamped onto every deletion request and
                // tombstone, where the column is NVARCHAR(20) NOT NULL
                // (AccountDeletionRequests.sql:11, AccountDeletionTombstones.sql:7).
                // Without this the console accepts a longer string and the
                // failure surfaces much later, as a truncation error the first
                // time a user asks to delete their account.
                RequireMaxLength(values, "PolicyVersion", 20, errors);
                break;

            case "Email":
                // Mirror of the Email options ValidateOnStart rule: sending
                // enabled with a relative FrontendBaseUrl would abort the
                // next boot, so the resulting combination is checked here
                // (payload value first, currently effective value otherwise).
                var enabledText = PayloadOrEffective(values, section, "Enabled", effectiveValue);
                var frontendBaseUrl = PayloadOrEffective(values, section, "FrontendBaseUrl", effectiveValue);
                if (bool.TryParse(enabledText, out var emailEnabled) && emailEnabled &&
                    !Uri.IsWellFormedUriString(frontendBaseUrl, UriKind.Absolute))
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(
                        "FrontendBaseUrl", "must be an absolute URL while email sending is enabled."));
                }

                break;

            case "Cors":
                foreach (var (path, value) in values)
                {
                    if (!path.Equals("AllowedOrigins", StringComparison.OrdinalIgnoreCase) ||
                        value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    // An empty override would tombstone every file origin
                    // and cut the console off — mirrors the production
                    // startup fail-fast on empty CORS origins.
                    if (value.GetArrayLength() == 0)
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "must contain at least one origin."));
                    }

                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } origin)
                        {
                            continue;
                        }

                        if (origin.Contains('*'))
                        {
                            errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "wildcard origins are not allowed with credentials."));
                        }
                        else if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                                 uri.AbsolutePath != "/" || origin.EndsWith('/') ||
                                 !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                        {
                            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                                path, $"'{origin}' must be a bare http(s) origin without path or trailing slash."));
                        }
                    }
                }

                // The first-party apps renew their session with a cookie on a
                // credentialed cross-origin fetch. Without credentials the browser
                // neither sends nor stores it, and every console and accounts
                // session would end at its first refresh.
                if (bool.TryParse(PayloadOrEffective(values, section, "AllowCredentials", effectiveValue), out var allowCredentials) &&
                    !allowCredentials &&
                    EffectiveArray(effectiveValue, FirstPartySpaOriginsKey).Count > 0)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(
                        "AllowCredentials",
                        "must stay true while IdentityProvider:FirstPartySpaOrigins lists an origin: " +
                        "their refresh cookie only travels on credentialed requests."));
                }

                break;

            case "ImageStorage":
                var malformedPublicBaseUrl = false;
                foreach (var (path, value) in values)
                {
                    if (path.Equals("PublicBaseUrl", StringComparison.OrdinalIgnoreCase) &&
                        value.ValueKind == JsonValueKind.String &&
                        value.GetString() is { } publicBase &&
                        !publicBase.StartsWith('/') &&
                        !Uri.IsWellFormedUriString(publicBase, UriKind.Absolute))
                    {
                        errors.Add(SystemSettingsErrors.InvalidFieldValue(
                            path, "must be an absolute URL or a rooted path starting with '/'."));
                        malformedPublicBaseUrl = true;
                    }
                }

                // An image URL is composed as PublicBaseUrl + '/' + key and is
                // served by the static-files middleware mounted at RequestPath,
                // so the base's path has to end with the serving path or every
                // image 404s. Both halves are resolved as they WILL be after
                // this save: changing them together is the supported way to
                // move the path, changing one alone is the mistake. RequestPath
                // only takes effect on restart, but the pair still has to be
                // consistent when it does.
                if (!malformedPublicBaseUrl)
                {
                    ValidateImageUrlPairing(values, section, errors, effectiveValue);
                }

                ForEachArrayEntry(values, "AllowedContentTypes", errors, (entry, path) =>
                    entry.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : $"'{entry}' must be an image/* content type.");
                break;

            case "GatewayRateLimiting":
                ValidateGatewayRateCeiling(values, section, errors, effectiveValue);
                break;
        }
    }

    /// <summary>
    /// The gateway's per-route policies, each as its (permit, window) pair.
    /// </summary>
    private static readonly (string Permit, string Window)[] GatewayRatePolicies =
    [
        ("AuthPermitLimit", "AuthWindowSeconds"),
        ("RegisterPermitLimit", "RegisterWindowSeconds"),
        ("ApiPermitLimit", "ApiWindowSeconds"),
        ("AdminPermitLimit", "AdminWindowSeconds"),
        ("RegistrationFollowupPermitLimit", "RegistrationFollowupWindowSeconds")
    ];

    /// <summary>
    /// Refuses a gateway save whose global bucket is slower than one of the
    /// per-route policies it sits above.
    /// <para>
    /// Every request passes the global limiter before its route policy, so the
    /// lower of the two is what a client actually gets. Without this rule an
    /// administrator can raise ApiPermitLimit to 2000, watch it save, and be
    /// throttled at the unchanged global 1000 — a control that reports success
    /// and does nothing, which is the exact failure the registry pruned the
    /// dead throttle fields to avoid.
    /// </para>
    /// </summary>
    private static void ValidateGatewayRateCeiling(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        List<Error> errors,
        Func<string, string?> effectiveValue)
    {
        // Rates, not raw permits: the windows are independently editable, so
        // 100/60s and 100/3600s are not the same ceiling.
        double? Rate(string permitField, string windowField)
        {
            if (TryReadLong(permitField) is not { } permits ||
                TryReadLong(windowField) is not { } seconds ||
                seconds <= 0)
            {
                return null;
            }

            return (double)permits / seconds;
        }

        long? TryReadLong(string fieldPath) =>
            long.TryParse(
                PayloadOrEffective(values, section, fieldPath, effectiveValue),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;

        // An unreadable value is already reported by the per-field validation;
        // pairing stays silent rather than adding a second, vaguer error.
        if (Rate("GlobalPermitLimit", "GlobalWindowSeconds") is not { } globalRate)
        {
            return;
        }

        var outrunning = GatewayRatePolicies
            .Select(policy => (policy.Permit, Rate: Rate(policy.Permit, policy.Window)))
            .Where(policy => policy.Rate is { } rate && rate > globalRate)
            .ToList();

        if (outrunning.Count == 0)
        {
            return;
        }

        // Report on a field this save actually touched: naming the global
        // ceiling to an administrator who was raising a route policy points
        // them at a control they did not open.
        var editsGlobal = ContainsField(values, "GlobalPermitLimit")
                       || ContainsField(values, "GlobalWindowSeconds");

        if (editsGlobal)
        {
            // Lowering the ceiling can undercut every policy at once, but
            // that is one mistake, not three. Name the fastest — clear it and
            // the rest clear with it.
            var fastest = outrunning.MaxBy(policy => policy.Rate)!.Permit;

            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                "GlobalPermitLimit",
                $"the global bucket would allow fewer requests per second than {fastest}, and every " +
                "request passes the global limiter first, so the lower ceiling is the one clients hit. " +
                "Raise the global limit, or lower the policies above it."));
            return;
        }

        foreach (var (permitField, _) in outrunning)
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                permitField,
                "the global bucket would allow fewer requests per second than this, and every request " +
                "passes the global limiter first, so the lower ceiling is the one clients hit. " +
                "Raise the global limit or lower this one."));
        }
    }

    /// <summary>
    /// Rejects an ImageStorage save whose resulting PublicBaseUrl does not end
    /// with the resulting RequestPath. The error is reported on whichever of
    /// the two fields the payload actually carries, so the console flags a
    /// field the admin just edited.
    /// </summary>
    private static void ValidateImageUrlPairing(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        List<Error> errors,
        Func<string, string?> effectiveValue)
    {
        var editsPublicBaseUrl = ContainsField(values, "PublicBaseUrl");
        if (!editsPublicBaseUrl && !ContainsField(values, "RequestPath"))
        {
            return;
        }

        var publicBaseUrl = PayloadOrEffective(values, section, "PublicBaseUrl", effectiveValue);
        var requestPath = PayloadOrEffective(values, section, "RequestPath", effectiveValue);

        // Nothing to pair until both halves exist; each has its own rule.
        if (string.IsNullOrWhiteSpace(publicBaseUrl) || string.IsNullOrWhiteSpace(requestPath))
        {
            return;
        }

        // A request path of "/" serves from the root and has no segment to
        // pair against (and never reaches here: the middleware rejects it).
        var servingSegments = requestPath.Trim('/');
        if (servingSegments.Length == 0)
        {
            return;
        }

        var servingPath = $"/{servingSegments}";
        if (PathOf(publicBaseUrl) is not { } basePath ||
            basePath.EndsWith(servingPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        errors.Add(SystemSettingsErrors.InvalidFieldValue(
            editsPublicBaseUrl ? "PublicBaseUrl" : "RequestPath",
            $"'{publicBaseUrl}' must end with the serving path '{servingPath}': image URLs are " +
            "composed as PublicBaseUrl + '/' + file name and are served under RequestPath, so a " +
            "mismatched pair returns 404 for every image. Change both fields together to move the path."));
    }

    /// <summary>
    /// Path portion of an absolute URL or of a rooted path, without its
    /// trailing slash. Null when the value is neither — a shape the caller has
    /// already reported, so pairing stays silent about it.
    /// </summary>
    private static string? PathOf(string publicBaseUrl)
    {
        if (Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsolutePath.TrimEnd('/');
        }

        return publicBaseUrl.StartsWith('/') ? publicBaseUrl.TrimEnd('/') : null;
    }

    private static bool ContainsField(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath)
    {
        foreach (var (path, _) in values)
        {
            if (path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireAbsoluteUrl(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath,
        bool allowEmpty,
        List<Error> errors)
    {
        foreach (var (path, value) in values)
        {
            if (!path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString();
            if (allowEmpty && string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (!Uri.IsWellFormedUriString(text, UriKind.Absolute))
            {
                errors.Add(SystemSettingsErrors.InvalidFieldValue(path, "must be an absolute URL (e.g. https://auth.example.com)."));
            }
        }
    }

    /// <summary>
    /// Refuses a string longer than the storage column that will hold it. The
    /// registry's Min/Max only describe numbers, so a string field whose value
    /// ends up in a fixed-width column needs its ceiling stated here.
    /// </summary>
    private static void RequireMaxLength(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath,
        int maxLength,
        List<Error> errors)
    {
        foreach (var (path, value) in values)
        {
            if (!path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (value.GetString() is { } text && text.Length > maxLength)
            {
                errors.Add(SystemSettingsErrors.InvalidFieldValue(
                    path, $"must be at most {maxLength} characters."));
            }
        }
    }

    private static void ForEachArrayEntry(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        string fieldPath,
        List<Error> errors,
        Func<string, string, string?> rule)
    {
        foreach (var (path, value) in values)
        {
            if (!path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase) ||
                value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String &&
                    item.GetString() is { } entry &&
                    !string.IsNullOrWhiteSpace(entry) &&
                    rule(entry, path) is { } message)
                {
                    errors.Add(SystemSettingsErrors.InvalidFieldValue(path, message));
                }
            }
        }
    }

    private const string FirstPartySpaOriginsKey = "IdentityProvider:FirstPartySpaOrigins";

    /// <summary>
    /// The IdentityProvider rules for the first-party origin list, checked on the
    /// configuration as it WILL be after this save. Each rule keeps the refresh
    /// cookie from being switched on in a state where it cannot work, or from
    /// being handed to a page that is not one of the platform's own apps:
    /// <list type="bullet">
    /// <item>the cookie delivery needs at least one listed origin;</item>
    /// <item>each entry is a bare https origin (the cookie is <c>__Host-</c>, so https only);</item>
    /// <item>a filled list includes the accounts app, whose logout page calls end-session;</item>
    /// <item>each entry is a CORS origin and CORS allows credentials, or the browser
    /// never sends the cookie.</item>
    /// </list>
    /// </summary>
    private static void ValidateFirstPartySpaOrigins(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        List<Error> errors,
        Func<string, string?> effectiveValue,
        string? requestOrigin)
    {
        const string field = "FirstPartySpaOrigins";
        var origins = PayloadArrayOrEffective(values, section, field, effectiveValue);

        if (bool.TryParse(PayloadOrEffective(values, section, "SpaRefreshCookieEnabled", effectiveValue), out var enabled) &&
            enabled && origins.Count == 0)
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                "SpaRefreshCookieEnabled", "needs at least one origin in FirstPartySpaOrigins."));
        }

        if (origins.Count == 0)
        {
            return;
        }

        var wellFormed = true;
        foreach (var origin in origins.Where(origin => !IsBareHttpsOrigin(origin)))
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                field, $"'{origin}' must be a bare https origin without path or trailing slash."));
            wellFormed = false;
        }

        if (!wellFormed)
        {
            return;
        }

        if (Uri.TryCreate(PayloadOrEffective(values, section, "AccountsBaseUrl", effectiveValue), UriKind.Absolute, out var accounts))
        {
            var accountsOrigin = accounts.GetLeftPart(UriPartial.Authority);
            if (!origins.Contains(accountsOrigin, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(SystemSettingsErrors.InvalidFieldValue(
                    field, $"must include the accounts app origin '{accountsOrigin}' (IdentityProvider:AccountsBaseUrl)."));
            }
        }

        // A filled list is the only set of browser pages allowed to sign in with a
        // password. The page saving it must stay in it, or the save locks its own
        // app out, and no page is left from which to undo it.
        if (!string.IsNullOrWhiteSpace(requestOrigin) &&
            !origins.Contains(requestOrigin.Trim().TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                field, $"must include '{requestOrigin.Trim()}', the origin of the page making this change: without it, " +
                       "that page could no longer sign in with a password to undo it."));
        }

        var corsOrigins = EffectiveArray(effectiveValue, "Cors:AllowedOrigins")
            .Select(origin => origin.TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in origins.Where(origin => !corsOrigins.Contains(origin)))
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                field, $"'{origin}' must also be listed in Cors:AllowedOrigins."));
        }

        if (!bool.TryParse(effectiveValue("Cors:AllowCredentials"), out var allowCredentials) || !allowCredentials)
        {
            errors.Add(SystemSettingsErrors.InvalidFieldValue(
                field, "needs Cors:AllowCredentials to be true: the refresh cookie only travels on credentialed requests."));
        }
    }

    private static bool IsBareHttpsOrigin(string origin) =>
        !origin.Contains('*') &&
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.AbsolutePath == "/" && !origin.EndsWith('/') &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        // ASCII only and no trailing dot: a browser sends an IDN host in its
        // punycode form, and the runtime match is a plain string compare.
        uri.IdnHost == uri.Host && !uri.Host.EndsWith('.') &&
        // Exactly the form a browser sends in Origin: an explicit default port
        // (":443") never appears there, and the runtime match is a string compare.
        origin.Equals(uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The array a field WILL hold after this save: the payload's entries when the
    /// field is part of it, the live effective entries otherwise. Blank entries are
    /// dropped, as the runtime drops them (they are the database layer's shrink
    /// tombstones).
    /// </summary>
    private static IReadOnlyList<string> PayloadArrayOrEffective(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        string fieldPath,
        Func<string, string?> effectiveValue)
    {
        foreach (var (path, value) in values)
        {
            if (path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase))
            {
                return value.ValueKind != JsonValueKind.Array
                    ? []
                    : value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!.Trim())
                        .Where(entry => entry.Length > 0)
                        .ToList();
            }
        }

        return EffectiveArray(effectiveValue, section.FullKey(fieldPath));
    }

    /// <summary>
    /// Reads a configuration array through the flat key lookup: configuration keeps
    /// element <c>i</c> at <c>{key}:{i}</c> and gives the array key itself no value.
    /// Blank entries (shrink tombstones) are skipped, not read as the end.
    /// </summary>
    private static IReadOnlyList<string> EffectiveArray(Func<string, string?> effectiveValue, string fullKey)
    {
        var entries = new List<string>();
        for (var index = 0; effectiveValue($"{fullKey}:{index}") is { } entry; index++)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                entries.Add(entry.Trim());
            }
        }

        return entries;
    }

    /// <summary>
    /// The value the configuration WILL have after this save: the payload's
    /// value when the field is part of it, the live effective value otherwise.
    /// </summary>
    private static string? PayloadOrEffective(
        IReadOnlyList<KeyValuePair<string, JsonElement>> values,
        SettingSectionDefinition section,
        string fieldPath,
        Func<string, string?> effectiveValue)
    {
        foreach (var (path, value) in values)
        {
            if (path.Equals(fieldPath, StringComparison.OrdinalIgnoreCase))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.GetRawText()
                };
            }
        }

        return effectiveValue(section.FullKey(fieldPath));
    }
}
