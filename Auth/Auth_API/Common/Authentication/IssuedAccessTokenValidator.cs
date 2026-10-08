using System.Security.Claims;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Common.Authentication;

/// <summary>
/// <see cref="IIssuedAccessTokenValidator"/> over the two bearer schemes' own parameters: the
/// platform profile first, then the application (userinfo) profile; the first success wins.
/// </summary>
/// <remarks>
/// It holds no rule of its own. <see cref="BearerSchemeRegistration"/> hands it both profiles from
/// <see cref="AccessTokenValidation"/>, with the settings and signing key it registers the schemes
/// with, so a token passes here exactly when one of the schemes would accept it on a request:
/// RS256 pinned, issuer, lifetime, and each profile's audience and type rules. The handler is the
/// one JwtBearer uses, without claim mapping, so <c>jti</c>, <c>exp</c> and <c>sid</c> keep their
/// names as they do on the schemes.
/// </remarks>
public sealed class IssuedAccessTokenValidator : IIssuedAccessTokenValidator
{
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly TokenValidationParameters[] _profiles;

    public IssuedAccessTokenValidator(TokenValidationParameters platform, TokenValidationParameters application)
    {
        _profiles = [platform, application];
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ClaimsPrincipal>> ValidateAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return AuthErrors.InvalidToken;
        }

        foreach (var profile in _profiles)
        {
            // Reports a bad token in the result rather than throwing, whatever is wrong with it.
            var result = await _handler.ValidateTokenAsync(token, profile);
            if (result.IsValid)
            {
                return new ClaimsPrincipal(result.ClaimsIdentity);
            }
        }

        return AuthErrors.InvalidToken;
    }
}
