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
/// Four exceptions to the full replacement, all lists: <c>RedirectUris</c>,
/// <c>ConfidentialRedirectUris</c>, <c>PostLogoutRedirectUris</c> and
/// <c>AllowedScopes</c> are left UNCHANGED when null or absent, and cleared by an
/// empty list (for <c>AllowedScopes</c>, back to <c>openid</c> only). An address
/// may not be in both <c>RedirectUris</c> and <c>ConfidentialRedirectUris</c>,
/// checked on the lists as they stand after the update
/// (<c>Application.RedirectUriInBothLists</c>).
/// </para>
/// <para>
/// <c>AllowOrganizationCreation</c> and <c>OrganizationCreatorRoleId</c> are also
/// left UNCHANGED when null or absent; the empty GUID clears the role. Allowing
/// creation needs an active role of this application that carries at least one
/// permission (<c>Application.OrganizationCreatorRoleInvalid</c>), every one of
/// which the caller holds.
/// </para>
/// <para>
/// <c>LogoUrlDark</c> (the dark-mode logo) is left UNCHANGED when null or
/// absent, and removed by the empty string.
/// </para>
/// <para>
/// <c>InitiateLoginUri</c> (where invitees are sent after accepting) follows the
/// same rule: left UNCHANGED when null or absent, removed by the empty string.
/// </para>
/// <para>
/// <c>RequireTwoFactor</c> and <c>AccessMode</c> are left UNCHANGED when null or absent
/// (S23, AM11): a body built before the field existed, or by a tool that forgot it, used to
/// switch the two-step requirement off, or close the application to its invitation list,
/// without a trace. A change of <c>RequireTwoFactor</c> is audited
/// (<c>application.two-factor-requirement-changed</c>).
/// </para>
/// </summary>
public record UpdateApplicationRequest(
    string Name,
    string? Description = null,
    string? BaseUrl = null,
    string? LogoUrl = null,
    string? ContactEmail = null,
    bool AllowSelfRegistration = false,
    bool? RequireTwoFactor = null,
    bool RequireEmailVerification = false,
    int SessionTimeoutMinutes = 60,
    int MaxConcurrentSessions = 5,
    IReadOnlyList<string>? RedirectUris = null,
    int? ReauthenticationMaxAgeMinutes = null,
    ApplicationAccessMode? AccessMode = null,
    IReadOnlyList<string>? AllowedScopes = null,
    bool? AllowOrganizationCreation = null,
    Guid? OrganizationCreatorRoleId = null,
    string? LogoUrlDark = null,
    IReadOnlyList<string>? PostLogoutRedirectUris = null,
    IReadOnlyList<string>? ConfidentialRedirectUris = null,
    string? InitiateLoginUri = null);
