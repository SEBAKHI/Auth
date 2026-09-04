using Auth.Domain.Enums;

namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Note the absence of an IsActive field, on this contract and on the update
/// one: switching an application off is its own endpoint. A full-object PUT
/// assembled from possibly stale client state must never be able to switch a
/// deactivated application back on as a side effect of, say, uploading a logo.
/// <para>
/// <c>AllowSelfRegistration</c> is accepted and stored but has NO EFFECT: no
/// registration path reads it, and none can — sign-up carries no application
/// identity. Whether strangers may create accounts is a server-wide policy,
/// <c>Registration:AllowSelfRegistration</c> in system settings. The field
/// stays on the contract so existing integrations do not break.
/// </para>
/// </summary>
public record CreateApplicationRequest(
    string Code,
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
    ApplicationAccessMode AccessMode = ApplicationAccessMode.Restricted);
