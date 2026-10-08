using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Interfaces;

/// <summary>
/// How the session behind an access token was authenticated, and what it must
/// still prove: the token's <c>amr</c>, <c>auth_time</c> and <c>mfa_req</c>.
/// </summary>
/// <param name="Methods">
/// What the session proved. <see cref="AuthenticationMethods.Unknown"/> emits
/// neither <c>amr</c> nor <c>auth_time</c>: an application token, or a session
/// recorded before methods were, claims nothing it cannot show.
/// </param>
/// <param name="AuthTime">
/// When the session signed in — its start, the same instant the session row
/// records — or null when unknown. Emitted only together with
/// <paramref name="Methods"/>.
/// </param>
/// <param name="Requirement">
/// What the session must still prove while its platform authority is withheld;
/// <see cref="MfaRequirement.None"/> emits no <c>mfa_req</c>.
/// </param>
public sealed record AccessTokenAuthentication(
    AuthenticationMethods Methods,
    DateTimeOffset? AuthTime,
    MfaRequirement Requirement)
{
    /// <summary>
    /// Nothing recorded and nothing required: the authentication of an
    /// application token.
    /// </summary>
    public static AccessTokenAuthentication Unrecorded { get; } =
        new(AuthenticationMethods.Unknown, null, MfaRequirement.None);

    /// <summary>Gets whether <c>amr</c> and <c>auth_time</c> are emitted.</summary>
    public bool IsRecorded => !Methods.IsUnknown && AuthTime.HasValue;
}
