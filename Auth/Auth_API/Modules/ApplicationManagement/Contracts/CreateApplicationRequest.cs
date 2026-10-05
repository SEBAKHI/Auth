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
/// <para>
/// <c>AllowedScopes</c> lists the OAuth scopes the application may be granted
/// beyond <c>openid</c> (<c>profile</c>, <c>email</c>, <c>phone</c>). Null or
/// absent means none.
/// </para>
/// <para>
/// <c>AllowOrganizationCreation</c> and <c>OrganizationCreatorRoleId</c> stay off
/// at creation: the creator role must be one of this application's roles, and
/// none exists yet, so a true or a role id is refused with
/// <c>Application.OrganizationCreatorRoleInvalid</c>. Switch it on with an update.
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
    ApplicationAccessMode AccessMode = ApplicationAccessMode.Restricted,
    IReadOnlyList<string>? AllowedScopes = null,
    bool? AllowOrganizationCreation = null,
    Guid? OrganizationCreatorRoleId = null);
