using Auth.Domain.Enums;

namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>
/// Note the absence of an IsActive field, on this contract and on the update
/// one: switching an application off is its own endpoint. A full-object PUT
/// assembled from possibly stale client state must never be able to switch a
/// deactivated application back on as a side effect of, say, uploading a logo.
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
