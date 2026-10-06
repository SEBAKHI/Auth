namespace Auth.Domain.Constants;

/// <summary>
/// Constants for JWT claim names used throughout the authentication system.
/// These follow standard OIDC/JWT naming conventions and are used consistently
/// in token generation and validation.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>
    /// Subject - The unique identifier for the user (User ID).
    /// Standard JWT claim: "sub"
    /// </summary>
    public const string Subject = "sub";

    /// <summary>
    /// Email address of the user.
    /// Standard OIDC claim: "email"
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// Full display name of the user.
    /// Standard OIDC claim: "name"
    /// </summary>
    public const string Name = "name";

    /// <summary>
    /// Given name (first name) of the user.
    /// Standard OIDC claim: "given_name"
    /// </summary>
    public const string GivenName = "given_name";

    /// <summary>
    /// Family name (last name) of the user.
    /// Standard OIDC claim: "family_name"
    /// </summary>
    public const string FamilyName = "family_name";

    /// <summary>
    /// Preferred locale/language of the user.
    /// Standard OIDC claim: "locale"
    /// </summary>
    public const string Locale = "locale";

    /// <summary>
    /// Time zone of the user.
    /// Custom claim: "timezone"
    /// </summary>
    public const string TimeZone = "timezone";

    /// <summary>
    /// Preferred UI theme of the user (light, dark, or system).
    /// Custom claim: "theme"
    /// </summary>
    public const string Theme = "theme";

    /// <summary>
    /// Whether the user's email address has been verified (a JSON boolean).
    /// Standard OIDC claim: "email_verified"
    /// </summary>
    public const string EmailVerified = "email_verified";

    /// <summary>
    /// Time zone of the user, as an IANA name. The standard name for what
    /// access tokens carry as <see cref="TimeZone"/>.
    /// Standard OIDC claim: "zoneinfo"
    /// </summary>
    public const string ZoneInfo = "zoneinfo";

    /// <summary>
    /// Phone number of the user, as stored (free-form, not guaranteed E.164).
    /// Standard OIDC claim: "phone_number"
    /// </summary>
    public const string PhoneNumber = "phone_number";

    /// <summary>
    /// Whether the user's phone number has been verified (a JSON boolean).
    /// Standard OIDC claim: "phone_number_verified"
    /// </summary>
    public const string PhoneNumberVerified = "phone_number_verified";

    /// <summary>
    /// Absolute URL of the user's profile picture.
    /// Standard OIDC claim: "picture"
    /// </summary>
    public const string Picture = "picture";

    /// <summary>
    /// Organization-scoped permission from the user's membership role, one
    /// claim per code. Custom claim: "org_perm",
    /// value "{organizationId}:{permissionCode}".
    /// </summary>
    public const string OrgPermissions = "org_perm";

    /// <summary>
    /// The one organization in which the user holds this application's
    /// delegated permissions, as the organization's id (a GUID string). Only
    /// application tokens carry it, and only when there is exactly one such
    /// organization: none or several means no claim, and the relying party reads
    /// <see cref="OrgPermissions"/> instead. Custom claim: "org_id".
    /// </summary>
    public const string OrgId = "org_id";

    /// <summary>
    /// The display name of the organization in <see cref="OrgId"/>, emitted
    /// with it and never alone. It is text a user typed when creating the
    /// organization: a relying party encodes it on output and never treats it
    /// as a verified identity. Custom claim: "org_name".
    /// </summary>
    public const string OrgName = "org_name";

    /// <summary>
    /// Roles assigned to the user.
    /// Custom claim: "roles"
    /// </summary>
    public const string Roles = "roles";

    /// <summary>
    /// Permissions granted to the user.
    /// Custom claim: "permissions"
    /// </summary>
    public const string Permissions = "permissions";

    /// <summary>
    /// Scopes granted to the application the token was issued to, as ONE
    /// space-delimited string ("openid profile email"), never one claim per
    /// scope. Only application tokens carry it; platform tokens do not.
    /// Standard claim: "scope" (RFC 9068 §2.2.3, RFC 8693 §4.2)
    /// </summary>
    public const string Scope = "scope";

    /// <summary>
    /// JWT ID - Unique identifier for the token.
    /// Standard JWT claim: "jti"
    /// </summary>
    public const string JwtId = "jti";

    /// <summary>
    /// Session ID - Stable identifier of the login session, constant across
    /// access-token refreshes. Custom claim: "sid"
    /// </summary>
    public const string Sid = "sid";

    /// <summary>
    /// Issued At - Timestamp when the token was issued.
    /// Standard JWT claim: "iat"
    /// </summary>
    public const string IssuedAt = "iat";

    /// <summary>
    /// Expiration Time - Timestamp when the token expires.
    /// Standard JWT claim: "exp"
    /// </summary>
    public const string Expiration = "exp";

    /// <summary>
    /// Issuer - The entity that issued the token.
    /// Standard JWT claim: "iss"
    /// </summary>
    public const string Issuer = "iss";

    /// <summary>
    /// Audience - The intended recipient of the token.
    /// Standard JWT claim: "aud"
    /// </summary>
    public const string Audience = "aud";
}
