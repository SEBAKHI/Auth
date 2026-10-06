using System.Security.Claims;
using System.Text.Json.Serialization;
using Auth.Domain.Constants;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetOidcUserInfo;

/// <summary>
/// The OpenID Connect UserInfo request (OIDC Core §5.3) of an application's access token: who
/// the token names, and which scopes it was granted.
/// </summary>
/// <param name="UserId">The token's <c>sub</c>; <see cref="Guid.Empty"/> when it is not a user id.</param>
/// <param name="Scope">
/// The token's <c>scope</c> claim, space-delimited, or null for a token minted before scopes
/// existed (read as <c>openid</c> only).
/// </param>
public record GetOidcUserInfoQuery(Guid UserId, string? Scope) : IRequest<ErrorOr<OidcUserInfoResponse>>
{
    /// <summary>
    /// Reads the query from the principal the userinfo scheme validated. The only reader of the
    /// token's claims here: the profile itself comes from the database.
    /// </summary>
    public static GetOidcUserInfoQuery FromPrincipal(ClaimsPrincipal principal) =>
        new(
            Guid.TryParse(principal.FindFirst(JwtClaimNames.Subject)?.Value, out var userId) ? userId : Guid.Empty,
            principal.FindFirst(JwtClaimNames.Scope)?.Value);
}

/// <summary>
/// The UserInfo response: the standard claims the token's scopes allow, read from the user's
/// row at the call. A member is absent when its scope was not granted or the user has no value.
/// </summary>
/// <remarks>
/// Every name is pinned to the OIDC claim name, so the API's camelCase policy cannot rename it,
/// and every optional member is dropped when null whatever the serializer's defaults are.
/// Never <c>roles</c>, <c>permissions</c>, <c>org_perm</c> or <c>scope</c>: an application reads
/// the user's profile here, not the platform's authority over them.
/// </remarks>
public record OidcUserInfoResponse
{
    [JsonPropertyName(JwtClaimNames.Subject)]
    public required string Sub { get; init; }

    [JsonPropertyName(JwtClaimNames.Name)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName(JwtClaimNames.GivenName)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GivenName { get; init; }

    [JsonPropertyName(JwtClaimNames.FamilyName)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FamilyName { get; init; }

    [JsonPropertyName(JwtClaimNames.Locale)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; init; }

    [JsonPropertyName(JwtClaimNames.ZoneInfo)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZoneInfo { get; init; }

    [JsonPropertyName(JwtClaimNames.Picture)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Picture { get; init; }

    [JsonPropertyName(JwtClaimNames.Email)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }

    [JsonPropertyName(JwtClaimNames.EmailVerified)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailVerified { get; init; }

    [JsonPropertyName(JwtClaimNames.PhoneNumber)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhoneNumber { get; init; }

    [JsonPropertyName(JwtClaimNames.PhoneNumberVerified)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PhoneNumberVerified { get; init; }
}
