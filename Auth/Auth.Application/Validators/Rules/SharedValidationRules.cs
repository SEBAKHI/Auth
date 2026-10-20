using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using FluentValidation;

namespace Auth.Application.Validators.Rules;

/// <summary>
/// Reusable FluentValidation rule extensions for common field types.
/// Every rule declares a catalog code with WithErrorCode (ADR 0001); the API localizes by that code.
/// </summary>
public static class SharedValidationRules
{
    public static IRuleBuilderOptions<T, string> IsValidEmail<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(EmailErrors.Required.Code)
            .EmailAddress().WithErrorCode(EmailErrors.InvalidFormat.Code)
            // 254, the same bound Email.Create applies and one under the
            // NVARCHAR(255) columns that store addresses. 256 let a 255- or
            // 256-character address through every validator and into an INSERT
            // that failed with a truncation error — a 500 on an anonymous
            // endpoint, repeatable at the registration limit.
            .MaximumLength(254).WithErrorCode(EmailErrors.TooLong.Code);
    }

    /// <summary>
    /// The contact address of an organization or an application. Its own codes,
    /// because a code names one request member (ADR 0001) and this one is
    /// <c>contactEmail</c>, not <c>email</c>. 254 for the same reason as
    /// <see cref="IsValidEmail{T}"/>; the columns are NVARCHAR(255).
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidContactEmail<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(ContactEmailErrors.Required.Code)
            .EmailAddress().WithErrorCode(ContactEmailErrors.InvalidFormat.Code)
            .MaximumLength(254).WithErrorCode(ContactEmailErrors.TooLong.Code);
    }

    public static IRuleBuilderOptions<T, string> IsValidFirstName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(UserErrors.FirstNameRequired.Code)
            .MaximumLength(100).WithErrorCode(UserErrors.FirstNameTooLong.Code);
    }

    public static IRuleBuilderOptions<T, string> IsValidLastName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(UserErrors.LastNameRequired.Code)
            .MaximumLength(100).WithErrorCode(UserErrors.LastNameTooLong.Code);
    }

    public static IRuleBuilderOptions<T, string?> IsValidPhoneNumber<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(20).WithErrorCode(PhoneNumberErrors.TooLong.Code);
    }

    public static IRuleBuilderOptions<T, string> IsRequiredPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(PasswordErrors.Required.Code)
            // A ceiling, not policy: PasswordValidator owns complexity and the
            // minimum. Without one this field accepted a request-body-sized
            // string, and every byte of it was regex-scanned and then fed to
            // Argon2id on an anonymous endpoint.
            .MaximumLength(PasswordLimits.MaxLength).WithErrorCode(PasswordErrors.TooLong.Code);
    }

    /// <summary>
    /// The characters a code may hold (<see cref="IsValidCode{T}"/>). Shared with the
    /// userinfo scheme, which accepts only an audience shaped like an application code.
    /// Anchored with <c>\z</c>, not <c>$</c>: in .NET <c>$</c> also matches before a final
    /// newline, which let <c>"shop\n"</c> through.
    /// </summary>
    public const string CodePattern = @"^[a-zA-Z0-9._-]+\z";

    /// <summary>The longest code <see cref="IsValidCode{T}"/> accepts.</summary>
    public const int CodeMaxLength = 100;

    public static IRuleBuilderOptions<T, string> IsValidCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(CodeErrors.Required.Code)
            .MaximumLength(CodeMaxLength).WithErrorCode(CodeErrors.TooLong.Code)
            .Matches(CodePattern).WithErrorCode(CodeErrors.InvalidFormat.Code);
    }

    public static IRuleBuilderOptions<T, string> IsValidPermissionCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(PermissionCodeErrors.Required.Code)
            .MaximumLength(200).WithErrorCode(PermissionCodeErrors.TooLong.Code)
            .Matches(@"^[a-z0-9:*_\-]+$").WithErrorCode(PermissionCodeErrors.InvalidFormat.Code);
    }

    public static IRuleBuilderOptions<T, string> IsValidName<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(NameErrors.Required.Code)
            .MaximumLength(200).WithErrorCode(NameErrors.TooLong.Code);
    }

    public static IRuleBuilderOptions<T, string?> IsValidDescription<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(500).WithErrorCode(DescriptionErrors.TooLong.Code);
    }

    public static IRuleBuilderOptions<T, int> IsValidPageNumber<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .GreaterThanOrEqualTo(1).WithErrorCode(PagingErrors.PageNumberOutOfRange.Code);
    }

    public static IRuleBuilderOptions<T, int> IsValidPageSize<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .InclusiveBetween(1, 100).WithErrorCode(PagingErrors.PageSizeOutOfRange.Code);
    }

    public static IRuleBuilderOptions<T, int> IsValidTrailingWindowDays<T>(this IRuleBuilder<T, int> ruleBuilder)
    {
        return ruleBuilder
            .InclusiveBetween(1, 90).WithErrorCode(DashboardErrors.DaysOutOfRange.Code);
    }

    /// <summary>
    /// Step-up re-authentication threshold (minutes). Null disables step-up. When
    /// set it must be at least 1 minute — a value of 0 would treat every session
    /// as stale and, after the forced login, loop straight back to login because
    /// a freshly minted session is still older than 0. The upper bound matches the
    /// IdP session's 7-day absolute lifetime; beyond it the session expires first,
    /// so step-up would never fire.
    /// </summary>
    public static IRuleBuilderOptions<T, int?> IsValidReauthenticationMaxAge<T>(this IRuleBuilder<T, int?> ruleBuilder)
    {
        return ruleBuilder
            .Must(minutes => minutes is null || (minutes >= 1 && minutes <= 10080))
            .WithErrorCode(ApplicationErrors.ReauthenticationMaxAgeOutOfRange.Code);
    }

    /// <summary>
    /// An application's allowed OAuth scopes: every name must be one this server
    /// grants (<see cref="OAuthScopes.Supported"/>, case-sensitive). <c>openid</c>
    /// is accepted and changes nothing. One code for the whole list, so the
    /// pointer names the list, as the published entry says.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> IsValidAllowedScopes<T>(this IRuleBuilder<T, IReadOnlyList<string>> ruleBuilder)
    {
        return ruleBuilder
            .Must(scopes => scopes.All(OAuthScopes.IsSupported))
            .WithErrorCode(ApplicationErrors.AllowedScopesInvalid.Code);
    }

    /// <summary>
    /// Upper bound on an application's redirect-URI allowlist. Deliberately
    /// small: the allowlist is a security boundary, not a URL directory, and a
    /// short list keeps the delete-and-reinsert sync cheap.
    /// </summary>
    public const int MaxRedirectUris = 20;

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> IsWithinRedirectUriLimit<T>(
        this IRuleBuilder<T, IReadOnlyList<string>> ruleBuilder)
    {
        return ruleBuilder
            .Must(uris => uris.Count <= MaxRedirectUris)
            .WithErrorCode(ApplicationErrors.RedirectUrisTooMany.Code);
    }

    /// <summary>
    /// A registered redirect URI must be absolute, fragment-free, at most 500
    /// characters (DB column), and use https — plain http is allowed only for
    /// localhost during development (OAuth 2.0 Security BCP). It is written in
    /// printable ASCII with no space (OI-166 (a), folded into OI-171): authorize puts
    /// it as-is in a <c>Location</c> header, which refuses a control or non-ASCII
    /// character with a 500. Only a save checks this: a stored address the rule now
    /// refuses keeps working at authorize until a save resends it.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidRedirectUri<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Must(BeAValidPrintableRedirectUri)
            .WithErrorCode(ApplicationErrors.RedirectUriInvalid.Code);
    }

    /// <summary>
    /// The server-side allowlist (OI-171) under the same cap as the public one,
    /// with its own code so the failure names the right list.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> IsWithinConfidentialRedirectUriLimit<T>(
        this IRuleBuilder<T, IReadOnlyList<string>> ruleBuilder)
    {
        return ruleBuilder
            .Must(uris => uris.Count <= MaxRedirectUris)
            .WithErrorCode(ApplicationErrors.ConfidentialRedirectUrisTooMany.Code);
    }

    /// <summary>
    /// A server-side redirect URI (OI-171) follows exactly the public list's rules,
    /// printable ASCII included, with its own code.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidConfidentialRedirectUri<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Must(BeAValidPrintableRedirectUri)
            .WithErrorCode(ApplicationErrors.ConfidentialRedirectUriInvalid.Code);
    }

    /// <summary>
    /// The post-logout allowlist under the same cap as the redirect-URI one,
    /// with its own code so the failure names the right list.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> IsWithinPostLogoutRedirectUriLimit<T>(
        this IRuleBuilder<T, IReadOnlyList<string>> ruleBuilder)
    {
        return ruleBuilder
            .Must(uris => uris.Count <= MaxRedirectUris)
            .WithErrorCode(ApplicationErrors.PostLogoutRedirectUrisTooMany.Code);
    }

    /// <summary>
    /// A post-logout redirect URI follows the redirect-URI rules, and is written
    /// in printable ASCII with no space (0x21-0x7E, after trimming): the
    /// end-session endpoint puts it as-is in a <c>Location</c> header, which
    /// refuses a control or non-ASCII character with a 500 after the session was
    /// already ended. Percent-encoding stays allowed.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidPostLogoutRedirectUri<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Must(BeAValidPrintableRedirectUri)
            .WithErrorCode(ApplicationErrors.PostLogoutRedirectUriInvalid.Code);
    }

    /// <summary>
    /// An application's initiate-login URI (OI-192), the address an invitee's browser is
    /// sent to after accepting: at most 500 characters (the column), absolute, <c>https</c>,
    /// or <c>http</c> only on <c>localhost</c>, <c>127.0.0.1</c> or <c>[::1]</c> (the hosts
    /// the accept page itself will launch), with no fragment and no user information. The
    /// length has its own code, as for the base URL.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidInitiateLoginUri<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(500).WithErrorCode(ApplicationErrors.InitiateLoginUriTooLong.Code)
            .Must(IsAcceptableInitiateLoginUri).WithErrorCode(ApplicationErrors.InitiateLoginUriInvalid.Code);
    }

    /// <summary>
    /// The initiate-login URI's shape rule alone (the length aside), shared with the launch
    /// after an accepted invitation, which applies it again to the stored value: one
    /// definition of what may become the browser's destination.
    /// </summary>
    public static bool IsAcceptableInitiateLoginUri(string uri)
    {
        var trimmed = uri.Trim();

        // A fragment is refused by its character, not by Uri.Fragment, which reads an
        // empty "#" as no fragment at all.
        if (trimmed.Contains('#') ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed) ||
            parsed.UserInfo.Length > 0 ||
            HasIssuerParameter(parsed.Query))
        {
            return false;
        }

        return parsed.Scheme == Uri.UriSchemeHttps ||
               (parsed.Scheme == Uri.UriSchemeHttp && parsed.Host is "localhost" or "127.0.0.1" or "[::1]");
    }

    /// <summary>
    /// Whether the query already names <c>iss</c> (any letter case, percent-encoded or not).
    /// The launch appends the issuer itself, and a second <c>iss</c> would leave the application
    /// to pick one of two, which frameworks do differently.
    /// </summary>
    private static bool HasIssuerParameter(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)[0])
            .Any(key => string.Equals(Uri.UnescapeDataString(key.Replace('+', ' ')), "iss", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The redirect-URI rules, in printable ASCII with no space (0x21-0x7E, after trimming).
    /// Percent-encoding stays allowed. Shared by the three allowlists.
    /// </summary>
    private static bool BeAValidPrintableRedirectUri(string uri) =>
        BeAValidRedirectUri(uri) && IsPrintableAscii(uri.Trim());

    private static bool IsPrintableAscii(string value) =>
        value.All(character => character is >= '\u0021' and <= '\u007E');

    private static bool BeAValidRedirectUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 500)
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Fragment.Length > 0)
        {
            return false;
        }

        if (parsed.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback;
    }

    /// <summary>
    /// The sort field must be null (server default order) or one of the
    /// endpoint's allow-listed field names (case-insensitive). The allow-list is
    /// what keeps client input away from SQL ORDER BY clauses.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> IsValidSortField<T>(
        this IRuleBuilder<T, string?> ruleBuilder,
        IReadOnlyCollection<string> allowedFields)
    {
        return ruleBuilder
            .Must(field => field is null ||
                allowedFields.Contains(field, StringComparer.OrdinalIgnoreCase))
            .WithErrorCode(SortingErrors.SortByNotAllowed.Code);
    }

    /// <param name="required">The code of the token's own concept, since several endpoints take a <c>token</c>.</param>
    public static IRuleBuilderOptions<T, string> IsRequiredToken<T>(this IRuleBuilder<T, string> ruleBuilder, Error required)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(required.Code);
    }

    /// <summary>
    /// A six-digit authenticator code on the two-factor endpoints (<c>code</c>).
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidTwoFactorCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(TwoFactorErrors.CodeRequired.Code)
            .Matches(SixDigitCodePattern).WithErrorCode(TwoFactorErrors.CodeInvalidFormat.Code);
    }

    /// <summary>
    /// The six-digit code emailed before an account binds its first second factor
    /// (<c>emailCode</c>), when one was sent. Its own code, because a published
    /// validation code names one request member.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidTwoFactorEmailCode<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Matches(SixDigitCodePattern).WithErrorCode(TwoFactorErrors.EmailCodeInvalidFormat.Code);
    }

    /// <summary>
    /// The six-digit code sent to an address during registration and email
    /// verification (<c>otp</c>).
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsValidEmailOtp<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty().WithErrorCode(EmailVerificationErrors.OtpRequired.Code)
            .Matches(SixDigitCodePattern).WithErrorCode(EmailVerificationErrors.InvalidOtpFormat.Code);
    }

    private const string SixDigitCodePattern = "^[0-9]{6}$";

    /// <summary>
    /// Languages a user may store as a preferred language. Mirrors the culture
    /// list served by the localization layer; kept local so Application does
    /// not reference Auth_Localization.
    /// </summary>
    private static readonly string[] SupportedPreferredLanguages = ["en", "ar", "tr", "fr", "zh", "ur", "fa"];

    public static IRuleBuilderOptions<T, string?> IsValidPreferredLanguage<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(language => language is null ||
                SupportedPreferredLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            .WithErrorCode(UserErrors.PreferredLanguageNotSupported.Code);
    }

    /// <summary>
    /// UI themes a user may store as a display preference.
    /// </summary>
    private static readonly string[] SupportedThemes = ["light", "dark", "system"];

    public static IRuleBuilderOptions<T, string?> IsValidTheme<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(theme => theme is null ||
                SupportedThemes.Contains(theme, StringComparer.OrdinalIgnoreCase))
            .WithErrorCode(UserErrors.ThemeNotSupported.Code);
    }

    /// <summary>
    /// The time zone must be an IANA identifier (e.g. "Asia/Riyadh") or "UTC".
    /// Windows ids are rejected so stored values stay portable across clients.
    /// </summary>
    /// <param name="invalid">
    /// The code for the caller's member: a user's stored preference and a
    /// statistics query parameter are different members (ADR 0001).
    /// </param>
    public static IRuleBuilderOptions<T, string?> IsValidTimeZone<T>(this IRuleBuilder<T, string?> ruleBuilder, Error invalid)
    {
        return ruleBuilder
            .Must(timeZone => timeZone is null || IsIanaTimeZone(timeZone))
            .WithErrorCode(invalid.Code);
    }

    private static bool IsIanaTimeZone(string id)
    {
        if (id.Length > 50)
        {
            return false;
        }

        if (!id.Contains('/') && !string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
    }
}
