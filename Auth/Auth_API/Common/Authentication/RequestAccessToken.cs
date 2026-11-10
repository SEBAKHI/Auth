using System.Globalization;
using System.Security.Claims;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;

namespace Auth_API.Common.Authentication;

/// <summary>
/// Reads <see cref="ICurrentAccessToken.IssuedAtUtc"/> from the current request's authenticated
/// principal: the <c>iat</c> of the bearer token its scheme validated, never a header read apart
/// from that validation. Outside a request, for a principal without the claim, or for a value that
/// is not a time in seconds, there is none.
/// </summary>
public sealed class RequestAccessToken(IHttpContextAccessor httpContextAccessor) : ICurrentAccessToken
{
    // The last second DateTimeOffset.FromUnixTimeSeconds takes: 9999-12-31T23:59:59Z.
    private const long LastUnixSecond = 253_402_300_799;

    /// <inheritdoc />
    public DateTime? IssuedAtUtc =>
        long.TryParse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(JwtClaimNames.IssuedAt),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seconds)
        && seconds is > 0 and <= LastUnixSecond
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
}
