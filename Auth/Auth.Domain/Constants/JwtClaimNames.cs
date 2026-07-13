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
    /// Organization-scoped permission from the user's membership role, one
    /// claim per code. Custom claim: "org_perm",
    /// value "{organizationId}:{permissionCode}".
    /// </summary>
    public const string OrgPermissions = "org_perm";

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
