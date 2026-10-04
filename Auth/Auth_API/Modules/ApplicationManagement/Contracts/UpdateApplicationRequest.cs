using Auth.Domain.Enums;

namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// <c>AllowSelfRegistration</c> is accepted and stored but has NO EFFECT: no
/// registration path reads it, and none can — sign-up carries no application
/// identity. Whether strangers may create accounts is a server-wide policy,
/// <c>Registration:AllowSelfRegistration</c> in system settings. The field
/// stays on the contract so existing integrations do not break; this being a
/// full replacement, send back the value you read.
/// <para>
/// Two exceptions to the full replacement, both lists: <c>RedirectUris</c> and
/// <c>AllowedScopes</c> are left UNCHANGED when null or absent, and cleared by an
/// empty list (for <c>AllowedScopes</c>, back to <c>openid</c> only).
/// </para>
/// </summary>
public record UpdateApplicationRequest(
    string Name,
    string? Description = null,
    string? BaseUrl = null,
    string? LogoUrl = null,
    string? ContactEmail = null,
    bool AllowSelfRegistration = false,
    bool RequireTwoFactor = false,
    bool RequireEmailVerification = false,
    int SessionTimeoutMinutes = 60,
    int MaxConcurrentSessions = 5,
    IReadOnlyList<string>? RedirectUris = null,
    int? ReauthenticationMaxAgeMinutes = null,
    ApplicationAccessMode AccessMode = ApplicationAccessMode.Restricted,
    IReadOnlyList<string>? AllowedScopes = null);
