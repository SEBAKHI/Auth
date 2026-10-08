using System.Text.Json.Serialization;
using Auth.Domain.Constants;

namespace Auth.Application.DTOs;

/// <summary>
/// DTO containing basic user information for responses.
/// </summary>
public record UserInfo
{
    /// <summary>
    /// Gets the user's unique identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The stable subject identifier, the one claim OIDC Core 5.3 requires a
    /// userinfo response to carry.
    /// </summary>
    /// <remarks>
    /// Computed from <see cref="Id"/> rather than stored beside it, so the two
    /// cannot drift apart. A standard client library reads "sub" to know which
    /// user it is looking at; without it the response is unusable to anything
    /// that follows the spec, however complete the rest of the fields are.
    /// </remarks>
    [JsonPropertyName("sub")]
    public string Sub => Id.ToString();

    /// <summary>
    /// Gets the user's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Gets the user's first name.
    /// </summary>
    public required string FirstName { get; init; }

    /// <summary>
    /// Gets the user's last name.
    /// </summary>
    public required string LastName { get; init; }

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the user's preferred language.
    /// </summary>
    public string? PreferredLanguage { get; init; }

    /// <summary>
    /// Gets the user's timezone.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>
    /// Gets the user's preferred UI theme (light, dark, or system).
    /// </summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Gets the user's roles.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// Gets the user's permissions.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>
    /// What the session must still prove before its token carries the account's
    /// platform permissions: "none", or — only while
    /// <c>TwoFactor:EnforceForPlatformAdmins</c> withholds them — "enroll",
    /// "step_up" or "reauthenticate". The token's <c>mfa_req</c>, echoed; the
    /// consoles route to the two-step page on anything but "none". An older API
    /// omits it, which a client reads as "none".
    /// </summary>
    public string MfaRequirement { get; init; } = MfaRequirementValues.None;
}
